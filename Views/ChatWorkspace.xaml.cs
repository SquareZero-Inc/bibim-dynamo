// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Threading.Tasks;
using Dynamo.Wpf.Extensions;

namespace BIBIM_MVP
{
    /// <summary>
    /// Section 1: Single Interface Strategy - Unified Chat Workspace
    /// Combines generation and analysis in one interface
    /// </summary>
    public partial class ChatWorkspace : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// Task 13.1: JavaScript bridge class for WebBrowser ObjectForScripting.
        /// Enables window.external calls from HTML to invoke ViewModel commands.
        /// Requirements: 3.1, 3.2
        /// </summary>
        [ComVisible(true)]
        public class ScriptingBridge
        {
            private readonly ChatWorkspace _parent;

            public ScriptingBridge(ChatWorkspace parent)
            {
                _parent = parent;
            }

            /// <summary>
            /// Called from JavaScript: window.external.ConfirmSpec()
            /// Confirms the pending specification and triggers code generation.
            /// Requirements: 3.1
            /// </summary>
            public void ConfirmSpec()
            {
                _parent.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_parent._viewModel?.ConfirmSpecCommand?.CanExecute(null) == true)
                        {
                            _parent._viewModel.ConfirmSpecCommand.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("ScriptingBridge.ConfirmSpec", ex);
                    }
                }));
            }

            /// <summary>
            /// Called from JavaScript: window.external.RequestChanges()
            /// Prompts user to enter modification feedback for the specification.
            /// Requirements: 3.2
            /// </summary>
            public void RequestChanges()
            {
                _parent.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_parent._viewModel?.RequestChangesCommand?.CanExecute(null) == true)
                        {
                            _parent._viewModel.RequestChangesCommand.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("ScriptingBridge.RequestChanges", ex);
                    }
                }));
            }

            /// <summary>
            /// Called from JavaScript: window.external.SubmitQuestionAnswers(json)
            /// Receives structured Q&amp;A answers from the interactive question form and
            /// forwards them to the ViewModel for assembly and spec revision.
            /// json format: [{"question":"...","answer":"..."},...]
            /// </summary>
            public void SubmitQuestionAnswers(string fid, string answersJson)
            {
                _parent.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _parent._viewModel?.HandleQuestionAnswers(fid, answersJson);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("ScriptingBridge.SubmitQuestionAnswers", ex);
                    }
                }));
            }
        }

        private ScriptingBridge _scriptingBridge;

        private ChatWorkspaceViewModel _viewModel;
        private ViewLoadedParams _viewLoadedParams;
        private List<AnalysisAction> _pendingActions = new List<AnalysisAction>();
        private bool _analysisCancelled = false;
        private bool _isAnalyzing = false;
        private System.Threading.CancellationTokenSource _analysisCts;
        private NodeManipulator _nodeManipulator;

        private static string L(string key) => LocalizationService.Get(key);

        private static string LF(string key, params object[] args) => LocalizationService.Format(key, args);

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        public ChatWorkspace(ChatWorkspaceViewModel viewModel, ViewLoadedParams viewLoadedParams)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _viewLoadedParams = viewLoadedParams;
            _nodeManipulator = new NodeManipulator(viewLoadedParams);
            DataContext = viewModel;

            // Task 13.1: Initialize scripting bridge for window.external calls
            _scriptingBridge = new ScriptingBridge(this);

            // Subscribe to ViewModel property changes to handle WebBrowser visibility
            viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Initialize WebBrowser with welcome message
            InitializeChatBrowser();

            // Subscribe to message updates
            viewModel.MessagesUpdated += OnMessagesUpdated;
            
            // Subscribe to version mismatch warning
            viewModel.VersionMismatchDetected += OnVersionMismatchDetected;

            // Set app version display
            AppVersionText.Text = $"v{VersionChecker.CurrentVersion}";

            // Cleanup when control is unloaded
            this.Unloaded += ChatWorkspace_Unloaded;
        }
        
        /// <summary>
        /// Cleanup event handlers when control is unloaded
        /// </summary>
        private void ChatWorkspace_Unloaded(object sender, RoutedEventArgs e)
        {
            ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
            _viewModel.Cleanup();
        }
        
        private void OnVersionMismatchDetected(object sender, (string sessionVersion, string currentVersion) versions)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(versions.sessionVersion))
                {
                    // Hide banner for new chat
                    VersionWarningBanner.Visibility = Visibility.Collapsed;
                }
                else
                {
                    VersionWarningText.Text = LF("Chat_VersionMismatchWarning", versions.sessionVersion, versions.currentVersion);
                    VersionWarningBanner.Visibility = Visibility.Visible;
                }
            });
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Handle WebBrowser visibility when history panel toggles (Airspace issue workaround)
            if (e.PropertyName == nameof(ChatWorkspaceViewModel.IsHistoryPanelVisible))
            {
                ChatBrowser.Visibility = _viewModel.IsHistoryPanelVisible 
                    ? Visibility.Collapsed 
                    : Visibility.Visible;
            }
        }



        private void InitializeChatBrowser()
        {
            string welcomeHtml = GenerateChatHtml(GetWelcomeMessage());
            ChatBrowser.NavigateToString(welcomeHtml);
        }

        private string GetWelcomeMessage()
        {
            string title = EscapeHtml(L("Chat_WelcomeTitle"));
            string body = EscapeHtml(L("Chat_WelcomeBody"));
            string hint = EscapeHtml(L("Chat_WelcomeHint"));

            return $@"
<div class='message ai'>
    <div class='bubble'>
        <p>{title}</p>
        <p>{body}</p>
        <p class='hint'>{hint}</p>
    </div>
</div>";
        }

        private void OnMessagesUpdated(object sender, string htmlContent)
        {
            Dispatcher.Invoke(() =>
            {
                // 빈 문자열이면 웰컴 메시지 표시 (새 대화 시작 시)
                string content = string.IsNullOrEmpty(htmlContent) ? GetWelcomeMessage() : htmlContent;
                string fullHtml = GenerateChatHtml(content);
                ChatBrowser.NavigateToString(fullHtml);
            });
        }

        private string GenerateChatHtml(string messagesHtml)
        {
            string copyDone = L("Chat_CopyCodeCopiedLabel");
            string copyCode = L("Chat_CopyCodeDefaultLabel");
            string actionGuideLabel = L("Chat_ActionGuideLabel");

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta http-equiv='X-UA-Compatible' content='IE=edge'>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        
        body {{
            font-family: 'Pretendard', 'Malgun Gothic', -apple-system, sans-serif;
            background: #1E1E1E;
            color: #E0E0E0;
            padding: 16px;
            font-size: 14px;
            line-height: 1.7;
            user-select: text;
            -webkit-user-select: text;
        }}
        
        /* 메시지 레이아웃 */
        .message {{ margin-bottom: 16px; display: flex; }}
        .message.user {{ justify-content: flex-end; }}
        .message.ai {{ justify-content: flex-start; }}
        
        .bubble {{
            max-width: 88%;
            padding: 14px 18px;
            border-radius: 12px;
            user-select: text;
        }}
        
        .message.user .bubble {{
            background: #0078D4;
            border-radius: 12px 12px 4px 12px;
            color: #FFFFFF;
        }}
        
        .message.ai .bubble {{
            background: #2D2D30;
            border: 1px solid #404040;
            border-radius: 12px 12px 12px 4px;
        }}
        
        /* 타이포그래피 */
        p {{ margin: 8px 0; }}
        
        strong {{ color: #FFD700; font-weight: 600; }}
        
        h1, h2, h3 {{ 
            margin: 20px 0 10px 0; 
            padding-bottom: 6px;
            border-bottom: 1px solid #404040;
            color: #FFFFFF;
        }}
        h1 {{ font-size: 18px; }}
        h2 {{ font-size: 16px; color: #4FC3F7; }}
        h3 {{ font-size: 14px; border-bottom: none; }}
        
        .hint {{ color: #888; font-size: 13px; margin-top: 10px; }}
        
        /* 리스트 */
        ul, ol {{ 
            margin: 10px 0 10px 24px; 
            padding-left: 0;
        }}
        li {{ 
            margin: 6px 0; 
            line-height: 1.6;
        }}
        
        /* 테이블 스타일 */
        table {{ 
            border-collapse: collapse; 
            width: 100%; 
            margin: 12px 0; 
            background: #252526;
            border-radius: 8px;
            overflow: hidden;
            font-size: 13px;
        }}
        th, td {{ 
            border: 1px solid #404040; 
            padding: 10px 12px; 
            text-align: left; 
        }}
        th {{ 
            background: #333333; 
            font-weight: 600; 
            color: #FF9800;
        }}
        tr:hover {{ background: #2A2A2A; }}
        
        /* 인라인 코드 */
        code {{
            background: #1A1A1A;
            padding: 3px 7px;
            border-radius: 4px;
            font-family: 'D2Coding', 'Consolas', 'Monaco', monospace;
            color: #9CDCFE;
            font-size: 13px;
        }}
        
        /* 코드 블록 */
        .code-block {{
            position: relative;
            margin: 10px 0;
        }}
        
        pre {{
            background: #0D0D0D;
            padding: 14px;
            border-radius: 8px;
            overflow-x: auto;
            border: 1px solid #333;
        }}
        
        pre code {{ 
            padding: 0; 
            background: transparent;
            color: #A9B7C6;
            font-size: 13px;
            line-height: 1.5;
        }}
        
        .copy-btn {{
            position: absolute;
            top: 8px;
            right: 8px;
            background: #404040;
            color: #CCC;
            border: none;
            padding: 4px 10px;
            border-radius: 4px;
            font-size: 11px;
            cursor: pointer;
            opacity: 0.7;
            transition: opacity 0.2s;
        }}
        .copy-btn:hover {{ opacity: 1; background: #505050; }}
        .copy-btn.copied {{ background: #4CAF50; color: white; }}
        
        /* 코드 블록 외부 복사 버튼 */
        .code-copy-btn {{
            background: #0078D4;
            color: #FFFFFF;
            border: none;
            padding: 8px 14px;
            border-radius: 8px;
            font-size: 12px;
            font-weight: 500;
            cursor: pointer;
            white-space: nowrap;
            flex-shrink: 0;
            transition: background 0.2s;
        }}
        .code-copy-btn:hover {{ background: #106EBE; }}
        .code-copy-btn.copied {{ background: #4CAF50; }}
        
        /* 상태 메시지 */
        .error {{ color: #F44336; }}
        .warning {{ color: #FFA726; }}
        .success {{ color: #4CAF50; }}
        
        /* 액션 버튼 */
        .action-btn {{
            display: inline-block;
            background: #0078D4;
            color: white;
            padding: 10px 18px;
            border-radius: 8px;
            margin: 10px 6px 10px 0;
            cursor: pointer;
            font-size: 13px;
            font-weight: 500;
            text-decoration: none;
            transition: background 0.2s;
        }}
        .action-btn:hover {{ background: #106EBE; }}
        
        /* 자동 수정 버튼 */
        .action-fix-btn {{
            display: inline-block;
            background: linear-gradient(135deg, #FF6B35, #F7931E);
            color: white;
            padding: 8px 14px;
            border-radius: 6px;
            margin: 6px 8px 6px 0;
            cursor: pointer;
            font-size: 12px;
            font-weight: 500;
            text-decoration: none;
            transition: all 0.2s;
            border: none;
        }}
        .action-fix-btn:hover {{ 
            background: linear-gradient(135deg, #E55A2B, #E8851A);
            transform: translateY(-1px);
            box-shadow: 0 2px 8px rgba(255, 107, 53, 0.3);
        }}
        
        /* 액션 결과 표시 */
        .action-result {{
            padding: 10px 14px;
            border-radius: 6px;
            margin: 8px 0;
            border-left: 3px solid;
        }}
        .action-result.success {{
            background: rgba(76, 175, 80, 0.15);
            border-color: #4CAF50;
        }}
        .action-result.error {{
            background: rgba(244, 67, 54, 0.15);
            border-color: #F44336;
        }}
        .action-result .status {{
            font-weight: bold;
            margin-right: 8px;
        }}
        .action-result.success .status {{ color: #4CAF50; }}
        .action-result.error .status {{ color: #F44336; }}
        
        /* 버전 경고 알림 - 상단 고정 */
        .version-alert {{
            background: rgba(255, 152, 0, 0.95);
            border: 1px solid #FF9800;
            border-radius: 0 0 8px 8px;
            padding: 10px 16px;
            margin: 0;
            display: flex;
            align-items: center;
            gap: 10px;
            position: sticky;
            top: 0;
            z-index: 100;
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
        }}
        .version-alert .alert-icon {{
            font-size: 16px;
            flex-shrink: 0;
        }}
        .version-alert span {{
            color: #1a1a1a;
            font-size: 12px;
            line-height: 1.4;
        }}
        .version-alert strong {{
            color: #000;
        }}
        
        /* 액션 버튼 컨테이너 */
        .action-buttons-container {{
            margin-top: 16px;
            padding-top: 12px;
            border-top: 1px solid #404040;
        }}
        .action-buttons-title {{
            color: #FF9800;
            font-weight: 600;
            margin-bottom: 10px;
            font-size: 13px;
        }}
        
        /* ACTION 마커 인라인 스타일 (본문에 표시되는 구체적 가이드) */
        .action-guide {{
            display: inline-block;
            background: rgba(33, 150, 243, 0.15);
            border-left: 3px solid #2196F3;
            padding: 8px 12px;
            margin: 6px 0;
            border-radius: 4px;
            font-family: 'D2Coding', 'Consolas', monospace;
            font-size: 12px;
            color: #81D4FA;
            line-height: 1.5;
        }}
        .action-guide::before {{
            content: '📋 {actionGuideLabel}';
            font-weight: bold;
            color: #2196F3;
        }}
        
        /* 코드 토글 (IE 호환) */
        .code-toggle-header {{
            cursor: pointer;
            color: #4FC3F7;
            font-weight: 500;
            padding: 12px 14px;
            background: #252526;
            border: 1px solid #404040;
            border-radius: 8px;
            margin: 10px 0;
            user-select: none;
        }}
        .code-toggle-header:hover {{
            color: #81D4FA;
            background: #2A2A2A;
        }}
        .toggle-icon {{
            font-size: 10px;
            margin-right: 6px;
            display: inline-block;
        }}
        .code-content {{
            background: #252526;
            border: 1px solid #404040;
            border-top: none;
            border-radius: 0 0 8px 8px;
            margin-top: -11px;
            padding: 0 14px 14px 14px;
        }}
        .code-content pre {{
            margin: 10px 0 0 0;
        }}
        
        /* Task 12.1: Specification Card Styles (IE11 compatible) */
        /* Requirements: 2.1, 2.5 */
        .spec-card {{
            background: #252526;
            border: 2px solid #4FC3F7;
            border-radius: 12px;
            padding: 16px;
            margin: 12px 0;
            box-shadow: 0 2px 8px rgba(79, 195, 247, 0.15);
        }}
        
        .spec-header {{
            display: block;
            padding-bottom: 12px;
            margin-bottom: 12px;
            border-bottom: 1px solid #404040;
        }}
        
        .spec-icon {{
            font-size: 18px;
            margin-right: 8px;
            vertical-align: middle;
        }}
        
        .spec-title {{
            font-size: 16px;
            font-weight: 600;
            color: #4FC3F7;
            vertical-align: middle;
        }}
        
        .spec-revision {{
            font-size: 12px;
            color: #888;
            margin-left: 10px;
            vertical-align: middle;
        }}
        
        .spec-section {{
            margin: 14px 0;
            padding: 10px 12px;
            background: #1E1E1E;
            border-radius: 8px;
            border-left: 3px solid #4FC3F7;
        }}
        
        .spec-label {{
            font-weight: 600;
            color: #FFD93D;
            margin-bottom: 8px;
            font-size: 13px;
        }}
        
        .spec-list {{
            margin: 8px 0 8px 20px;
            padding-left: 0;
        }}
        
        .spec-list li {{
            margin: 6px 0;
            color: #E0E0E0;
            line-height: 1.5;
        }}
        
        .spec-output {{
            color: #E0E0E0;
            margin: 4px 0;
        }}
        
        .spec-questions {{
            background: rgba(255, 152, 0, 0.1);
            border-left-color: #FF9800;
        }}
        
        .spec-questions .spec-label {{
            color: #FF9800;
        }}
        
        .spec-actions {{
            margin-top: 16px;
            padding-top: 14px;
            border-top: 1px solid #404040;
            text-align: center;
        }}
        
        /* Spec action buttons - using existing accent colors */
        .spec-btn {{
            display: inline-block;
            padding: 10px 20px;
            border-radius: 8px;
            margin: 4px 8px;
            cursor: pointer;
            font-size: 14px;
            font-weight: 500;
            border: none;
            transition: all 0.2s;
            text-decoration: none;
        }}
        
        .spec-btn.confirm {{
            background: #4CAF50;
            color: white;
        }}
        .spec-btn.confirm:hover {{
            background: #45A049;
            box-shadow: 0 2px 8px rgba(76, 175, 80, 0.3);
        }}
        
        .spec-btn.modify {{
            background: #0078D4;
            color: white;
        }}
        .spec-btn.modify:hover {{
            background: #106EBE;
            box-shadow: 0 2px 8px rgba(0, 120, 212, 0.3);
        }}
        
        .spec-btn.cancel {{
            background: #404040;
            color: #CCC;
        }}
        .spec-btn.cancel:hover {{
            background: #505050;
        }}
    </style>
</head>
<body>
{messagesHtml}
<script>
// Wait for document to be ready
(function() {{
    function init() {{
        window.scrollTo(0, document.body.scrollHeight);
        
        // Enable Ctrl+C copy functionality (IE-compatible)
        if (document.attachEvent) {{
            // IE8 and below
            document.attachEvent('onkeydown', handleKeyDown);
        }} else if (document.addEventListener) {{
            // Modern browsers
            document.addEventListener('keydown', handleKeyDown, false);
        }}
    }}
    
    function handleKeyDown(e) {{
        e = e || window.event;
        // Ctrl+C (or Cmd+C on Mac)
        if ((e.ctrlKey || e.metaKey) && e.keyCode === 67) {{
            try {{
                var selection = window.getSelection ? window.getSelection() : document.selection;
                if (selection) {{
                    var text = selection.toString ? selection.toString() : selection.createRange().text;
                    if (text && text.length > 0) {{
                        document.execCommand('copy');
                        if (e.preventDefault) e.preventDefault();
                        else e.returnValue = false;
                    }}
                }}
            }} catch(err) {{
                // Silent fail
            }}
        }}
    }}
    
    // Initialize when DOM is ready
    if (document.readyState === 'complete' || document.readyState === 'interactive') {{
        init();
    }} else if (document.addEventListener) {{
        document.addEventListener('DOMContentLoaded', init, false);
    }} else if (document.attachEvent) {{
        document.attachEvent('onreadystatechange', function() {{
            if (document.readyState === 'complete') init();
        }});
    }}
}})();

function copyCode(btn) {{
    var parent = btn.parentElement || btn.parentNode;
    var pres = parent.getElementsByTagName('pre');
    var pre = pres.length > 0 ? pres[0] : null;
    var code = pre ? (pre.innerText || pre.textContent) : '';
    
    // Create temporary textarea for copying
    var textarea = document.createElement('textarea');
    textarea.value = code;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    
    try {{
        document.execCommand('copy');
        btn.innerText = '{copyDone}';
        btn.className = btn.className + ' copied';
        setTimeout(function() {{
            btn.innerText = '{copyCode}';
            btn.className = btn.className.replace(' copied', '');
        }}, 1500);
    }} catch(err) {{
        // Silent fail
    }} finally {{
        document.body.removeChild(textarea);
    }}
}}

// Copy code from external button (finds code in sibling code-content)
function copyCodeFromBlock(btn) {{
    var wrapper = btn.parentElement || btn.parentNode;
    var bubble = wrapper.parentElement || wrapper.parentNode;
    var pres = bubble.getElementsByTagName('pre');
    var pre = pres.length > 0 ? pres[0] : null;
    var code = pre ? (pre.innerText || pre.textContent) : '';
    
    var textarea = document.createElement('textarea');
    textarea.value = code;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    
    try {{
        document.execCommand('copy');
        btn.innerHTML = '&#10003; {copyDone}';
        btn.className = btn.className + ' copied';
        setTimeout(function() {{
            btn.innerHTML = '&#128203; {copyCode}';
            btn.className = btn.className.replace(' copied', '');
        }}, 1500);
    }} catch(err) {{
        // Silent fail
    }} finally {{
        document.body.removeChild(textarea);
    }}
}}

// Toggle code visibility (IE7/8 compatible - no getElementsByClassName)
function toggleCode(header) {{
    var parent = header.parentElement || header.parentNode;
    var content = null;
    var icon = null;
    
    // IE-compatible: iterate children to find elements by class
    var children = parent.childNodes;
    for (var i = 0; i < children.length; i++) {{
        var child = children[i];
        if (child.className && child.className.indexOf('code-content') >= 0) {{
            content = child;
            break;
        }}
    }}
    
    // Find toggle icon in header
    var headerChildren = header.childNodes;
    for (var j = 0; j < headerChildren.length; j++) {{
        var hChild = headerChildren[j];
        if (hChild.className && hChild.className.indexOf('toggle-icon') >= 0) {{
            icon = hChild;
            break;
        }}
    }}
    
    if (content) {{
        if (content.style.display === 'none') {{
            content.style.display = 'block';
            header.style.borderRadius = '8px 8px 0 0';
            if (icon) icon.innerHTML = '▼';
        }} else {{
            content.style.display = 'none';
            header.style.borderRadius = '8px';
            if (icon) icon.innerHTML = '▶';
        }}
    }}
}}
</script>
</body>
</html>";
        }

        /// <summary>
        /// Triggers graph analysis. Credit check and re-entrancy guard applied.
        /// </summary>
        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isAnalyzing) return;
            try
            {
                await RunAnalysisAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError("ChatWorkspace.AnalyzeButton_Click", ex);
            }
        }

        private void AnalysisCancelButton_Click(object sender, RoutedEventArgs e)
        {
            _analysisCancelled = true;
            _analysisCts?.Cancel();
            AnalysisModal.Visibility = Visibility.Collapsed;
            ChatBrowser.Visibility = Visibility.Visible;
        }

        private async Task RunAnalysisAsync()
        {
            _isAnalyzing = true;
            _analysisCts?.Dispose();
            _analysisCts = new System.Threading.CancellationTokenSource();
            var token = _analysisCts.Token;
            _analysisCancelled = false;

            // Hide WebBrowser to avoid Airspace issue with modal overlay
            ChatBrowser.Visibility = Visibility.Collapsed;

            AnalysisModal.Visibility = Visibility.Visible;
            AnalysisProgressBar.Value = 0;
            AnalysisProgressText.Text = "0%";
            AnalysisStatusText.Text = L("Chat_AnalysisExtracting");

            // Force UI update using Dispatcher with higher priority
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            await Task.Delay(100);

            try
            {


                UpdateAnalysisProgress(10, L("Chat_AnalysisAccessWorkspace"));
                await Task.Delay(50);

                GraphAnalysisData graphData = null;

                // Run graph extraction on UI thread (required for Dynamo API access)
                await Dispatcher.InvokeAsync(() =>
                {
                    var graphReader = new GraphReader(_viewLoadedParams);
                    UpdateAnalysisProgress(15, L("Chat_AnalysisExtracting"));
                    graphData = graphReader.ExtractGraphData();
                });

                await Task.Delay(50);

                // B5: guard against null if InvokeAsync lambda threw
                if (graphData == null)
                {
                    AnalysisModal.Visibility = Visibility.Collapsed;
                    ChatBrowser.Visibility = Visibility.Visible;
                    _viewModel.AppendHtmlMessage($@"
<div class='message ai'>
    <div class='bubble'>
        <p class='error'>{EscapeHtml(LF("Chat_AnalysisException", "Graph data extraction returned null"))}</p>
    </div>
</div>");
                    return;
                }

                if (!string.IsNullOrEmpty(graphData.Error))
                {

                    AnalysisModal.Visibility = Visibility.Collapsed;
                    ChatBrowser.Visibility = Visibility.Visible;
                    _viewModel.AppendHtmlMessage($@"
<div class='message ai'>
    <div class='bubble'>
        <p class='error'>{EscapeHtml(LF("Chat_AnalysisFailed", graphData.Error))}</p>
    </div>
</div>");
                    return;
                }

                UpdateAnalysisProgress(20, LF("Chat_AnalysisExtractDone", graphData.NodeCount));
                await Task.Delay(50);

                var result = await AnalysisService.AnalyzeGraphAsync(graphData, (progress, status) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        AnalysisProgressBar.Value = progress;
                        AnalysisProgressText.Text = $"{progress}%";
                        AnalysisStatusText.Text = status;
                    }));
                }, token);

                AnalysisModal.Visibility = Visibility.Collapsed;
                ChatBrowser.Visibility = Visibility.Visible;

                if (_analysisCancelled) return;

                if (result.Success)
                {
                    string reportHtml = ConvertMarkdownToHtml(result.Report);
                    reportHtml = StyleActionMarkers(reportHtml);
                    string actionButtonsHtml = RenderActionButtons(result.Actions);

                    _viewModel.AppendHtmlMessage($@"
<div class='message ai'>
    <div class='bubble'>
        {reportHtml}
        {actionButtonsHtml}
    </div>
</div>");

                    string analysisPrompt = $"Graph analysis request (nodes {graphData.NodeCount})";

                    _viewModel.SaveAnalysisToHistory(result.Report);


                    NotificationHelper.ShowBalloonTip(
                        L("Common_BibimAi"),
                        L("Chat_AnalysisCompletedNotification"));

                    _pendingActions = result.Actions;
                }
                else
                {
                    string analysisPrompt = $"Graph analysis request (nodes {graphData.NodeCount})";


                    _viewModel.AppendHtmlMessage($@"
<div class='message ai'>
    <div class='bubble'>
        <p class='error'>{EscapeHtml(LF("Chat_AnalysisError", result.ErrorMessage))}</p>
    </div>
</div>");
                }
            }
            catch (OperationCanceledException) when (_analysisCancelled)
            {
                // User-initiated cancellation — modal already hidden by cancel button
            }
            catch (Exception ex)
            {

                AnalysisModal.Visibility = Visibility.Collapsed;
                ChatBrowser.Visibility = Visibility.Visible;
                _viewModel.AppendHtmlMessage($@"
<div class='message ai'>
    <div class='bubble'>
        <p class='error'>{EscapeHtml(LF("Chat_AnalysisException", ex.Message))}</p>
    </div>
</div>");
            }
            finally
            {
                _isAnalyzing = false;
                _analysisCts?.Dispose();
                _analysisCts = null;
            }
        }

        /// <summary>
        /// Updates analysis progress bar and status text.
        /// </summary>
        private void UpdateAnalysisProgress(int progress, string status)
        {
            AnalysisProgressBar.Value = progress;
            AnalysisProgressText.Text = $"{progress}%";
            AnalysisStatusText.Text = status;
        }

        private string ConvertMarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return "";

            // Use MarkdownRenderer with proper pipeline (includes table support)
            try
            {
                return MarkdownRenderer.ConvertToHtml(markdown);
            }
            catch
            {
                return $"<p>{System.Net.WebUtility.HtmlEncode(markdown).Replace("\n", "<br/>")}</p>";
            }
        }

        /// <summary>
        /// Replace [ACTION:...] markers with styled guide boxes for better visibility
        /// </summary>
        private string StyleActionMarkers(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;

            // Replace [ACTION:TYPE|params|DisplayText] with styled div
            var regex = new System.Text.RegularExpressions.Regex(@"\[ACTION:[^\]]+\]");
            return regex.Replace(html, match =>
            {
                string markerText = match.Value;
                // Extract just the display text (last part after final |)
                int lastPipe = markerText.LastIndexOf('|');
                string displayText = lastPipe > 0 
                    ? markerText.Substring(lastPipe + 1).TrimEnd(']')
                    : markerText;
                
                return $"<div class='action-guide'>{System.Net.WebUtility.HtmlEncode(displayText)}</div>";
            });
        }

        private void InputTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    return; // Allow newline
                }
                else
                {
                    e.Handled = true;
                    if (_viewModel?.SendCommand?.CanExecute(null) == true)
                    {
                        _viewModel.SendCommand.Execute(null);
                    }
                }
            }
        }

        private void ChatBrowser_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
        {
            // Handle custom protocol links (bibim://)
            if (e.Uri != null && e.Uri.Scheme == "bibim")
            {
                e.Cancel = true;
                HandleCustomLink(e.Uri.Host);
                return;
            }
            
            // Open external http/https links in default browser
            if (e.Uri != null && (e.Uri.Scheme == "http" || e.Uri.Scheme == "https"))
            {
                e.Cancel = true;
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = e.Uri.AbsoluteUri,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Logger.Log("ChatWorkspace.Navigating", $"Failed to open external link: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// WebBrowser Loaded event - enable context menu and document features
        /// Task 13.1: Register ObjectForScripting for window.external bridge
        /// </summary>
        private void ChatBrowser_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Task 13.1: Register scripting bridge for window.external calls
                // This enables JavaScript to call ConfirmSpec() and RequestChanges()
                ChatBrowser.ObjectForScripting = _scriptingBridge;
                
                // Suppress script errors
                dynamic activeX = ChatBrowser.GetType().InvokeMember("ActiveXInstance",
                    System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, ChatBrowser, new object[] { });

                if (activeX != null)
                {
                    activeX.Silent = true;
                }
                
                // Hook into Windows message loop to catch Ctrl+C inside WebBrowser
                // This is necessary because WPF KeyDown events don't reach inside WebBrowser
                ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
            }
            catch
            {
                // Ignore if can't access ActiveX
            }
        }
        
        /// <summary>
        /// Intercept Windows messages to catch Ctrl+C inside WebBrowser
        /// WPF WebBrowser doesn't forward keyboard events to WPF, so we need to hook at Win32 level
        /// </summary>
        private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
        {
            // WM_KEYDOWN = 0x0100
            const int WM_KEYDOWN = 0x0100;
            // VK_C = 0x43
            const int VK_C = 0x43;
            
            if (msg.message == WM_KEYDOWN && (int)msg.wParam == VK_C)
            {
                // Check if Ctrl is pressed
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // Check if WebBrowser has focus (or its child window)
                    if (IsWebBrowserFocused())
                    {
                        TryCopySelectedText();
                        // Don't set handled = true, let the browser also try to handle it
                        // This provides double-chance for copy to work
                    }
                }
            }
        }
        
        /// <summary>
        /// Check if WebBrowser or its child window has focus
        /// </summary>
        private bool IsWebBrowserFocused()
        {
            try
            {
                // Get the focused window handle
                IntPtr focusedHandle = GetFocus();
                if (focusedHandle == IntPtr.Zero) return false;
                
                // Get WebBrowser's window handle
                var hwndSource = PresentationSource.FromVisual(ChatBrowser) as HwndSource;
                if (hwndSource == null) return false;
                
                IntPtr browserHandle = hwndSource.Handle;
                
                // Check if focused window is browser or its child
                return focusedHandle == browserHandle || IsChild(browserHandle, focusedHandle);
            }
            catch
            {
                return false;
            }
        }
        
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetFocus();
        
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        /// <summary>
        /// Handle Ctrl+C copy in WebBrowser control (PreviewKeyDown)
        /// </summary>
        private void ChatBrowser_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Let KeyDown handle it instead
        }

        /// <summary>
        /// Handle Ctrl+C copy in WebBrowser control (KeyDown)
        /// </summary>
        private void ChatBrowser_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                TryCopySelectedText();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Try to copy selected text from WebBrowser to clipboard
        /// Multiple fallback strategies for maximum compatibility
        /// </summary>
        private void TryCopySelectedText()
        {
            try
            {
                // Strategy 1: Use IE document.selection API
                dynamic doc = ChatBrowser.Document;
                if (doc != null)
                {
                    try
                    {
                        dynamic selection = doc.selection;
                        if (selection != null)
                        {
                            dynamic range = selection.createRange();
                            if (range != null)
                            {
                                string text = range.text;
                                if (!string.IsNullOrEmpty(text))
                                {
                                    System.Windows.Clipboard.SetText(text);
                                    return;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("ChatWorkspace.TryCopySelectedText", $"Strategy 1 failed: {ex.Message}");
                    }

                    // Strategy 2: Use execCommand
                    try
                    {
                        bool success = doc.execCommand("Copy", false, null);
                        if (success)
                            return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("ChatWorkspace.TryCopySelectedText", $"Strategy 2 failed: {ex.Message}");
                    }

                    // Strategy 3: Get body.innerText as last resort
                    try
                    {
                        dynamic body = doc.body;
                        if (body != null)
                        {
                            string bodyText = body.innerText;
                            if (!string.IsNullOrEmpty(bodyText))
                            {
                                System.Windows.Clipboard.SetText(bodyText);
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("ChatWorkspace.TryCopySelectedText", $"Strategy 3 failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ChatWorkspace.TryCopySelectedText", $"Copy failed completely: {ex.Message}");
            }
        }

        /// <summary>
        /// Context menu - Copy selected text
        /// </summary>
        private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TryCopySelectedText();
        }

        /// <summary>
        /// Context menu - Select all text
        /// </summary>
        private void SelectAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                dynamic doc = ChatBrowser.Document;
                if (doc?.body != null)
                {
                    doc.execCommand("SelectAll", false, null);
                }
            }
            catch
            {
                // Silent fail
            }
        }

        /// <summary>
        /// Handle right-click on WebBrowser
        /// </summary>
        private void ChatBrowser_RightClick(object sender, MouseButtonEventArgs e)
        {
            // Context menu will show automatically
            // This handler is just to ensure the event is processed
        }

        private void HandleCustomLink(string action)
        {
            if (action.StartsWith("action_"))
            {
                ExecuteAnalysisAction(action);
            }
        }

        /// <summary>
        /// Render ACTION buttons as clickable HTML elements (DISABLED - Feature postponed)
        /// </summary>
        private string RenderActionButtons(List<AnalysisAction> actions)
        {
            // Auto-fix buttons disabled for now - will be added in future version
            return "";
        }

        /// <summary>
        /// Get localized label for action type
        /// </summary>
        private string GetActionTypeLabel(ActionType type)
        {
            switch (type)
            {
                case ActionType.ADD_NODE: return L("ActionType_AddNode");
                case ActionType.DELETE_NODE: return L("ActionType_DeleteNode");
                case ActionType.REPLACE_NODE: return L("ActionType_ReplaceNode");
                case ActionType.CONNECT: return L("ActionType_Connect");
                case ActionType.DISCONNECT: return L("ActionType_Disconnect");
                case ActionType.RECONNECT: return L("ActionType_Reconnect");
                case ActionType.FIX_CODE: return L("ActionType_FixCode");
                case ActionType.REPLACE_CODE: return L("ActionType_ReplaceCode");
                case ActionType.SET_VALUE: return L("ActionType_SetValue");
                case ActionType.SET_LACING: return L("ActionType_SetLacing");
                case ActionType.GROUP_NODES: return L("ActionType_GroupNodes");
                case ActionType.ADD_NOTE: return L("ActionType_AddNote");
                default: return L("ActionType_Execute");
            }
        }

        /// <summary>
        /// Execute an analysis action and show result
        /// </summary>
        private void ExecuteAnalysisAction(string actionId)
        {
            Logger.Log("ChatWorkspace.ExecuteAnalysisAction", $"called: {actionId} pendingCount={_pendingActions?.Count ?? 0}");

            try
            {
                var action = _pendingActions?.FirstOrDefault(a => a.ActionId == actionId);
                if (action == null)
                {
                    Logger.Log("ChatWorkspace.ExecuteAnalysisAction", $"Action not found: {actionId}");
                    _viewModel.AppendHtmlMessage($@"
<div class='message ai'>
    <div class='bubble'>
        <p class='error'>{EscapeHtml(LF("Chat_ActionNotFound", actionId))}</p>
    </div>
</div>");
                    return;
                }

                Logger.Log("ChatWorkspace.ExecuteAnalysisAction", $"Executing: {action.Type} nodeId={action.TargetNodeId}");

                // Execute the action
                var result = _nodeManipulator.ExecuteAction(action);

                Logger.Log("ChatWorkspace.ExecuteAnalysisAction", $"Result: success={result.Success}");

                // Show result report
                string resultHtml = result.ToHtmlReport();
                _viewModel.AppendHtmlMessage($@"
<div class='message ai'>
    <div class='bubble'>
        <h3>{EscapeHtml(L("Chat_ActionResultTitle"))}</h3>
        {resultHtml}
    </div>
</div>");

                // Remove executed action from pending list
                _pendingActions.Remove(action);
            }
            catch (Exception ex)
            {
                Logger.LogError("ChatWorkspace.ExecuteAnalysisAction", ex);
                _viewModel.AppendHtmlMessage($@"
<div class='message ai'>
    <div class='bubble'>
        <p class='error'>{EscapeHtml(LF("Chat_ActionExecutionError", ex.Message))}</p>
    </div>
</div>");
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsMenu.IsOpen = true;
        }

        private void ApiKeySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var setupView = new ApiKeySetupView();
                var window = Window.GetWindow(this);
                if (window != null) setupView.Owner = window;
                setupView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.LogError("ChatWorkspace.ApiKeySettings_Click", ex);
            }
        }

        private void HistoryOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel?.ToggleHistoryPanelCommand?.CanExecute(null) == true)
            {
                _viewModel.ToggleHistoryPanelCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Task 7.1: Inverse Boolean Converter for IsEnabled binding
    /// Converts true to false and false to true
    /// </summary>
    public class InverseBooleanConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }
    }
}
