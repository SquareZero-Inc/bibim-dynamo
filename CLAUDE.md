# BIBIM AI — Dynamo Plugin

Revit/Dynamo용 AI 코드 생성 플러그인. Dynamo Python 스크립트를 자연어로 생성·분석.  
백엔드: Claude (Anthropic) + Gemini (RAG). OSS BYOK — 서버 없음, 로컬 JSON 스토리지.

---

## 빌드

```bash
# 개발/디버그 (Revit 2025 + Dynamo 3.3.0, net8.0)
dotnet build BIBIM_MVP.csproj -c Debug

# 특정 Revit+Dynamo 버전 타겟 빌드
dotnet build BIBIM_MVP.csproj -c R2026_D361   # Revit 2026, Dynamo 3.6.1 (net8.0)
dotnet build BIBIM_MVP.csproj -c R2025_D330   # Revit 2025, Dynamo 3.3.0 (net8.0)
dotnet build BIBIM_MVP.csproj -c R2024_D293   # Revit 2024, Dynamo 2.19.3 (net48)

# 언어 빌드 (기본값: kr)
dotnet build BIBIM_MVP.csproj -c Debug -p:AppLanguage=en
dotnet build BIBIM_MVP.csproj -c Debug -p:AppLanguage=kr
```

출력 경로: `bin/{언어}/{Configuration}/`  
버전 단일 소스: `Directory.Build.props` (현재 `2.4.1`)

---

## 아키텍처

```
BIBIM_AI/                            # Git 루트 (이 디렉토리)
├── BIBIM_Extension.cs           # Dynamo IViewExtension 진입점
├── Common/
│   ├── ServiceContainer.cs      # DI 컨테이너 (부분 적용, Singleton 혼재)
│   ├── ConfigService.cs         # rag_config.json 로드 (static)
│   └── Logger.cs
├── Config/
│   ├── rag_config.json          # API 키·모델·RAG store 설정 (.gitignore)
│   └── i18n/kr.json, en.json    # 로컬라이제이션 문자열
├── Models/
│   ├── SessionModels.cs         # ChatSession / SingleMessage / SessionContext 등
│   ├── CodeSpecification.cs     # 스펙 DTO
│   └── GenerationResult.cs      # 파이프라인 결과 파싱 (TYPE: 프로토콜)
├── Services/
│   ├── HistoryManager.cs        # OSS stub — 로컬 히스토리 없음 (LocalSessionManager 사용)
│   ├── TokenTracker.cs          # 인메모리 토큰 사용량 누적 (세션 단위)
│   ├── GeminiService.cs         # 파이프라인 오케스트레이터 (RAG→Claude→검증)
│   ├── ClaudeApiClient.cs       # Claude API HTTP 호출 + 토큰 트래킹
│   ├── RagService.cs            # Gemini RAG fetch/verify/cache/키워드 추출
│   ├── GenerationPipelineService.cs  # 파이프라인 phase → i18n 상태 콜백
│   ├── LocalCodeValidationService.cs # 코드 검증 + ValidateAndFix
│   ├── LocalSessionManager.cs   # %APPDATA%/BIBIM/history/sessions.json 관리
│   ├── ConversationContextManager.cs # 세션 컨텍스트·재시도 컨텍스트 관리
│   └── AnalysisService.cs       # 노드 그래프 분석 (Claude + Gemini RAG)
├── ViewModels/
│   └── ChatWorkspaceViewModel.cs    # 메인 ViewModel
└── Views/
    ├── ChatWorkspace.xaml(.cs)  # 메인 채팅 UI (WebBrowser 기반)
    ├── ApiKeySetupView.xaml(.cs) # BYOK API 키 설정 다이얼로그
    └── TopNavigationBar.xaml(.cs) # 상단 네비게이션 바
```

---

## 코드 생성 파이프라인

```
사용자 입력
  → SpecGenerator (스펙 생성, Claude)
  → 사용자 확인/수정 (JavaScript bridge → WPF)
  → GenerationPipelineService (phase 상태 콜백)
      → RagService.FetchRelevantApiDocsAsync (RAG, Gemini)
      → ClaudeApiClient.CallClaudeApiAsync (코드 생성)
      → RagService.VerifyAndFixCodeAsync (RAG 검증)
      → LocalCodeValidationService.ValidateAndFix (로컬 검증)
      → 실패 시 AutoFixRequestBuilder → ClaudeApiClient 재호출 (최대 2회, 전략 에스컬레이션)
  → 결과 반환
```

---

## 멀티 타겟 빌드 주의사항

- **net8.0** (Revit 2025+): `ImplicitUsings`, C# 최신 문법 사용 가능
- **net48** (Revit 2022–2024): C# 7.3 제한, `using` 선언 불가, JSON은 `Newtonsoft.Json` 사용
- `#if NET48` / `#else` 분기로 JSON 파싱 이중 구현 다수 존재 (`JsonHelper.cs`로 래핑)
- net48 타겟에서는 `System.Text.Json` 사용 불가

---

## 로컬 스토리지 구조

| 경로 | 용도 |
|------|------|
| `%APPDATA%/BIBIM/history/sessions.json` | 대화 세션 (ChatSession / SingleMessage) |
| `%APPDATA%/BIBIM/logs/bibim_debug.txt` | 디버그 로그 (자동 로테이션) |
| `{DLL위치}/rag_config.json` | API 키·모델·RAG store 설정 |

---

## 로컬라이제이션

- 문자열 키: `Config/i18n/kr.json`, `Config/i18n/en.json`
- XAML: `{loc:Loc 키이름}` (LocExtension)
- C#: `LocalizationService.Get("키")`, `LocalizationService.Format("키", args)`
- 빌드 시 `-p:AppLanguage=en|kr`로 언어 결정, `AppLanguage.Current` 런타임 접근

---

## 앱 시작 플로우 (OSS BYOK)

1. `AppLanguage.Initialize()` + `LocalizationService.Initialize()` — Startup에서
2. `ServiceContainer.Initialize()` — DI 컨테이너 초기화 (`IVersionChecker` 등록)
3. `ClaudeApiClient.GetClaudeApiKey()` — `rag_config.json`에서 키 확인
4. 키 없으면 `ApiKeySetupView` 다이얼로그 표시 → 저장 후 재확인
5. 키 있으면 `ChatWorkspace` 윈도우 오픈

---

## 기술 부채

- **DI 마이그레이션**: `GeminiService`, `ConfigService` static → 인터페이스 기반으로 전환 예정
- **Claude C# SDK 도입**: 현재 수동 RAG→생성→검증 루프 → Tool Use 루프로 교체 예정

---

## 자주 쓰는 파일

| 목적 | 파일 |
|------|------|
| 버전 변경 | `Directory.Build.props` |
| UI 문자열 추가 | `Config/i18n/kr.json` + `en.json` |
| 프롬프트 수정 | `Services/Prompts/` |
| API 키·모델 설정 | `Config/rag_config.json` (gitignored — 직접 편집) |
| 릴리즈 노트 | `Config/release_notes_v*.md` |
