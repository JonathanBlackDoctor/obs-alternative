# CLAUDE.md — SilentStream 개발 규약

> 이 파일은 이 리포지토리에서 작업하는 Claude Code에게 **자동 적용되는 공유 규약**입니다.
> 단일 진실 공급원(SoT)은 [`SilentStream_개발계획서.md`](./SilentStream_개발계획서.md) 이며,
> Phase별 실행 방법은 [`docs/EXECUTION_GUIDE.md`](./docs/EXECUTION_GUIDE.md) 를 따릅니다.
> 신규 기능(교시 VOD·폰 원격) 확장 계획은 [`SilentStream_확장계획서_교시VOD_폰원격제어.md`](./SilentStream_확장계획서_교시VOD_폰원격제어.md) 입니다.

## 프로젝트 한 줄 요약
OBS 대체용 **완전 자동·비가시적 YouTube 송출 + 로컬 백업 녹화** 프로그램.
C# / .NET 8 (WPF) + FFmpeg, Windows 10/11 전용, 한국어 UI.

## 작업 규약 (반드시 준수)

1. **브랜치**: `claude/relaxed-davinci-qmmjwl` 에서만 개발/커밋/푸시한다. 다른 브랜치(예: `main`)에 푸시하려면 사용자 허락을 받는다.
2. **PR**: 사용자가 명시적으로 요청하기 전까지 PR을 생성하지 않는다.
3. **완료 기준(AC) 우선**: 각 Phase/Task는 계획서에 정의된 완료 기준(AC)을 충족해야 "완료"다. AC를 못 채우면 진행을 멈추고 보고한다.
4. **불확실하면 질문**: 명세가 모호하면 추측하지 말고 `AskUserQuestion`으로 물어본 뒤 진행한다.
5. **커밋 단위**: 의미 있는 작업 단위마다 커밋한다. 한 커밋에 여러 Phase를 섞지 않는다.

## 오케스트레이션 실행 모델

- 상위 **Orchestrator**가 Phase를 순차/병렬로 위임하고, 각 Phase의 AC를 검증한 뒤 다음으로 넘어간다.
- 서브 에이전트(Infra / Media / YouTube / UI / Packaging)는 **자기 영역만** 구현하고, 다른 영역은 `Core/Contracts`의 인터페이스에만 의존한다.
- **인터페이스 고정 규칙**: 모듈 간 인터페이스는 Phase 0에서 고정한다. 시그니처 변경이 필요하면 Orchestrator가 승인하고, 의존하는 모든 모듈에 전파한 뒤 한 커밋으로 묶는다.

## 코드 컨벤션

- **언어**: 코드 식별자·주석은 영어. 사용자 대면 UI 문자열·로그 메시지는 한국어.
- **타깃**: .NET 8.0 / WPF. 비동기는 `async/await` + `CancellationToken` 전파.
- **DI**: 생성자 주입. 모듈은 인터페이스(`Core/Contracts`)로만 결합한다.
- **민감정보**: OAuth 토큰은 DPAPI로 암호화 저장한다. 토큰/시크릿/`config.json`을 커밋하지 않는다.
- **스타일**: 주변 코드의 네이밍·들여쓰기·주석 밀도를 따른다.

## 빌드·테스트 환경 주의

- 이 프로젝트는 **Windows 전용**이다. WPF / DXGI Desktop Duplication / WASAPI는 Linux에서 빌드·실행되지 않는다.
- **클라우드(웹) 세션이 Linux 컨테이너라면** 실제 빌드·캡처·송출 검증은 불가하다. 그 경우 코드 생성·정적 검증까지만 하고, 실행 검증이 필요한 AC는 "로컬 Windows 검증 필요"로 표시한 뒤 사용자에게 보고한다.
- 로컬 환경별 설정(SDK/FFmpeg 경로, 자격증명 위치 등)은 `CLAUDE.local.md`(커밋 안 함)에 둔다. 템플릿: `docs/CLAUDE.local.md.template`.

## 법적·윤리적 고지

본 도구는 **사용자 본인 소유 PC의 자기 기록·방송 목적**을 전제로 한다. 설치/최초 실행 시 동의 화면을 표시하고, 타인의 화면·음성을 동의 없이 캡처하는 용도로 사용할 수 없음을 EULA에 명시한다.
