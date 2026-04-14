# BIBIM AI v1.0.0 릴리즈 노트

**릴리즈일**: 2026-04-15

## 🎉 오픈소스 공개 (BYOK)

BIBIM AI가 오픈소스로 공개됩니다.

### BYOK (Bring Your Own Key)
- 구독 없이 **본인의 API 키로 직접 사용**하는 방식으로 전환되었습니다.
- Anthropic API 키(필수)와 Google Gemini API 키(선택, RAG 기능용)만 있으면 됩니다.
- 첫 실행 시 API 키 설정 화면이 자동으로 표시되며, 설정 후 즉시 사용 가능합니다.

### 주요 기능
- **코드 생성**: 자연어로 설명하면 Dynamo Python 스크립트를 자동 생성합니다.
- **그래프 분석**: 기존 Dynamo 그래프를 분석하고 개선 방향을 제안합니다.
- **RAG (선택)**: Gemini를 통한 Revit API 문서 검색으로 더 정확한 코드를 생성합니다.
- **다중 버전 지원**: Revit 2022–2027 / Dynamo 2.x–4.x 전 버전 지원.

### 지원 환경
- Revit 2022 이상 + Dynamo 설치 필수
- Anthropic API 키 (https://console.anthropic.com/)
- Google Gemini API 키 (선택, https://aistudio.google.com/)
