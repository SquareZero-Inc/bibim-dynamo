# BIBIM AI v1.0.2

**릴리즈일**: 2026-04-28

## 주요 변경사항

### 멀티 프로바이더 백엔드 연결
설정 다이얼로그에 3개 프로바이더 섹션(Anthropic / OpenAI / Google)과 4개 모델 옵션(Sonnet 4.6 / Opus 4.7 / GPT-5.5 / Gemini 3.1 Pro)이 표시됩니다. 코드 생성 파이프라인이 활성 모델 ID에 맞춰 해당 프로바이더로 요청을 라우팅합니다 — GPT-5.5 선택 시 OpenAI Chat Completions API, Gemini 3.1 Pro 선택 시 `:generateContent`로 자동 분기. 팩토리 + 3개 어댑터는 `Services/Providers/`에 있습니다.

> 이번 빌드에서 Dynamo 워크로드 기준 end-to-end 검증된 프로바이더는 **Anthropic Claude** 입니다. OpenAI / Gemini 라우팅은 코드 경로가 연결되어 빌드는 정상 통과하지만, autofix 루프와 그래프 분석 파이프라인 전체 회귀 커버리지는 v1.1에서 진행됩니다. 비-Anthropic 모델로 문제가 생기면 설정에서 Claude 모델로 되돌려 주세요.

### Anthropic Prompt Caching 활성
5분 윈도우 내의 spec → codegen → autofix 호출이 캐시된 시스템 프롬프트를 공유합니다. 매 요청에서 시스템 프롬프트를 `cache_control: ephemeral` 마커가 붙은 텍스트 블록 배열로 보내고, `anthropic-beta: prompt-caching-2024-07-31` 헤더도 함께 전송합니다. 호출별 가변 컨텍스트(Revit API 문서, 이전 그래프 분석)는 시스템 프롬프트 밖으로 빼서 user 메시지로 합쳐 보내, 캐시 prefix가 호출 사이에 bit-stable하게 유지됩니다.

`TokenTracker`가 `usage.cache_creation_input_tokens` / `usage.cache_read_input_tokens`를 읽어 `SessionCacheCreationTokens`, `SessionCacheReadTokens`, `SessionCacheHitRatio`로 노출합니다. 몇 차례 사용 후 `[TokenTracker]` 로그 라인에서 hit ratio가 올라가는지 확인할 수 있습니다.

### API 호출당 채팅 히스토리 다이어트
긴 세션에서 매 호출마다 모든 이전 턴을 Claude로 보내지 않습니다. `BuildMessageHistory`가 직렬화 직전 20-message trailing window를 적용하고, `GetConversationHistoryForSpec`도 동일 윈도우를 거치도록 연결되어 spec 생성이 더 이상 윈도우를 우회하지 않습니다. 코드 응답이 인메모리 히스토리에 저장될 때도 압축되어 전체 Python 본문은 `[Generated Dynamo Python script — ~N lines]`로 대체되고 GUIDE 섹션만 원문 유지됩니다. `_conversationHistory`(UI / 영속화)와 `_contextManager`(원본 복구용)는 여전히 풀 콘텐츠를 봅니다.

### 더 빠른 파이프라인 (Verify 스테이지 제거)
선택적 Gemini 기반 verify 스테이지가 제거되었습니다. 새 파이프라인: 로컬 BM25 RAG → Claude 코드 생성 → 로컬 검증 → autofix 루프. verify 스테이지는 OSS 유저가 접근 불가능한 SquareZero 사설 fileSearch 코퍼스에 의존했고, 로컬 검증 게이트가 이미 같은 종류의 이슈를 잡고 있었습니다. 제거로 Gemini 키가 설정된 경우 워스트 케이스 코드 생성 지연 시간 최대 ~25초 단축.

### Auto-Fix가 Codegen 시스템 프롬프트를 공유
`RequestValidationAutoFixAsync`가 자체 짧은 fix-only 시스템 프롬프트 대신 `CodeGenSystemPrompt.Build(...)`를 사용합니다. 캐시 prefix를 원본 코드 생성 호출과 공유하므로 autofix 재시도가 별도 캐시 슬롯을 만들지 않고 ~30 토큰만 추가 결제됩니다. `AutoFixRequestBuilder`는 user 메시지에 Revit 2024+ breaking-changes 블록이나 common API patterns 블록을 더 이상 재발행하지 않습니다 — 해당 룰은 공유 시스템 프롬프트에만 존재합니다.

### 기타 최적화
- 호출 유형별 `max_tokens` 차등 (spec / autofix = 2048, codegen = 4096, analysis = 3072) — truncation 줄이고 현실적 예산 유지
- 로컬 BM25 RAG 다이어트: `TopK` 5 → 3, chunk 표시 한도 3000 → 1200 자, 멤버별 `Remarks` 필드 제거
- 그래프 분석 JSON 직렬화를 compact로 전환 (`WriteIndented = false`)
- Legacy `RagService.cs`, `RagQueryPrompt.cs`, `RagVerificationPrompt.cs` 삭제 / `AnalysisService`를 `LocalDynamoRagService`로 마이그레이션

### API 키 발급 가이드 링크 추가
API Key Setup 다이얼로그 상단에 **"📖 API 키 발급 가이드 보기"** 버튼이 있습니다. Anthropic / OpenAI / Google 각각의 키 발급 절차를 단계별로 안내하는 노션 페이지가 열립니다(한국어/영문 자동 분기).

### 설정 다이얼로그 전면 한국어화
이전 빌드에서는 API Key Setup 다이얼로그의 대부분 레이블(섹션 제목, 설명 문구, "Active Model", Cancel / Save 버튼, "✓ Saved" 배지, 비활성 모델 툴팁)이 한국어 빌드에서도 영어로 표시됐습니다. v1.0.2에서 모든 노출 문자열을 `LocalizationService` 경로로 통일 — KR 빌드가 처음부터 끝까지 자연스럽게 한국어로 읽힙니다.

### 모델별 응답 속도 표시
API Key Setup 다이얼로그의 각 모델 라디오 옆에 ⚡ 글리프가 인라인으로 표시됩니다 (Sonnet 4.6 = ⚡⚡⚡, Opus 4.7 / GPT-5.5 = ⚡⚡, Gemini 3.1 Pro = ⚡). 한국어 툴팁(빠름 / 보통 / 느림)도 함께 노출되어 모델 선택 시 응답 속도와 깊이의 trade-off를 한눈에 볼 수 있습니다.

## 버그 수정 / 개선
- 모든 흐름(스펙 생성 / 코드 생성 / 재시도 / 질문 응답)에서 로딩 상태 정리가 `finally` 블록으로 안전하게 처리되는지 검증 완료. API 오류 시 패널이 "Loading…" 상태에 멈추지 않습니다.
- 같은 세션에서 여러 키를 연속 저장할 때 save 배지 라벨이 정확히 갱신
- `BIBIM-101` 수정: `GetConversationHistoryForSpec`이 이제 `BuildMessageHistory`와 동일한 trailing window를 적용 — spec 생성이 풀 히스토리를 무한정 보내지 않습니다.
- net48 (`R2024_D293`) 빌드 안정화: `BIBIM_Extension.cs`가 `System.Threading.Tasks`를 명시적 import (`ImplicitUsings`는 .NET 8+ 전용이라 해당 빌드에서 누락 발생).

## 요구사항
- Autodesk Revit 2022 이상 + Dynamo 설치
- Anthropic API 키 (검증된 경로 기준)
- 선택: OpenAI / Google 키도 저장하면 해당 프로바이더로 라우팅됨 — v1.1 풀 검증 예정

## 소스
[github.com/SquareZero-Inc/bibim-dynamo](https://github.com/SquareZero-Inc/bibim-dynamo)
