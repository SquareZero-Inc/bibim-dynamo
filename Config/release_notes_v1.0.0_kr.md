# BIBIM AI v1.0.0 — 공개 오픈소스 릴리즈

**릴리즈일**: 2026-04-15

**BIBIM AI**는 Autodesk Revit용 AI 기반 Dynamo 스크립트 어시스턴트입니다.
자연어로 Dynamo Python 스크립트를 생성·분석·디버깅할 수 있으며, 구독 없이 사용할 수 있습니다.

## 주요 변경사항

### BYOK (내 API 키 직접 사용)
- 별도 서버·계정·구독 없이 본인 API 키로 직접 AI 추론
- **Anthropic Claude** (필수) + **Google Gemini** (선택, 향후 RAG 기능용) 지원
- 키는 DLL 옆 `rag_config.json`에 로컬 저장 — 제3자 서버로 전송되지 않음

### AI 코드 생성 파이프라인
- 자연어 → Dynamo Python 스크립트 즉시 생성
- 생성 전 스펙 확인 단계 — 실행 전 무엇을 만들지 검토 가능
- 자동 수정 루프: 검증 실패 시 전략 에스컬레이션으로 최대 2회 자동 재시도
- RAG (Revit API 문서 검색)는 이번 OSS 릴리즈에서 일시적으로 비활성화 — 곧 업데이트 예정

### 노드 그래프 분석
- 현재 Dynamo 그래프 원클릭 분석
- 문제 감지, 개선 제안, 노드 연결 구조를 자연어로 설명

### 멀티 버전 지원
| Revit | Dynamo | 런타임 |
|-------|--------|--------|
| 2022–2024 | 2.x | .NET 4.8 |
| 2025 | 3.3.0 | .NET 8 |
| 2026 | 3.6.1 | .NET 8 |
| 2027 | 27.0 | .NET 10 |

### 세션 히스토리
- 대화 기록은 `%APPDATA%/BIBIM/history/sessions.json`에 로컬 저장
- 클라우드 동기화 없음, 데이터 외부 전송 없음

### 다국어 지원
- UI 한국어 / 영어 지원
- 설치 시 언어 선택

## 요구사항
- Autodesk Revit 2022 이상
- Claude API 키 ([console.anthropic.com](https://console.anthropic.com/))
- Google Gemini API 키 — 선택사항, RAG 기능 활성화 시 필요 ([aistudio.google.com/apikey](https://aistudio.google.com/apikey))

## 설치
설치 파일(`BIBIM_AI_Setup_v1.0.0.exe`) 실행 후 안내에 따라 진행하세요.
첫 실행 시 API 키 입력 창이 나타납니다.

## RAG 안내
이번 릴리즈에서 RAG(Gemini fileSearch 기반 Revit API 문서 검색)는 **일시적으로 비활성화**되어 있습니다.
코퍼스가 SquareZero 사설 Google Cloud 프로젝트에 호스팅되어 있어 OSS 사용자 키로는 접근이 불가능했습니다.
현재 OSS 환경에 맞는 접근 방식으로 재구성 중입니다.
RAG 없이도 코드 생성은 정상 동작합니다 — Claude의 Revit API 내장 지식이 대부분의 케이스를 커버합니다.

## 소스
[github.com/SquareZero-Inc/bibim-dynamo](https://github.com/SquareZero-Inc/bibim-dynamo)
