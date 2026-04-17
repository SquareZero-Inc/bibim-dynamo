// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Dynamo.Graph.Nodes;
using Dynamo.Models;
using Dynamo.ViewModels;
using Dynamo.Wpf.Extensions;

namespace BIBIM_MVP
{
    /// <summary>
    /// Section 1: Unified Chat Workspace ViewModel
    /// Handles both generation and analysis modes in single interface
    /// </summary>
    public class ChatWorkspaceViewModel : INotifyPropertyChanged
    {
        private readonly ViewLoadedParams _viewLoadedParams;
        private StringBuilder _htmlContent = new StringBuilder();
        private string _lastUserMessage;
        private string _pendingInputText; // 취소 시 복원할 입력 텍스트
        private CancellationTokenSource _loadingCts;
        private CancellationTokenSource _requestCts; // API 요청 취소용

        // Node manipulation for Group/Zoom features
        private readonly NodeManipulator _nodeManipulator;
        
        // Local session management
        private readonly LocalSessionManager _localSessionManager;
        private ChatSession _currentSession;
        
        // 대화 히스토리 저장 (API 호출 시 컨텍스트 유지)
        private readonly List<ChatMessage> _conversationHistory = new List<ChatMessage>();

        // Error-resilient context management
        private ConversationContextManager _contextManager;

        // Spec-first code generation
        private readonly SpecificationManager _specManager;

        // Generation pipeline orchestrator (separates pipeline concerns from ViewModel)
        private readonly GenerationPipelineService _pipeline;
        
        // Bypass prefix for direct code generation
        private const string BypassPrefix = "!direct ";
        private const string BypassPrefixAlt = "/direct ";
        
        private string _lastGeneratedPythonNodeGuid;
        private CodeSpecification _lastConfirmedSpec;
        private DateTime _lastConfirmedSpecAtUtc = DateTime.MinValue;

        // Event for UI updates
        public event EventHandler<string> MessagesUpdated;

        // Event for version warning banner
        public event EventHandler<(string sessionVersion, string currentVersion)> VersionMismatchDetected;

        private static string L(string key) => LocalizationService.Get(key);

        private static string LF(string key, params object[] args) => LocalizationService.Format(key, args);

        private static string CreateRequestId()
        {
            return $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        private static void LogPerf(string requestId, string step, long elapsedMs, string detail = null)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return;
            string suffix = string.IsNullOrWhiteSpace(detail) ? "" : $" detail={detail}";
            Logger.Log("ChatWorkspaceViewModel", $"[PERF] rid={requestId} step={step} ms={elapsedMs}{suffix}");
        }

        #region Properties

        private string _inputText;
        public string InputText
        {
            get => _inputText;
            set { _inputText = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private bool _isHistoryPanelVisible;
        public bool IsHistoryPanelVisible
        {
            get => _isHistoryPanelVisible;
            set { _isHistoryPanelVisible = value; OnPropertyChanged(); }
        }

        public ObservableCollection<HistoryEntry> HistoryList { get; } = new ObservableCollection<HistoryEntry>();

        /// <summary>
        /// Exposes SpecificationManager state for UI binding.
        /// </summary>
        public bool HasPendingSpec => _specManager.HasPendingSpec;

        /// <summary>
        /// Requirement: 2.1, 8.1
        /// </summary>
        private bool _isRetryButtonVisible;
        public bool IsRetryButtonVisible
        {
            get => _isRetryButtonVisible;
            set { _isRetryButtonVisible = value; OnPropertyChanged(); }
        }

        #endregion

        #region Commands

        public ICommand SendCommand { get; }
        public ICommand ToggleHistoryPanelCommand { get; }
        public ICommand LoadHistoryCommand { get; }
        public ICommand NewChatCommand { get; }

        /// <summary>
        /// Bound to confirm button in spec card.
        /// </summary>
        public ICommand ConfirmSpecCommand { get; }

        /// <summary>
        /// Bound to modify button in spec card.
        /// </summary>
        public ICommand RequestChangesCommand { get; }

        /// <summary>
        /// </summary>
        public ICommand CancelSpecCommand { get; }

        /// <summary>
        /// Requirement: 2.1
        /// </summary>
        public ICommand RetryCommand { get; }

        /// <summary>
        /// Command to cancel ongoing API request.
        /// </summary>
        public ICommand CancelCommand { get; }

        #endregion

        private readonly string[] _loadingMessages = { LocalizationService.Get("ViewModel_LoadingGenerating") };
        private int _currentLoadingStep = 0;

        public ChatWorkspaceViewModel(ViewLoadedParams viewLoadedParams)
        {
            _viewLoadedParams = viewLoadedParams;
            _localSessionManager = new LocalSessionManager();
            _nodeManipulator = new NodeManipulator(viewLoadedParams);


            _specManager = new SpecificationManager();
            _specManager.SpecStateChanged += OnSpecStateChanged;

            // Pipeline orchestrator: delegates status updates back to ViewModel.StatusText
            _pipeline = new GenerationPipelineService(text => StatusText = text);

            SendCommand = new RelayCommand(async (obj) => await SendMessageAsync());
            ToggleHistoryPanelCommand = new RelayCommand(async (obj) => await ToggleHistoryPanelAsync());
            LoadHistoryCommand = new RelayCommand(async (obj) => await LoadHistoryEntryAsync(obj));
            NewChatCommand = new RelayCommand((obj) => { StartNewChat(); return Task.CompletedTask; });


            ConfirmSpecCommand = new RelayCommand(async (obj) => await ConfirmSpecAsync());
            RequestChangesCommand = new RelayCommand((obj) => { RequestChanges(); return Task.CompletedTask; });
            CancelSpecCommand = new RelayCommand((obj) => { CancelSpec(); return Task.CompletedTask; });


            RetryCommand = new RelayCommand(async (obj) => await HandleRetryAsync(), (obj) => !IsBusy);

            // Initialize cancel command
            CancelCommand = new RelayCommand((obj) => { CancelRequest(); return Task.CompletedTask; }, (obj) => IsBusy);

            // Start with a new session
            StartNewChat();

            InitializeUserTracking();
            _ = RefreshHistoryListAsync();
        }

        /// <summary>
        /// </summary>
        private void OnSpecStateChanged(object sender, SpecStateChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasPendingSpec));
        }

        /// <summary>
        /// Starts a new chat session, clearing current conversation.
        /// Requirements: 4.2
        /// </summary>
        private void StartNewChat()
        {
            _currentSession = _localSessionManager.CreateSession();
            ClearConversationHistory();
            TokenTracker.ResetSession();


            _specManager.ClearPendingSpec();

            _lastGeneratedPythonNodeGuid = null;
            _lastConfirmedSpec = null;
            _lastConfirmedSpecAtUtc = DateTime.MinValue;
            

            if (_contextManager != null)
            {
                _contextManager.StartNewSession(_currentSession.SessionId);
            }
            

            IsRetryButtonVisible = false;
            
            MessagesUpdated?.Invoke(this, "");
            
            // Hide version warning banner
            VersionMismatchDetected?.Invoke(this, (null, null));
        }

        private void InitializeUserTracking()
        {
            try
            {
                InitializeContextManager();
                Logger.Log("ChatWorkspaceViewModel", "InitializeUserTracking: local session ready");
            }
            catch (Exception ex)
            {
                Logger.Log("ChatWorkspaceViewModel", $"InitializeUserTracking failed: {ex.Message}");
            }
        }

        private void InitializeContextManager()
        {
            try
            {
                _contextManager = new ConversationContextManager(_localSessionManager);
                if (_currentSession != null)
                    _contextManager.StartNewSession(_currentSession.SessionId);
                Logger.Log("ChatWorkspaceViewModel", "ConversationContextManager initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Log("ChatWorkspaceViewModel", $"Failed to initialize ConversationContextManager: {ex.Message}");
            }
        }

        /// <summary>
        /// Append HTML message to chat and notify UI
        /// </summary>
        public void AppendHtmlMessage(string html)
        {
            _htmlContent.Append(html);
            MessagesUpdated?.Invoke(this, _htmlContent.ToString());
        }

        /// <summary>
        /// Requirements: 3.1, 3.2
        /// </summary>
        /// <param name="errorMessage">The user-friendly error message to display</param>
        public void AppendErrorMessageToChat(string errorMessage)
        {
            AppendHtmlMessage(ChatHtmlBuilder.ErrorBubble(EscapeHtml(errorMessage)));
        }

        /// <summary>
        /// Save analysis result to history (exposed for ChatWorkspace)
        /// </summary>
        public void SaveAnalysisToHistory(string analysisReport)
        {
            if (string.IsNullOrEmpty(analysisReport)) return;
            
            // 분석 결과를 _conversationHistory에 추가하여 이후 대화에서 컨텍스트 유지
            _conversationHistory.Add(new ChatMessage { Text = analysisReport, IsUser = false });
            
            // ConversationContextManager에도 추가
            if (_contextManager != null)
            {
                _contextManager.AddTurn(L("Chat_AnalyzeButton"), analysisReport, isError: false);
                _contextManager.SaveSession();
            }
            
            SaveSingleMessage("assistant", "analysis", analysisReport);
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(InputText) || IsBusy) return;

            if (InputText.Length > 5000)
            {
                AppendHtmlMessage(ChatHtmlBuilder.SimpleErrorBubble(EscapeHtml(L("ViewModel_MessageTooLong"))));
                return;
            }

            string userMsg = InputText;
            _lastUserMessage = userMsg;
            _pendingInputText = userMsg; // 취소 시 복원용 저장
            InputText = "";
            
            // 요청 취소 토큰 초기화
            _requestCts?.Cancel();
            _requestCts?.Dispose();
            _requestCts = new CancellationTokenSource();

            string requestId = CreateRequestId();
            var requestTotalSw = Stopwatch.StartNew();
            Logger.Log("ChatWorkspaceViewModel", $"[PERF] rid={requestId} step=request-start detail=prompt_len:{userMsg.Length}");

            // Add user message to chat
            AppendHtmlMessage(ChatHtmlBuilder.UserBubble(EscapeHtml(userMsg)));

            // Save user message to history (new format)
            SaveSingleMessage("user", "text", userMsg, null, requestId);


            await HandleSpecFirstFlowAsync(userMsg, requestId);
            requestTotalSw.Stop();
            LogPerf(requestId, "request-total", requestTotalSw.ElapsedMilliseconds);
        }

        /// <summary>
        /// - Check if bypass prefix is present (e.g., "!direct ")
        /// - If bypass: call existing direct code generation flow
        /// - If no pending spec: call SpecGenerator.GenerateSpecificationAsync()
        /// - If pending spec exists: call SpecGenerator.ReviseSpecificationAsync()
        /// </summary>
        /// <param name="userMessage">The user's message.</param>
        private async Task HandleSpecFirstFlowAsync(string userMessage, string requestId = null)
        {
            var flowSw = Stopwatch.StartNew();
            IsBusy = true;
            _loadingCts = new CancellationTokenSource();
            _ = CycleLoadingMessagesAsync(_loadingCts.Token);



            try
            {
                // 취소 확인
                if (_requestCts?.Token.IsCancellationRequested == true)
                {
                    HandleCancellation();
                    return;
                }

                // .dyn 파일 생성 요청 안내
                string lowerMsg = userMessage.ToLower();
                if (lowerMsg.Contains(".dyn") ||
                    (lowerMsg.Contains("dyn") && (lowerMsg.Contains("파일") || lowerMsg.Contains("저장") || lowerMsg.Contains("생성") || lowerMsg.Contains("만들") || lowerMsg.Contains("export") || lowerMsg.Contains("save") || lowerMsg.Contains("file") || lowerMsg.Contains("generate") || lowerMsg.Contains("create") || lowerMsg.Contains("make"))))
                {
                    AppendHtmlMessage(ChatHtmlBuilder.AiBubble(
                        $"<p class='warning'>{EscapeHtml(L("ViewModel_DynNotSupportedTitle"))}</p>" +
                        $"<p>{EscapeHtml(L("ViewModel_DynNotSupportedBody"))}</p>" +
                        $"<p class='hint'>{EscapeHtml(L("ViewModel_DynNotSupportedHint"))}</p>"));
                    IsBusy = false;
                    _loadingCts?.Cancel();
                    StatusText = "";
                    return;
                }


                bool isBypass = userMessage.StartsWith(BypassPrefix, StringComparison.OrdinalIgnoreCase) ||
                               userMessage.StartsWith(BypassPrefixAlt, StringComparison.OrdinalIgnoreCase);

                if (isBypass)
                {
                    // Strip bypass prefix and use direct code generation
                    string strippedMessage = userMessage;
                    if (userMessage.StartsWith(BypassPrefix, StringComparison.OrdinalIgnoreCase))
                        strippedMessage = userMessage.Substring(BypassPrefix.Length);
                    else if (userMessage.StartsWith(BypassPrefixAlt, StringComparison.OrdinalIgnoreCase))
                        strippedMessage = userMessage.Substring(BypassPrefixAlt.Length);

                    await DirectCodeGenerationAsync(strippedMessage, requestId);
                    return;
                }

                // 취소 확인
                if (_requestCts?.Token.IsCancellationRequested == true)
                {
                    HandleCancellation();
                    return;
                }


                if (_specManager.HasPendingSpec)
                {
                    // Route to revision flow - user message is treated as feedback
                    await ReviseSpecificationAsync(userMessage, requestId);
                }
                else if (ShouldReviseLastConfirmedSpec(userMessage))
                {
                    // Restore last confirmed spec as pending so follow-up edits revise instead of generate.
                    var restoredSpec = CloneSpecificationForRevision(_lastConfirmedSpec);
                    if (restoredSpec != null)
                    {
                        _specManager.SetPendingSpec(restoredSpec);
                        await ReviseSpecificationAsync(userMessage, requestId);
                    }
                    else
                    {
                        await GenerateNewSpecificationAsync(userMessage, requestId);
                    }
                }
                else
                {
                    // Generate new specification
                    await GenerateNewSpecificationAsync(userMessage, requestId);
                }
            }
            catch (OperationCanceledException)
            {
                // 취소된 경우 - 이미 HandleCancellation에서 처리됨
            }
            catch (Exception ex)
            {
                if (_requestCts?.Token.IsCancellationRequested == true)
                {
                    HandleCancellation();
                    return;
                }
                AppendHtmlMessage(ChatHtmlBuilder.SimpleErrorBubble(EscapeHtml(LF("ViewModel_SystemError", ex.Message))));
            }
            finally
            {
                flowSw.Stop();
                LogPerf(requestId, "flow", flowSw.ElapsedMilliseconds);
                IsBusy = false;
                _loadingCts?.Cancel();
                StatusText = "";
            }
        }

        /// <summary>
        /// Direct code generation flow (bypass spec-first).
        /// Uses the existing GeminiService flow.
        /// </summary>
        private async Task DirectCodeGenerationAsync(string userMessage, string requestId = null)
        {
            var directSw = Stopwatch.StartNew();
            try
            {
                // 취소 확인
                if (_requestCts?.Token.IsCancellationRequested == true)
                {
                    HandleCancellation();
                    return;
                }

                // Build message history for API (includes current message)
                var messages = BuildMessageHistory(userMessage);
                GenerationResult result = await _pipeline.RunAsync(messages, requestId, _requestCts?.Token ?? default);

                // 취소 확인
                if (_requestCts?.Token.IsCancellationRequested == true)
                {
                    HandleCancellation();
                    return;
                }

                RecordSuccessfulTurn(userMessage, result.RawResponse);


                ProcessAndDisplayResponse(result, requestId);

            }
            catch (Exception ex)
            {

                await HandleApiError(userMessage, ex);
            }
            finally
            {
                directSw.Stop();
                LogPerf(requestId, "code-flow", directSw.ElapsedMilliseconds, "direct");
            }
        }

        /// <summary>
        /// Generate a new specification from user request.
        /// If the response is a general chat (not code request), display it directly.
        /// If the spec has clarifying questions, display them as conversational questions first.
        /// Only show the spec card when all questions are resolved.
        /// </summary>
        private async Task GenerateNewSpecificationAsync(string userMessage, string requestId = null)
        {
            try
            {
                // 취소 확인
                if (_requestCts?.Token.IsCancellationRequested == true)
                {
                    HandleCancellation();
                    return;
                }

                // Call SpecGenerator to create specification
                var specSw = Stopwatch.StartNew();
                var spec = await SpecGenerator.GenerateSpecificationAsync(userMessage, GetConversationHistoryForSpec(), requestId, _requestCts?.Token ?? default);
                specSw.Stop();
                LogPerf(requestId, "spec", specSw.ElapsedMilliseconds, "generate");

                // 취소 확인
                if (_requestCts?.Token.IsCancellationRequested == true)
                {
                    HandleCancellation();
                    return;
                }

                // Check if this is a general chat response (not a code request)
                if (spec.IsChatResponse)
                {
                    // Display as normal chat message
                    string chatHtml = ConvertMarkdownToHtml(spec.ChatResponseText);
                    AppendHtmlMessage(ChatHtmlBuilder.AiBubble(chatHtml));

                    RecordSuccessfulTurn(userMessage, spec.ChatResponseText);

                    // Save to history (new format)
                    SaveSingleMessage("assistant", "text", spec.ChatResponseText, null, requestId);

                    // Track AI response

                    
                    // 응답 완료 알림
                    NotificationHelper.ShowResponseNotification(isCodeGenerated: false);
                    return;
                }

                // Check if there are clarifying questions
                if (spec.ClarifyingQuestions != null && spec.ClarifyingQuestions.Count > 0)
                {
                    AppendHtmlMessage(ChatHtmlBuilder.QuestionFormBubble(
                        EscapeHtml(L("ViewModel_AdditionalInfoNeeded")),
                        EscapeHtml(L("ViewModel_QuestionsIntroShort")),
                        spec.ClarifyingQuestionsStructured,
                        L("ViewModel_QuestionOtherPlaceholder"),
                        L("ViewModel_QuestionSubmit"),
                        L("ViewModel_QuestionValidation")));

                    // Store the partial spec as pending so user answers can revise it
                    _specManager.SetPendingSpec(spec);

                    // Flat text used only for history/logging (not for the form)
                    string questionsText = string.Join("\n", spec.ClarifyingQuestions);
                    RecordSuccessfulTurn(userMessage, $"TYPE: QUESTION|{questionsText}");

                    // Save AI question to history (new format)
                    SaveSingleMessage("assistant", "question", questionsText, null, requestId);

                    // 응답 완료 알림
                    NotificationHelper.ShowResponseNotification(isCodeGenerated: false);
                }
                else
                {
                    string specJson = JsonHelper.Serialize(spec);

                    // No questions - show the spec card
                    _specManager.SetPendingSpec(spec);

                    // Display formatted spec HTML
                    string specHtml = SpecGenerator.FormatSpecificationHtml(spec);
                    AppendHtmlMessage(ChatHtmlBuilder.AiBubble(specHtml));

                    // Context gets raw JSON (recovery), history gets a readable summary (API context)
                    string specSummary = $"[SPEC] {spec.OriginalRequest}\nSteps: {string.Join(", ", spec.ProcessingSteps ?? new System.Collections.Generic.List<string>())}\nOutput: {spec.Output?.Description ?? "N/A"}";
                    RecordSuccessfulTurn(userMessage, $"SPEC_GENERATED|{specJson}", specSummary);

                    // Save spec to history (new format)
                    SaveSingleMessage("assistant", "spec", specJson, null, requestId);

                    // 명세서 생성 완료 알림
                    NotificationHelper.ShowBalloonTip(
                        L("Common_BibimAi"),
                        L("ViewModel_SpecCreatedNotification"));
                }
            }
            catch (Exception ex)
            {

                await HandleApiError(userMessage, ex);
            }
        }

        /// <summary>
        /// Revise existing specification based on user feedback.
        /// </summary>
        private async Task ReviseSpecificationAsync(string userFeedback, string requestId = null)
        {
            try
            {
                // 취소 확인
                if (_requestCts?.Token.IsCancellationRequested == true)
                {
                    HandleCancellation();
                    return;
                }

                var currentSpec = _specManager.GetPendingSpec();
                if (currentSpec == null)
                {
                    // No pending spec, treat as new request
                    await GenerateNewSpecificationAsync(userFeedback, requestId);
                    return;
                }

                // Call SpecGenerator to revise specification with conversation history
                var specSw = Stopwatch.StartNew();
                var revisedSpec = await SpecGenerator.ReviseSpecificationAsync(currentSpec, userFeedback, GetConversationHistoryForSpec(), requestId, _requestCts?.Token ?? default);
                specSw.Stop();
                LogPerf(requestId, "spec", specSw.ElapsedMilliseconds, "revise");

                // 취소 확인
                if (_requestCts?.Token.IsCancellationRequested == true)
                {
                    HandleCancellation();
                    return;
                }

                // Check if this is a general chat response (not a revision)
                if (revisedSpec.IsChatResponse)
                {
                    // Display as normal chat message
                    string chatHtml = ConvertMarkdownToHtml(revisedSpec.ChatResponseText);
                    AppendHtmlMessage(ChatHtmlBuilder.AiBubble(chatHtml));

                    RecordSuccessfulTurn(userFeedback, revisedSpec.ChatResponseText);

                    // Save to history (new format)
                    SaveSingleMessage("assistant", "text", revisedSpec.ChatResponseText, null, requestId);

                    // Track AI response

                    
                    // Clear pending spec since the conversation shifted away from code generation
                    _specManager.ClearPendingSpec();

                    
                    // 응답 완료 알림
                    NotificationHelper.ShowResponseNotification(isCodeGenerated: false);
                    return;
                }

                // Check if there are clarifying questions - display as conversational questions only
                if (revisedSpec.ClarifyingQuestions != null && revisedSpec.ClarifyingQuestions.Count > 0)
                {
                    AppendHtmlMessage(ChatHtmlBuilder.QuestionFormBubble(
                        EscapeHtml(L("ViewModel_AdditionalInfoNeeded")),
                        EscapeHtml(L("ViewModel_QuestionsIntroShort")),
                        revisedSpec.ClarifyingQuestionsStructured,
                        L("ViewModel_QuestionOtherPlaceholder"),
                        L("ViewModel_QuestionSubmit"),
                        L("ViewModel_QuestionValidation")));

                    // Store the partial spec as pending so user answers can revise it
                    _specManager.SetPendingSpec(revisedSpec);

                    // Flat text used only for history/logging (not for the form)
                    string questionsText = string.Join("\n", revisedSpec.ClarifyingQuestions);
                    RecordSuccessfulTurn(userFeedback, $"TYPE: QUESTION|{questionsText}");

                    // Save AI question to history (new format)
                    SaveSingleMessage("assistant", "question", questionsText, null, requestId);

                    // Track AI response



                    
                    // 응답 완료 알림
                    NotificationHelper.ShowResponseNotification(isCodeGenerated: false);
                    return;
                }

                // No questions - show the spec card (all ambiguities resolved)
                string specJson = JsonHelper.Serialize(revisedSpec);

                // Update pending spec
                _specManager.SetPendingSpec(revisedSpec);

                // Display formatted revised spec HTML
                string specHtml = SpecGenerator.FormatSpecificationHtml(revisedSpec);
                AppendHtmlMessage(ChatHtmlBuilder.AiBubble(specHtml));

                // Context gets raw JSON (recovery), history gets a readable summary (API context)
                string specSummary = $"[SPEC_REVISED] {revisedSpec.OriginalRequest}\nSteps: {string.Join(", ", revisedSpec.ProcessingSteps ?? new System.Collections.Generic.List<string>())}\nOutput: {revisedSpec.Output?.Description ?? "N/A"}";
                RecordSuccessfulTurn(userFeedback, $"SPEC_REVISED|{specJson}", specSummary);

                // Save revised spec to history (new format)
                SaveSingleMessage("assistant", "spec", specJson, null, requestId);


                
                // 명세서 수정 완료 알림
                NotificationHelper.ShowBalloonTip(
                    L("Common_BibimAi"),
                    L("ViewModel_SpecRevisedNotification"));
            }
            catch (Exception ex)
            {

                await HandleApiError(userFeedback, ex);
            }
        }

        /// <summary>
        /// Confirms the pending specification and generates code.
        /// </summary>
        private async Task ConfirmSpecAsync()
        {
            // If already generating code, show friendly message instead of error
            if (IsBusy)
            {
                AppendHtmlMessage(ChatHtmlBuilder.AiBubble($"<p class='info'>{EscapeHtml(L("ViewModel_CodeGeneratingInProgress"))}</p>"));
                return;
            }

            if (!_specManager.HasPendingSpec)
            {
                AppendHtmlMessage(ChatHtmlBuilder.SimpleWarningBubble(EscapeHtml(L("ViewModel_NoSpecToConfirm"))));
                return;
            }

            var spec = _specManager.GetPendingSpec();
            string requestId = CreateRequestId();
            var requestTotalSw = Stopwatch.StartNew();
            Logger.Log("ChatWorkspaceViewModel", $"[PERF] rid={requestId} step=request-start detail=confirm-spec");

            // 새 요청 취소 토큰 생성
            _requestCts?.Cancel();
            _requestCts?.Dispose();
            _requestCts = new CancellationTokenSource();

            try
            {
                _lastConfirmedSpec = CloneSpecificationForRevision(spec);
                _lastConfirmedSpecAtUtc = DateTime.UtcNow;

                // Generate code from confirmed spec (API call first)
                // Note: ConfirmPendingSpec is called AFTER successful code generation
                // to preserve spec state if API fails (e.g., 529 overload error)
                bool success = await GenerateCodeFromSpecAsync(spec, requestId);
                
                // Only clear pending state if code generation succeeded
                if (success)
                {
                    _specManager.ConfirmPendingSpec();
                }
            }
            finally
            {
                requestTotalSw.Stop();
                LogPerf(requestId, "request-total", requestTotalSw.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Builds prompt including spec details and calls GeminiService.
        /// </summary>
        /// <param name="spec">The confirmed specification.</param>
        /// <returns>True if code generation succeeded, false if an error occurred.</returns>
        private async Task<bool> GenerateCodeFromSpecAsync(CodeSpecification spec, string requestId = null)
        {
            if (spec == null) return false;

            IsBusy = true;
            _loadingCts = new CancellationTokenSource();
            _ = CycleLoadingMessagesAsync(_loadingCts.Token);
            var codeFlowSw = Stopwatch.StartNew();
            bool success = false;

            try
            {
                // Build spec-enhanced prompt
                string specPrompt = BuildSpecEnhancedPrompt(spec);

                // Add to conversation history
                _conversationHistory.Add(new ChatMessage { Text = specPrompt, IsUser = true });

                // Build message history for API
                var messages = new List<ChatMessage>(_conversationHistory);

                // Call GeminiService with spec-enhanced prompt
                GenerationResult result = await _pipeline.RunAsync(messages, requestId, _requestCts?.Token ?? default);

                RecordSuccessfulTurn(specPrompt, result.RawResponse);

                ProcessAndDisplayResponse(result, requestId);

                // Save to history with spec data
                SaveToHistoryWithSpec(spec.OriginalRequest, result, spec, requestId);

                success = !result.IsValidationBlock;
            }
            catch (Exception ex)
            {

                await HandleApiError(spec.OriginalRequest, ex);
                success = false;
            }
            finally
            {
                codeFlowSw.Stop();
                LogPerf(requestId, "code-flow", codeFlowSw.ElapsedMilliseconds, "confirmed-spec");
                IsBusy = false;
                _loadingCts?.Cancel();
                StatusText = "";
            }
            
            return success;
        }

        /// <summary>
        /// Ensures the code generator has full context from the confirmed spec.
        /// </summary>
        private string BuildSpecEnhancedPrompt(CodeSpecification spec)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[SPEC_CONFIRMED|spec_id={spec.SpecId}|lang={AppLanguage.Current}|v=1]");
            sb.AppendLine(L("ViewModel_ConfirmedSpecPromptHeader"));
            sb.AppendLine();
            sb.AppendLine(LF("ViewModel_OriginalRequest", spec.OriginalRequest));
            sb.AppendLine();
            sb.AppendLine(L("ViewModel_InputsHeader"));
            foreach (var input in spec.Inputs)
            {
                sb.AppendLine($"- {input.Name} ({input.Type}): {input.Description}");
            }
            sb.AppendLine();
            sb.AppendLine(L("ViewModel_StepsHeader"));
            for (int i = 0; i < spec.ProcessingSteps.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {spec.ProcessingSteps[i]}");
            }
            sb.AppendLine();
            sb.AppendLine(L("ViewModel_OutputHeader"));
            sb.AppendLine(LF("ViewModel_OutputType", spec.Output.Type));
            sb.AppendLine(LF("ViewModel_OutputDescription", spec.Output.Description));
            if (!string.IsNullOrEmpty(spec.Output.Unit))
            {
                sb.AppendLine(LF("ViewModel_OutputUnit", spec.Output.Unit));
            }
            sb.AppendLine();
            sb.AppendLine(L("ViewModel_GenerateCodeFromSpec"));
            sb.AppendLine();
            sb.AppendLine(L("ViewModel_ResponseFormatRequired"));
            sb.AppendLine(L("ViewModel_ResponseFormatCode"));
            sb.AppendLine(L("ViewModel_ResponseFormatGuide"));

            return sb.ToString();
        }

        /// <summary>
        /// Requirements: 3.1, 3.2, 3.3, 7.1, 7.5
        /// </summary>
        private async Task HandleApiError(string userMessage, Exception ex)
        {
            try
            {
                // 취소된 경우 별도 처리
                if (ex is OperationCanceledException || _requestCts?.Token.IsCancellationRequested == true)
                {
                    HandleCancellation();
                    return;
                }

                // Enhanced error logging for debugging
                string requestId = CreateRequestId();
                string errorType = ClassifyError(ex);
                
                // Log detailed error information
                Logger.Log("ChatWorkspaceViewModel", $"[API_ERROR] rid={requestId} type={errorType} exception={ex.GetType().Name}");
                Logger.Log("ChatWorkspaceViewModel", $"[API_ERROR] rid={requestId} message={ex.Message}");
                
                // Log inner exception if exists
                if (ex.InnerException != null)
                {
                    Logger.Log("ChatWorkspaceViewModel", $"[API_ERROR] rid={requestId} inner_exception={ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
                
                // Log stack trace (truncated for readability)
                if (ex.StackTrace != null)
                {
                    string truncatedStack = ex.StackTrace.Length > 500 
                        ? ex.StackTrace.Substring(0, 500) + "..." 
                        : ex.StackTrace;
                    Logger.Log("ChatWorkspaceViewModel", $"[API_ERROR] rid={requestId} stack={truncatedStack.Replace("\n", " | ")}");
                }

                // Create retry context ()
                if (_contextManager != null)
                {
                    _contextManager.CreateRetryContext(userMessage, errorType);
                    _contextManager.AddTurn(userMessage, null, isError: true);
                    _contextManager.SaveSession();
                }

                // Determine error message based on error type and consecutive error count ()
                string errorMessage;
                
                // AI 서버 오류인 경우 유저 친화적 메시지 표시
                if (IsAIServerError(errorType))
                {
                    errorMessage = L("ViewModel_AIServerTemporaryIssue");
                    Logger.Log("ChatWorkspaceViewModel", $"[API_ERROR] rid={requestId} user_message=ai_server_issue");
                }
                else if (_contextManager != null && _contextManager.ShouldShowAlternativeGuidance())
                {
                    errorMessage = L("ViewModel_ServerBusyAlternative");
                }
                else
                {
                    errorMessage = L("ViewModel_ServerBusy");
                }

                // Display error message using AppendErrorMessageToChat (Task 7.2)
                AppendErrorMessageToChat(errorMessage);

                // Show retry button ()
                IsRetryButtonVisible = true;

                // Track error event with enhanced details

            }
            catch (Exception logEx)
            {
                // Fallback error handling
                Logger.Log("ChatWorkspaceViewModel", $"Error in HandleApiError: {logEx.Message}");
            }
        }

        /// <summary>
        /// Requirement: 7.1
        /// </summary>
        private string ClassifyError(Exception ex)
        {
            // Check for HttpRequestException with status code
            if (ex is HttpRequestException httpEx)
            {
#if NET5_0_OR_GREATER
                // StatusCode property is available from .NET 5+
                if (httpEx.StatusCode.HasValue)
                {
                    int statusCodeValue = (int)httpEx.StatusCode.Value;
                    if (statusCodeValue == 429) return "RateLimit";
                    if (statusCodeValue == 503) return "ServiceUnavailable";
                    if (statusCodeValue == 500) return "AIServerError";
                    if (statusCodeValue == 502) return "AIServerError";
                    if (statusCodeValue == 504) return "AIServerError";
                    if (statusCodeValue == 529) return "AIServerError";
                }
#endif
                // Fallback: check message text (works on all targets including net48)
                string message = httpEx.Message?.ToLower() ?? "";
                if (message.Contains("429") || message.Contains("rate limit") || message.Contains("too many requests"))
                    return "RateLimit";
                if (message.Contains("503") || message.Contains("service unavailable"))
                    return "ServiceUnavailable";
                if (message.Contains("500") || message.Contains("internal server error"))
                    return "AIServerError";
                if (message.Contains("502") || message.Contains("bad gateway"))
                    return "AIServerError";
                if (message.Contains("504") || message.Contains("gateway timeout"))
                    return "AIServerError";
                if (message.Contains("529") || message.Contains("overload"))
                    return "AIServerError";
                if (message.Contains("timeout") || message.Contains("timed out"))
                    return "Timeout";
            }

            // Check for TaskCanceledException (timeout)
            if (ex is TaskCanceledException)
                return "Timeout";

            // Check for OperationCanceledException (timeout)
            if (ex is OperationCanceledException)
                return "Timeout";

            // Default to Unknown
            return "Unknown";
        }
        
        /// <summary>
        /// Check if error is caused by AI server (not our fault)
        /// </summary>
        private bool IsAIServerError(string errorType)
        {
            return errorType == "RateLimit" 
                || errorType == "ServiceUnavailable" 
                || errorType == "AIServerError"
                || errorType == "Timeout";
        }

        /// <summary>
        /// Requirements: 2.2, 2.3, 6.1, 6.4, 6.5
        /// </summary>
        private async Task HandleRetryAsync()
        {
            // Get retry context
            var retryContext = _contextManager?.GetPendingRetry();
            if (retryContext == null)
            {
                Logger.Log("ChatWorkspaceViewModel", "No retry context available");
                AppendHtmlMessage(ChatHtmlBuilder.SimpleWarningBubble(EscapeHtml(L("ViewModel_NoRetryContext"))));
                return;
            }

            string requestId = CreateRequestId();
            var requestTotalSw = Stopwatch.StartNew();
            Logger.Log(
                "ChatWorkspaceViewModel",
                $"[PERF] rid={requestId} step=request-start detail=retry_prompt_len:{retryContext.OriginalUserMessage?.Length ?? 0}");


            ShowRetryLoading();
            var codeFlowSw = Stopwatch.StartNew();

            try
            {
                // Build message history from retry context
                var messages = new List<ChatMessage>();
                foreach (var turn in retryContext.ConversationHistory)
                {
                    messages.Add(new ChatMessage
                    {
                        Text = turn.UserMessage,
                        IsUser = true
                    });

                    if (!string.IsNullOrEmpty(turn.AssistantResponse))
                    {
                        messages.Add(new ChatMessage
                        {
                            Text = turn.AssistantResponse,
                            IsUser = false
                        });
                    }
                }

                // Add the original user message that failed
                messages.Add(new ChatMessage
                {
                    Text = retryContext.OriginalUserMessage,
                    IsUser = true
                });

                // Retry API call
                GenerationResult result = await _pipeline.RunAsync(messages, requestId, _requestCts?.Token ?? default);

                // Add user message to history then record the turn (matches all other success paths)
                _conversationHistory.Add(new ChatMessage { Text = retryContext.OriginalUserMessage, IsUser = true });
                RecordSuccessfulTurn(retryContext.OriginalUserMessage, result.RawResponse);
                _contextManager?.ClearPendingRetry();

                // Track successful retry


                // Process and display response
                ProcessAndDisplayResponse(result, requestId);

            }
            catch (Exception ex)
            {
                // Retry failed - handle error again

                await HandleApiError(retryContext.OriginalUserMessage, ex);
            }
            finally
            {
                codeFlowSw.Stop();
                LogPerf(requestId, "code-flow", codeFlowSw.ElapsedMilliseconds, "retry");
                requestTotalSw.Stop();
                LogPerf(requestId, "request-total", requestTotalSw.ElapsedMilliseconds);


                HideRetryLoading();
            }
        }

        /// <summary>
        /// Requirement: 8.2
        /// </summary>
        private void ShowRetryLoading()
        {
            IsBusy = true;
            IsRetryButtonVisible = false;
            _loadingCts = new CancellationTokenSource();
            _ = CycleLoadingMessagesAsync(_loadingCts.Token);
        }

        /// <summary>
        /// Requirement: 8.2
        /// </summary>
        private void HideRetryLoading()
        {
            IsBusy = false;
            _loadingCts?.Cancel();
            StatusText = "";
        }

        /// <summary>
        /// Displays prompt for user to enter feedback.
        /// </summary>
        private void RequestChanges()
        {
            if (!_specManager.HasPendingSpec)
            {
                AppendHtmlMessage(ChatHtmlBuilder.SimpleWarningBubble(EscapeHtml(L("ViewModel_NoSpecToModify"))));
                return;
            }

            AppendHtmlMessage(ChatHtmlBuilder.ModifyBubble(
                EscapeHtml(L("ViewModel_RequestChangesTitle")),
                EscapeHtml(L("ViewModel_RequestChangesBody")),
                EscapeHtml(L("ViewModel_RequestChangesHint"))));
        }

        /// <summary>
        /// Clears the pending specification and displays cancellation message.
        /// </summary>
        private void CancelSpec()
        {
            if (!_specManager.HasPendingSpec)
            {
                return; // Nothing to cancel
            }

            _specManager.ClearPendingSpec();

            _lastConfirmedSpec = null;
            _lastConfirmedSpecAtUtc = DateTime.MinValue;

            AppendHtmlMessage(ChatHtmlBuilder.AiBubble($"<p>{EscapeHtml(L("ViewModel_SpecCancelled"))}</p>"));
        }

        /// <summary>
        /// Cancel ongoing API request and restore input text.
        /// </summary>
        private void CancelRequest()
        {
            if (!IsBusy) return;

            _requestCts?.Cancel();
            _loadingCts?.Cancel();
        }

        /// <summary>
        /// Handle cancellation - display message, restore input, save to history.
        /// </summary>
        private void HandleCancellation()
        {
            // 입력창에 이전 메시지 복원
            if (!string.IsNullOrEmpty(_pendingInputText))
            {
                InputText = _pendingInputText;
                _pendingInputText = null;
            }

            // 취소 메시지 표시
            AppendHtmlMessage(ChatHtmlBuilder.CancelBubble(EscapeHtml(L("ViewModel_RequestCancelled"))));

            // DB에 취소 기록 저장 (로그 분석용 태그 포함)
            SaveSingleMessage("assistant", "text", "[CANCELLED] " + L("ViewModel_RequestCancelled"));

            IsBusy = false;
            StatusText = "";
        }

        /// <summary>
        /// Saves to history with specification data.
        /// </summary>
        private void SaveToHistoryWithSpec(string userPrompt, GenerationResult result, CodeSpecification spec, string requestId = null)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (string.IsNullOrEmpty(userPrompt)) return;

                string pythonCode = result.IsCode ? CleanPythonCode(result.PythonCode) : "";

                // Create message pair with spec data
                var messagePair = new MessagePair
                {
                    UserPrompt = userPrompt,
                    AiResponse = result.RawResponse ?? "",
                    PythonCode = pythonCode,
                    CreatedAt = DateTime.UtcNow,
                    SpecificationJson = spec != null ? JsonHelper.Serialize(spec) : "",
                    WasSpecConfirmed = spec?.IsConfirmed ?? false
                };

                // Auto-generate title from first message if empty
                if (string.IsNullOrEmpty(_currentSession.Title))
                {
                    _currentSession.Title = _localSessionManager.GenerateTitle(userPrompt);
                }

                // Save to local session manager
                _localSessionManager.AddMessagePair(_currentSession.SessionId, messagePair);

                // Refresh history list
                _ = RefreshHistoryListAsync();

            }
            catch (Exception ex)
            {
                Logger.Log("ChatWorkspaceViewModel", $"SaveToHistoryWithSpec failed: {ex.Message}");
            }
            finally
            {
                sw.Stop();
                LogPerf(requestId, "history-save", sw.ElapsedMilliseconds, "with-spec");
            }
        }

        // Builds the message list for the API pipeline.
        // Side-effect: appends currentMessage to _conversationHistory — call exactly once per user turn.
        private List<ChatMessage> BuildMessageHistory(string currentMessage)
        {
            var currentMsg = new ChatMessage { Text = currentMessage, IsUser = true };
            _conversationHistory.Add(currentMsg);

            var messages = new List<ChatMessage>(_conversationHistory);
            return messages;
        }
        
        /// <summary>
        /// Get conversation history from ConversationContextManager for SpecGenerator.
        /// Returns a List of ChatMessage objects representing the current conversation.
        /// </summary>
        private List<ChatMessage> GetConversationHistoryForSpec()
        {
            var messages = new List<ChatMessage>();
            
            // _conversationHistory를 단일 소스로 사용 (항상 최신 상태 유지)
            // _contextManager는 에러 복구/세션 저장 용도로만 사용
            messages.AddRange(_conversationHistory);
            
            return messages;
        }
        
        /// <summary>
        /// AI 응답을 히스토리에 저장
        /// </summary>
        private void AddAssistantMessageToHistory(string response)
        {
            _conversationHistory.Add(new ChatMessage { Text = response, IsUser = false });
        }

        /// <summary>
        /// Records a successful AI turn to both in-memory history and the persistent context manager.
        /// <paramref name="contextResponse"/> is what the context manager stores (raw, for recovery).
        /// <paramref name="historyResponse"/> is what goes into _conversationHistory (defaults to contextResponse).
        /// Use the two-arg overload when both are the same string.
        /// </summary>
        private void RecordSuccessfulTurn(string userMessage, string contextResponse, string historyResponse = null)
        {
            AddAssistantMessageToHistory(historyResponse ?? contextResponse);
            if (_contextManager != null)
            {
                _contextManager.AddTurn(userMessage, contextResponse, isError: false);
                _contextManager.SaveSession();
            }
        }

        /// <summary>
        /// Called by ScriptingBridge.SubmitQuestionAnswers() when the user submits
        /// answers from the interactive question form.
        /// Assembles a natural-language answer string and routes it to ReviseSpecificationAsync.
        /// answersJson format: [{"question":"...","answer":"..."},...]
        /// </summary>
        // async void required: called via Dispatcher.BeginInvoke(new Action(...)) from JS bridge.
        // All logic delegated to async Task to ensure exceptions are caught and logged.
        public async void HandleQuestionAnswers(string fid, string answersJson)
        {
            try
            {
                await HandleQuestionAnswersCoreAsync(fid, answersJson);
            }
            catch (Exception ex)
            {
                Logger.LogError("ChatWorkspaceViewModel.HandleQuestionAnswers", ex);
            }
        }

        private async Task HandleQuestionAnswersCoreAsync(string fid, string answersJson)
        {
            if (string.IsNullOrWhiteSpace(answersJson) || IsBusy) return;

            // Append done marker so the form's IIFE disables the submit button on next page reload
            if (!string.IsNullOrEmpty(fid))
                _htmlContent.Append($"<span id='_bqf_done_{fid}' style='display:none'></span>");

            try
            {
                // Parse the JSON array
                var pairs = new List<(string question, string answer)>();
#if NET48
                var arr = Newtonsoft.Json.Linq.JArray.Parse(answersJson);
                foreach (var item in arr)
                {
                    string q = item["question"]?.ToString() ?? string.Empty;
                    string a = item["answer"]?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(q) && !string.IsNullOrWhiteSpace(a))
                        pairs.Add((q, a));
                }
#else
                using (var doc = System.Text.Json.JsonDocument.Parse(answersJson))
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        string q = item.TryGetProperty("question", out var qp) ? qp.GetString() ?? string.Empty : string.Empty;
                        string a = item.TryGetProperty("answer", out var ap) ? ap.GetString() ?? string.Empty : string.Empty;
                        if (!string.IsNullOrWhiteSpace(q) && !string.IsNullOrWhiteSpace(a))
                            pairs.Add((q, a));
                    }
                }
#endif
                if (pairs.Count == 0) return;

                // Assemble into natural language so the AI understands the context
                var sb = new StringBuilder();
                for (int i = 0; i < pairs.Count; i++)
                    sb.AppendLine($"{i + 1}. {pairs[i].question} → {pairs[i].answer}");
                string userAnswer = sb.ToString().Trim();

                // Show as a user bubble (mirrors what the user "said")
                AppendHtmlMessage(ChatHtmlBuilder.UserBubble(EscapeHtml(userAnswer)));

                // Add to history so context is preserved
                _conversationHistory.Add(new ChatMessage { Text = userAnswer, IsUser = true });

                // Prepare request — mirrors HandleSpecFirstFlowAsync pattern
                _requestCts?.Cancel();
                _requestCts?.Dispose();
                _requestCts = new CancellationTokenSource();
                IsBusy = true;
                _loadingCts = new CancellationTokenSource();
                _ = CycleLoadingMessagesAsync(_loadingCts.Token);

                // Route through ReviseSpecificationAsync (same path as typing an answer)
                string requestId = Guid.NewGuid().ToString("N").Substring(0, 8);
                try
                {
                    await ReviseSpecificationAsync(userAnswer, requestId);
                }
                finally
                {
                    IsBusy = false;
                    _loadingCts?.Cancel();
                    StatusText = "";
                }
            }
            catch (Exception ex)
            {
                IsBusy = false;
                _loadingCts?.Cancel();
                StatusText = "";
                Logger.Log("ChatWorkspaceViewModel", $"HandleQuestionAnswers error: {ex.Message}");
                AppendHtmlMessage(ChatHtmlBuilder.SimpleErrorBubble(EscapeHtml(L("ViewModel_GeneralError"))));
            }
        }

        /// <summary>
        /// 대화 히스토리 초기화 (새 대화 시작 시)
        /// </summary>
        public void ClearConversationHistory()
        {
            _conversationHistory.Clear();
            _htmlContent.Clear();
        }

        private void ProcessAndDisplayResponse(GenerationResult result, string requestId = null)
        {
            switch (result.Type)
            {
                case GenerationResultType.Question:
                    AppendHtmlMessage(ChatHtmlBuilder.QuestionBubble(
                        EscapeHtml(L("ViewModel_AdditionalInfoNeeded")),
                        EscapeHtml(result.Message)));
                    SaveToHistory(_lastUserMessage, result.Message, "", requestId);
                    NotificationHelper.ShowResponseNotification(isCodeGenerated: false);
                    break;

                case GenerationResultType.ValidationBlock:
                {
                    string blockHtml = ConvertMarkdownToHtml(result.Message);
                    AppendHtmlMessage(ChatHtmlBuilder.ValidationBlockBubble(
                        EscapeHtml(L("ViewModel_CodeInjectionBlockedTitle")),
                        blockHtml));
                    SaveToHistory(_lastUserMessage, result.Message, "", requestId);
                    SaveSingleMessage("assistant", "validation_block", result.Message, null, requestId);
                    NotificationHelper.ShowResponseNotification(isCodeGenerated: false);
                    break;
                }

                case GenerationResultType.Code:
                {
                    string cleanedCode = CleanPythonCode(result.PythonCode);
                    string guideText = result.GuideText ?? "";


                    string nodeGuid;
                    int inputCount;
                    var injectSw = Stopwatch.StartNew();
                    (nodeGuid, inputCount) = InjectPythonCode(cleanedCode);
                    injectSw.Stop();
                    LogPerf(requestId, "inject-create", injectSw.ElapsedMilliseconds, $"inputs:{inputCount}");

                    if (!string.IsNullOrEmpty(nodeGuid))
                        _lastGeneratedPythonNodeGuid = nodeGuid;

                    if (!string.IsNullOrEmpty(nodeGuid))
                    {
                        string groupDescription = !string.IsNullOrWhiteSpace(guideText)
                            ? guideText.Length > 200 ? guideText.Substring(0, 200) + "..." : guideText
                            : L("ViewModel_GroupDescriptionDefault");

                        _nodeManipulator.CreateGroup(
                            new List<string> { nodeGuid },
                            L("ViewModel_GroupLabel_BibimGenerated"),
                            groupDescription,
                            "#FFFFE0B2");

                        _nodeManipulator.ZoomToFit(nodeGuid);
                    }

                    string inputInfo = inputCount > 1 ? LF("ViewModel_InputPortsAdded", inputCount) : "";
                    AppendHtmlMessage(ChatHtmlBuilder.CodeSuccessBubble(
                        $"✅ {EscapeHtml(L("ViewModel_NodeGeneratedSuccess"))}{EscapeHtml(inputInfo)}. {EscapeHtml(L("ViewModel_NoManualCopyNeeded"))}"));

                    if (!string.IsNullOrWhiteSpace(guideText))
                    {
                        string guideHtml = MarkdownRenderer.ConvertGuideToHtml(guideText);
                        AppendHtmlMessage(ChatHtmlBuilder.GuideBubble(
                            EscapeHtml(L("ViewModel_NextStepTitle")),
                            guideHtml));
                    }

                    int lineCount = cleanedCode.Split('\n').Length;
                    AppendHtmlMessage(ChatHtmlBuilder.CodeToggleBubble(
                        EscapeHtmlForCode(cleanedCode),
                        EscapeHtml(LF("ViewModel_ViewGeneratedCode", lineCount)),
                        EscapeHtml(L("ViewModel_CopyCode"))));

                    SaveToHistory(_lastUserMessage, guideText, cleanedCode, requestId);
                    SaveSingleMessage("assistant", "code", guideText, cleanedCode, requestId);
                    NotificationHelper.ShowResponseNotification(isCodeGenerated: true);
                    break;
                }

                default: // Chat
                {
                    string responseHtml = ConvertMarkdownToHtml(result.Message);
                    AppendHtmlMessage(ChatHtmlBuilder.AiBubble(responseHtml));
                    SaveToHistory(_lastUserMessage, result.Message, "", requestId);
                    SaveSingleMessage("assistant", "text", result.Message, null, requestId);
                    NotificationHelper.ShowResponseNotification(isCodeGenerated: false);
                    break;
                }
            }
        }

        private string ConvertMarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return "";
            try
            {
                // Use MarkdownRenderer with proper pipeline (includes table support)
                return MarkdownRenderer.ConvertToHtml(markdown);
            }
            catch
            {
                return $"<p>{EscapeHtml(markdown)}</p>";
            }
        }

        private string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("\n", "<br/>");
        }

        /// <summary>
        /// Escape HTML for code blocks - preserves newlines for pre/code tags
        /// </summary>
        private string EscapeHtmlForCode(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
            // NOTE: Do NOT replace \n - pre/code tags need actual newlines
        }

        private static readonly Regex _markdownCodePattern = new Regex(
            @"```(?:python)?\s*(.*?)```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private string CleanPythonCode(string rawCode)
        {
            var match = _markdownCodePattern.Match(rawCode);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            string codeToProcess = rawCode;
            int importClrIndex = codeToProcess.IndexOf("import clr");
            int importSysIndex = codeToProcess.IndexOf("import System");

            if (importClrIndex >= 0)
                codeToProcess = codeToProcess.Substring(importClrIndex);
            else if (importSysIndex >= 0)
                codeToProcess = codeToProcess.Substring(importSysIndex);

            string[] garbagePatterns = { "\n```", "\n###", "\nTYPE:", "\n## ", "```", "TYPE:" };
            foreach (string pattern in garbagePatterns)
            {
                int garbageIndex = codeToProcess.IndexOf(pattern);
                if (garbageIndex > 0)
                {
                    codeToProcess = codeToProcess.Substring(0, garbageIndex).Trim();
                    break;
                }
            }

            return codeToProcess.Trim();
        }

        private bool ShouldReviseLastConfirmedSpec(string message)
        {
            if (_lastConfirmedSpec == null)
            {
                return false;
            }

            if (_lastConfirmedSpecAtUtc == DateTime.MinValue)
            {
                return false;
            }

            if (DateTime.UtcNow - _lastConfirmedSpecAtUtc > TimeSpan.FromMinutes(15))
            {
                return false;
            }

            return HasEditIntent(message);
        }

        private static bool HasEditIntent(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant();
            return lower.Contains("수정")
                || lower.Contains("고쳐")
                || lower.Contains("바꿔")
                || lower.Contains("변경")
                || lower.Contains("리비전")
                || lower.Contains("다시")
                || lower.Contains("edit")
                || lower.Contains("fix")
                || lower.Contains("change")
                || lower.Contains("modify")
                || lower.Contains("update")
                || lower.Contains("again");
        }

        private static CodeSpecification CloneSpecificationForRevision(CodeSpecification source)
        {
            if (source == null)
            {
                return null;
            }

            try
            {
                var cloned = JsonHelper.Deserialize<CodeSpecification>(JsonHelper.Serialize(source));
                if (cloned != null)
                {
                    cloned.IsConfirmed = false;
                }

                return cloned;
            }
            catch
            {
                return null;
            }
        }

        private bool TryUpdateExistingPythonNode(string nodeGuid, string pythonCode, int requiredInputs, out string details)
        {
            details = "";

            try
            {
                if (string.IsNullOrWhiteSpace(nodeGuid))
                {
                    details = L("ViewModel_NoExistingNodeGuid");
                    return false;
                }

                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null)
                {
                    details = L("ViewModel_WorkspaceNotFound");
                    return false;
                }

                var node = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == nodeGuid);
                if (node == null)
                {
                    details = LF("ViewModel_NodeNotFound", nodeGuid);
                    return false;
                }

                var nodeType = node.GetType();
                var codeProperty = nodeType.GetProperty("Code")
                    ?? nodeType.GetProperty("Script")
                    ?? nodeType.GetProperty("ScriptContent")
                    ?? nodeType.GetProperty("PythonCode");

                if (codeProperty == null || !codeProperty.CanWrite)
                {
                    details = LF("ViewModel_NodeTypeNotSupported", nodeType.Name);
                    return false;
                }

                if (node.InPorts.Count != requiredInputs)
                {
                    bool portsAdjusted = AddInputPortsToPythonNode(node, requiredInputs);
                    if (!portsAdjusted && node.InPorts.Count != requiredInputs)
                    {
                        details = LF("ViewModel_InputPortSyncFailed", node.InPorts.Count, requiredInputs);
                        return false;
                    }
                }

                codeProperty.SetValue(node, pythonCode);

                try
                {
                    var onNodeModified = nodeType.GetMethod("OnNodeModified",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                        null, new Type[] { typeof(bool) }, null);
                    onNodeModified?.Invoke(node, new object[] { true });
                }
                catch (Exception ex)
                {
                    Logger.Log("ChatWorkspaceViewModel", $"OnNodeModified for existing node failed (non-critical): {ex.Message}");
                }

                details = L("ViewModel_UpdateExistingNodeCompleted");
                return true;
            }
            catch (Exception ex)
            {
                details = LF("ViewModel_UpdateExistingNodeFailed", ex.Message);
                Logger.Log("ChatWorkspaceViewModel", details);
                return false;
            }
        }

        private static Type _cachedPythonNodeType = null;
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Analyze Python code to detect required input count based on IN[n] pattern
        /// </summary>
        private int DetectRequiredInputCount(string pythonCode)
        {
            try
            {
                // Find all IN[number] patterns
                var matches = Regex.Matches(pythonCode, @"IN\[(\d+)\]");

                if (matches.Count == 0)
                {
                    // No IN[n] pattern found, default to 1 input
                    return 1;
                }

                // Find the maximum index
                int maxIndex = 0;
                foreach (Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out int index))
                    {
                        maxIndex = Math.Max(maxIndex, index);
                    }
                }

                // Return count (index is 0-based, so +1)
                return maxIndex + 1;
            }
            catch
            {
                return 1; // Fallback to 1 input
            }
        }

        /// <summary>Delegates to NodeManipulator.AddInputPortsToPythonNode.</summary>
        private static bool AddInputPortsToPythonNode(NodeModel node, int inputCount)
            => NodeManipulator.AddInputPortsToPythonNode(node, inputCount);

        /// <summary>
        /// Inject Python code into Dynamo workspace with automatic input port detection
        /// Returns the created node's GUID (or null if failed)
        /// </summary>
        private (string nodeGuid, int inputCount) InjectPythonCode(string pythonCode)
        {
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow.DataContext as DynamoViewModel;
                if (dynamoViewModel == null)
                {
                    AppendHtmlMessage(ChatHtmlBuilder.SimpleErrorBubble(EscapeHtml(L("ViewModel_DynamoViewModelNotFound"))));
                    return (null, 0);
                }

                Type pythonNodeType = null;
                lock (_cacheLock) { pythonNodeType = _cachedPythonNodeType; }

                if (pythonNodeType == null)
                {
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    var candidateTypes = new List<Type>();

                    foreach (var assembly in assemblies)
                    {
                        Type[] types;
                        try { types = assembly.GetTypes(); }
                        catch { continue; }

                        foreach (var t in types)
                        {
                            if (t != null && !t.IsAbstract && typeof(NodeModel).IsAssignableFrom(t) &&
                                (t.Name == "PythonNodeModel" || t.Name == "PythonNode" ||
                                 (t.FullName != null && (t.FullName.EndsWith(".PythonNodeModel") || t.FullName.EndsWith(".PythonNode")))))
                            {
                                candidateTypes.Add(t);
                            }
                        }
                    }

                    pythonNodeType = candidateTypes
                        .OrderByDescending(t => t.Namespace != null &&
                            (t.Namespace.Contains("DSCPython") || t.Namespace.Contains("DSIronPython") || t.Namespace.Contains("Python")))
                        .FirstOrDefault();

                    if (pythonNodeType == null)
                    {
                        AppendHtmlMessage(ChatHtmlBuilder.SimpleErrorBubble(EscapeHtml(L("ViewModel_PythonNodeTypeNotFound"))));
                        return (null, 0);
                    }

                    lock (_cacheLock) { _cachedPythonNodeType = pythonNodeType; }
                }

                var pythonNode = Activator.CreateInstance(pythonNodeType) as NodeModel;
                if (pythonNode == null)
                {
                    AppendHtmlMessage(ChatHtmlBuilder.SimpleErrorBubble(EscapeHtml(L("ViewModel_PythonNodeCreateFailed"))));
                    return (null, 0);
                }

                // NEW: Detect required input count from code
                int requiredInputs = DetectRequiredInputCount(pythonCode);
                Logger.Log("ChatWorkspaceViewModel", $"Detected {requiredInputs} required inputs from Python code");

                // Get code property reference (but DON'T set code yet to avoid port mismatch warning)
                var codeProperty = pythonNodeType.GetProperty("Code")
                    ?? pythonNodeType.GetProperty("Script")
                    ?? pythonNodeType.GetProperty("ScriptContent");

                // Section 3.3: Calculate position to avoid overlap
                var (x, y) = CalculateNodePosition(dynamoViewModel);

                // Add node to workspace FIRST (with default code - no warning)
                dynamoViewModel.ExecuteCommand(new DynamoModel.CreateNodeCommand(pythonNode, x, y, false, false));
                Logger.Log("ChatWorkspaceViewModel", "Node added to workspace");

                // Add input ports BEFORE setting code so ports match IN[] references
                bool portsAdded = AddInputPortsToPythonNode(pythonNode, requiredInputs);
                Logger.Log("ChatWorkspaceViewModel", $"Ports added: {portsAdded}, Current count: {pythonNode.InPorts.Count}, Expected: {requiredInputs}");

                // NOW set the code - ports already match so no warning
                if (codeProperty != null && codeProperty.CanWrite)
                {
                    codeProperty.SetValue(pythonNode, pythonCode);
                    Logger.Log("ChatWorkspaceViewModel", "Code set successfully after ports configured");
                }

                // Force re-evaluation to ensure clean state with correct ports + code
                try
                {
                    var onNodeModified = pythonNode.GetType().GetMethod("OnNodeModified",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                        null, new Type[] { typeof(bool) }, null);
                    if (onNodeModified != null)
                    {
                        onNodeModified.Invoke(pythonNode, new object[] { true });
                        Logger.Log("ChatWorkspaceViewModel", "OnNodeModified called to force clean re-evaluation");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("ChatWorkspaceViewModel", $"OnNodeModified call failed (non-critical): {ex.Message}");
                }

                return (pythonNode.GUID.ToString(), requiredInputs);
            }
            catch (Exception ex)
            {
                AppendHtmlMessage(ChatHtmlBuilder.SimpleErrorBubble(EscapeHtml(LF("ViewModel_NodeInjectError", ex.Message))));
                return (null, 0);
            }
        }

        /// <summary>
        /// Section 3.3: QA Logic - Calculate position avoiding existing nodes
        /// </summary>
        private (double x, double y) CalculateNodePosition(DynamoViewModel dynamoViewModel)
        {
            double defaultX = 100;
            double defaultY = 100;
            double nodeWidth = 200;
            double nodeHeight = 150;
            double padding = 50;

            try
            {
                var workspace = dynamoViewModel.Model?.CurrentWorkspace;
                if (workspace == null || !workspace.Nodes.Any())
                    return (defaultX, defaultY);

                // Find rightmost node position
                double maxX = workspace.Nodes.Max(n => n.X);
                double maxY = workspace.Nodes.Where(n => Math.Abs(n.X - maxX) < nodeWidth).Max(n => n.Y);

                // Place new node to the right of existing nodes
                return (maxX + nodeWidth + padding, maxY);
            }
            catch
            {
                return (defaultX, defaultY);
            }
        }

        private void SaveToHistory(string userPrompt, string aiResponse, string pythonCode, string requestId = null)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (string.IsNullOrEmpty(userPrompt)) return;

                // Create message pair for local storage
                var messagePair = new MessagePair
                {
                    UserPrompt = userPrompt,
                    AiResponse = aiResponse ?? "",
                    PythonCode = pythonCode ?? "",
                    CreatedAt = DateTime.UtcNow
                };

                // Auto-generate title from first message if empty
                if (string.IsNullOrEmpty(_currentSession.Title))
                {
                    _currentSession.Title = _localSessionManager.GenerateTitle(userPrompt);
                }

                // Save to local session manager
                _localSessionManager.AddMessagePair(_currentSession.SessionId, messagePair);

                // Refresh history list
                _ = RefreshHistoryListAsync();

            }
            catch (Exception ex)
            {
                Logger.LogError("ChatWorkspaceViewModel.SaveAnalysisToHistory", ex);
            }
            finally
            {
                sw.Stop();
                LogPerf(requestId, "history-save", sw.ElapsedMilliseconds, "message-pair");
            }
        }

        /// <summary>
        /// Save a single message to history (new format for accurate ordering)
        /// </summary>
        /// <param name="role">"user" or "assistant"</param>
        /// <param name="contentType">"text", "question", "spec", "code", "guide", "analysis"</param>
        /// <param name="content">Message content</param>
        /// <param name="pythonCode">Python code if applicable</param>
        private void SaveSingleMessage(string role, string contentType, string content, string pythonCode = null, string requestId = null)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (string.IsNullOrEmpty(content)) return;

                // Auto-generate title from first user message if empty
                if (string.IsNullOrEmpty(_currentSession.Title) && role == "user")
                {
                    _currentSession.Title = _localSessionManager.GenerateTitle(content);
                }

                // Save to local session manager using new format
                _localSessionManager.AddSingleMessage(_currentSession.SessionId, role, contentType, content, pythonCode);

                // Refresh history list
                _ = RefreshHistoryListAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError("ChatWorkspaceViewModel.SaveSingleMessage", ex);
            }
            finally
            {
                sw.Stop();
                LogPerf(requestId, "history-save", sw.ElapsedMilliseconds, $"single:{role}/{contentType}");
            }
        }

        private async Task CycleLoadingMessagesAsync(CancellationToken token)
        {
            int index = 0;
            while (!token.IsCancellationRequested)
            {
                StatusText = _loadingMessages[index];
                index = (index + 1) % _loadingMessages.Length;
                try { await Task.Delay(1200, token); }
                catch (TaskCanceledException) { break; }
            }
        }


        private async Task ToggleHistoryPanelAsync()
        {
            IsHistoryPanelVisible = !IsHistoryPanelVisible;
            if (IsHistoryPanelVisible)
                await RefreshHistoryListAsync();
        }

        private async Task RefreshHistoryListAsync()
        {
            try
            {
                // Load from local session manager
                var sessions = _localSessionManager.GetAllSessions();
                
                // Korea Standard Time (UTC+9), fallback to UTC if not available
                TimeZoneInfo koreaTimeZone;
                try { koreaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); }
                catch { koreaTimeZone = TimeZoneInfo.Utc; }
                
                HistoryList.Clear();
                foreach (var session in sessions)
                {
                    // Convert UTC to Korea time for display
                    var koreaTime = TimeZoneInfo.ConvertTimeFromUtc(session.UpdatedAt, koreaTimeZone);
                    
                    // Convert ChatSession to HistoryEntry for UI compatibility
                    var entry = new HistoryEntry
                    {
                        Id = session.SessionId,
                        Timestamp = koreaTime.ToString("yyyy-MM-dd HH:mm"),
                        RevitVersion = session.RevitVersion ?? "",
                        UserPrompt = session.Title,
                        AiResponse = "",
                        PythonCode = ""
                    };
                    HistoryList.Add(entry);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ChatWorkspaceViewModel.RefreshHistoryListAsync", ex);
            }

            await Task.CompletedTask;
        }

        private async Task LoadHistoryEntryAsync(object param)
        {
            var entry = param as HistoryEntry;
            if (entry == null) return;

            IsHistoryPanelVisible = false;
            _htmlContent.Clear();
            _conversationHistory.Clear();


            _specManager.ClearPendingSpec();

            _lastConfirmedSpec = null;
            _lastConfirmedSpecAtUtc = DateTime.MinValue;

            // Load session from local storage
            var session = _localSessionManager.LoadSession(entry.Id);
            if (session == null) return;

            // Set as current session for continuing conversation
            _currentSession = session;


            // Requirements: 4.3, 4.4
            // FIX: Also restore _conversationHistory from SessionContext.Turns for LLM API calls
            if (_contextManager != null)
            {
                try
                {
                    var sessionContext = _contextManager.RestoreSession(session.SessionId);
                    
                    // FIX: Restore conversation history from SessionContext.Turns
                    // This ensures LLM API calls have access to previous conversation context
                    if (sessionContext.Turns != null && sessionContext.Turns.Count > 0)
                    {
                        foreach (var turn in sessionContext.Turns.OrderBy(t => t.Timestamp))
                        {
                            // Add user message
                            if (!string.IsNullOrEmpty(turn.UserMessage))
                            {
                                _conversationHistory.Add(new ChatMessage { Text = turn.UserMessage, IsUser = true });
                            }
                            // Add assistant response (skip if error turn with no response)
                            if (!string.IsNullOrEmpty(turn.AssistantResponse))
                            {
                                _conversationHistory.Add(new ChatMessage { Text = turn.AssistantResponse, IsUser = false });
                            }
                        }
                        Logger.Log("ChatWorkspaceViewModel", $"Restored {sessionContext.Turns.Count} turns to conversation history");
                    }
                    
                    // Check if there's a pending retry and show retry button
                    if (sessionContext.PendingRetry != null)
                    {
                        IsRetryButtonVisible = true;
                        Logger.Log("ChatWorkspaceViewModel", $"Restored session with pending retry: {session.SessionId}");
                    }
                    else
                    {
                        IsRetryButtonVisible = false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("ChatWorkspaceViewModel", $"Failed to restore session context: {ex.Message}");
                    IsRetryButtonVisible = false;
                }
            }

            // Check version mismatch and show warning alert
            var (currentRevitVersion, _, _, _) = GeminiService.GetCurrentConfigInfo();
            string sessionRevitVersion = session.RevitVersion ?? "";
            
            if (!string.IsNullOrEmpty(sessionRevitVersion) && 
                !string.IsNullOrEmpty(currentRevitVersion) &&
                sessionRevitVersion != currentRevitVersion)
            {
                // Fire event for WPF banner instead of HTML
                VersionMismatchDetected?.Invoke(this, (sessionRevitVersion, currentRevitVersion));
            }

            // Check if session uses new SingleMessages format
            if (session.UsesNewFormat)
            {
                // New format: display individual messages in order
                foreach (var msg in session.SingleMessages.OrderBy(m => m.SequenceOrder))
                {
                    DisplaySingleMessage(msg);
                }
                

                // Requirements: 4.3, 4.4
                if (_contextManager != null)
                {
                    var sessionContext = _contextManager.GetCurrentSession();
                    if (sessionContext != null && sessionContext.Turns != null)
                    {
                        // Find error turns that don't have corresponding SingleMessages
                        var errorTurns = sessionContext.Turns.Where(t => t.IsError && string.IsNullOrEmpty(t.AssistantResponse)).ToList();
                        foreach (var errorTurn in errorTurns)
                        {
                            // Display error message
                            string errorMessage = L("ViewModel_ServerBusy");
                            if (sessionContext.ConsecutiveErrors >= 3)
                            {
                                errorMessage = L("ViewModel_ServerBusyAlternative");
                            }
                            AppendErrorMessageToChat(errorMessage);
                        }
                    }
                }
            }
            else
            {
                // Legacy format: display MessagePair (backward compatibility)
                foreach (var msg in session.Messages.OrderBy(m => m.SequenceOrder))
                {
                    // User message
                    _conversationHistory.Add(new ChatMessage { Text = msg.UserPrompt, IsUser = true });
                    AppendHtmlMessage(ChatHtmlBuilder.UserBubble(EscapeHtml(msg.UserPrompt)));

                    // AI response
                    _conversationHistory.Add(new ChatMessage { Text = msg.AiResponse ?? "", IsUser = false });

                    if (!string.IsNullOrEmpty(msg.AiResponse))
                    {
                        AppendHtmlMessage(ChatHtmlBuilder.AiBubble(ConvertMarkdownToHtml(msg.AiResponse)));
                    }

                    // Python code if exists (collapsed by default)
                    if (!string.IsNullOrEmpty(msg.PythonCode))
                    {
                        int historyLineCount = msg.PythonCode.Split('\n').Length;
                        AppendHtmlMessage(ChatHtmlBuilder.CodeToggleBubble(
                            EscapeHtmlForCode(msg.PythonCode),
                            EscapeHtml(LF("ViewModel_ViewGeneratedCode", historyLineCount)),
                            EscapeHtml(L("ViewModel_CopyCode"))));
                    }
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Display a single message from history based on its type
        /// </summary>
        private void DisplaySingleMessage(SingleMessage msg)
        {
            bool isUser = msg.Role == "user";
            
            // Add to conversation history
            _conversationHistory.Add(new ChatMessage { Text = msg.Content, IsUser = isUser });

            if (isUser)
            {
                // User message
                AppendHtmlMessage(ChatHtmlBuilder.UserBubble(EscapeHtml(msg.Content)));
            }
            else
            {
                // AI message - display based on content type
                switch (msg.ContentType)
                {
                    case "question":
                        AppendHtmlMessage(ChatHtmlBuilder.QuestionBubble(
                            EscapeHtml(L("ViewModel_AdditionalInfoNeeded")),
                            EscapeHtml(msg.Content)));
                        break;

                    case "spec":
                        try
                        {
                            var spec = JsonHelper.Deserialize<CodeSpecification>(msg.Content);
                            AppendHtmlMessage(spec != null
                                ? ChatHtmlBuilder.AiBubble(SpecGenerator.FormatSpecificationHtml(spec))
                                : ChatHtmlBuilder.AiBubble(ConvertMarkdownToHtml(msg.Content)));
                        }
                        catch
                        {
                            AppendHtmlMessage(ChatHtmlBuilder.AiBubble(ConvertMarkdownToHtml(msg.Content)));
                        }
                        break;

                    case "code":
                        if (!string.IsNullOrEmpty(msg.Content))
                        {
                            AppendHtmlMessage(ChatHtmlBuilder.GuideBubble(
                                EscapeHtml(L("ViewModel_NextStepTitle")),
                                ConvertMarkdownToHtml(msg.Content)));
                        }
                        if (!string.IsNullOrEmpty(msg.PythonCode))
                        {
                            int lineCount = msg.PythonCode.Split('\n').Length;
                            AppendHtmlMessage(ChatHtmlBuilder.CodeToggleBubble(
                                EscapeHtmlForCode(msg.PythonCode),
                                EscapeHtml(LF("ViewModel_ViewGeneratedCode", lineCount)),
                                EscapeHtml(L("ViewModel_CopyCode"))));
                        }
                        break;

                    case "analysis":
                        AppendHtmlMessage(ChatHtmlBuilder.ModifyBubble(
                            EscapeHtml(L("ViewModel_AnalysisResultTitle")),
                            ConvertMarkdownToHtml(msg.Content)));
                        break;

                    default:
                        AppendHtmlMessage(ChatHtmlBuilder.AiBubble(ConvertMarkdownToHtml(msg.Content)));
                        break;
                }
            }
        }

        internal void Cleanup()
        {
            _specManager.SpecStateChanged -= OnSpecStateChanged;
            _requestCts?.Dispose();
            _loadingCts?.Dispose();
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }
}
