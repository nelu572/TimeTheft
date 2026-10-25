# TimeTheft AI 개발 지침

## 프로젝트 기본 정보

- 프로젝트명: TimeTheft
- 개발 형태: 7일 게임잼, 1인 개발
- 핵심 목표: 제한된 기간 안에 핵심 플레이 루프를 완성하고 완성도 있는 게임을 제작한다.
- 사용자와의 소통 및 프로젝트 문서는 한국어로 작성한다.

## 기술 스택과 현재 구조

- 엔진: Unity 6, `6000.0.68f1` ([버전 정보](ProjectSettings/ProjectVersion.txt))
- 언어: C#. URP `17.0.4`, Input System `1.18.0`, uGUI `2.0.0`, Test Framework `1.6.0` ([패키지 목록](Packages/manifest.json)).
- 실제 게임은 Notion의 분 단위 징수/배급 규칙(v0.4)과 시계 선선택 UX(사용자 추가 지시 v0.5)를 사용한다. 기획의 원문은 Notion에만 두고, 구현 상태·잠정 설정·검증 근거는 [HANDOFF.md](HANDOFF.md)에 기록한다.
- 아이템 공통 기반·6종 효과·자동 지급/보유·기대값 기반 AI 사용/보존/조합 전략을 구현했다. 시작 2개/이후 묶음 1개/보유 상한 3개 및 양측 종류 공개는 밸런스 검증용 잠정 정책이다. AI는 최적 전략이 아니며 난이도 단계·직접 쓰는 메모는 미구현이다. 수치·중복/조합 정책·수갑 예외·검증 근거는 HANDOFF.md를 따른다. 과거 규칙의 아이템 기록을 현행 요구사항으로 해석하지 않는다.

| 경로 | 역할 |
| --- | --- |
| `Assets/Scenes/TimeTheftGame.unity` | 유일한 메인 개발·플레이 씬. 기본 Build Profiles 목록의 첫 활성 씬이다. |
| `Assets/Scenes/TabletopStudy.unity` | 배치·Focus 실험용. 게임 진행과 분리해 보존한다. |
| `Assets/Scenes/CounterStructureStudy.unity` | 통합 계수기의 독립 제작·실험용. 고정 표시값을 사용한다. |
| `Assets/TimeTheft/Scripts/TimeTheftMatch.cs` | 현행 규칙·시계 상태·턴·수명·Time Bank의 유일한 소유자. 순수 C#이다. |
| `Assets/TimeTheft/Scripts/TimeTheftMatch.Inventory.cs` | 같은 Match의 묶음별 자동 지급·상한·지급 결과·공개 인벤토리 관찰. Editor 테스트 주입과 분리한다. |
| `Assets/TimeTheft/Scripts/TimeTheftAiObservation.cs`, `TimeTheftAi.Strategy.cs` | 공개 다중집합·자기 열쇠 지식·합법 ID의 불변 관찰과 제한된 AI 계획 평가. Match·숨은 위치 배열·게임 RNG/시드는 전달하지 않는다. |
| `Assets/TimeTheft/Scripts/TimeTheftSettlement.cs` | 실제 D와 AI 가상 평가가 공유하는 무상태 정수 분 계산. 실제 상태 반영과 승패 소유자는 Match다. |
| `Assets/TimeTheft/Scripts/TimeTheftGameController.cs` | 입력·AI 호출·공개/정산 코루틴·재시작을 연결한다. |
| `Assets/TimeTheft/Scripts/TimeTheftGameView.cs` | 씬에 배치한 시계·계수기·행동 UI·결과 전표를 표시한다. |
| `Assets/TimeTheft/Scripts/ClockTokenView.cs`, `Assets/TimeTheft/Prefabs/ClockToken.prefab` | 제작 중 모델과 독립된 임시 시계 표시부. 메인 씬에서 5개를 재사용한다. |
| `Assets/TimeTheft/TimeTheftSettings.asset` | 현행 규칙의 잠정 수치·숫자 분포·선공·AI 성향·연출 시간·재현용 시드. |
| `Assets/TimeTheft/Scripts/TabletopCameraLook.cs`, `TabletopFocusCamera.cs`, `TabletopFocusTarget.cs` | 기존 자유 시차·호버·Focus·열람 입력. 카메라 및 시차 설정을 임의로 변경하지 않는다. |
| `Assets/TimeTheft/Scripts/Legacy/` | 계수기 Study가 참조하는 `TabletopClockView`·`TabletopDecisionObject`와 표시용 `ClockAction`만 보존한다. 구형 규칙 모델·Controller·UI·설정은 제거했다. 메인에서 사용하지 않는다. |
| `Assets/TimeTheft/Prefabs/TabletopClock.prefab` | 계수기 Study의 비교용 시계. 표시 스크립트와 GUID를 보존한다. 제작 중인 최종 회중시계 모델이 아니다. |
| `Assets/TimeTheft/Table/Prefabs/TimeTheft_Table_T3.prefab` | 기존 메시·UV·텍스처를 보존하는 T3 테이블. |
| `Assets/TimeTheft/Counter/IntegratedTimeCounter.prefab` | 기존 Bank·Player·Opponent 통합 계수기. 메인 인스턴스의 TMP 세 개만 현행 View에 바인딩한다. |
| `Assets/TimeTheft/Tests/` | 현행 규칙·아이템·메인 통합·공통 카메라·Study 자동 검사. 구형 게임 회귀 검사는 제거했다. 아래 검증 정책에 따라 범위를 선택한다. |
| `Assets/TimeTheft/Tests/PlayMode/GameUiTestInput.cs` | 현행 통합 검사의 자동 UI 입력 도우미. Legacy 진행 코드에 의존하지 않는다. |
| `.agents/skills/commit/` | 명시적으로 요청받은 커밋 작업 지침. |

### 씬·표시·아트 보호 기준

- 현행 게임은 5개 시계 기반 `TimeTheftGame` 하나다. 개별 기능 검증은 자동 테스트를 우선한다. 새 씬이나 중복 진행 시스템을 만들지 않는다. 이전 3개 시계·유지/교환 게임은 개발 대상이 아니며 전용 씬·진행·테스트를 제거했다.
- 메인 씬의 `TimeTheftGame` 루트는 새 Controller와 View만 사용한다. `ClockSlots`는 검증용 임시 시계 5개, `Action UI`는 징수·배급, `Turn Feedback`은 수치 변화 표시를 담당한다.
- 메인 씬에 남겨 둔 과거 배분 기준점과 숨긴 피드백 도형은 보존용 Transform/표시 자료다. 구형 Controller·View·DecisionUI·RoundFeedback 컴포넌트는 메인에 연결하지 않는다.
- 새 묶음의 숫자·종류는 설정의 `Preview Duration`(기본 5.5초) 동안 자동 공개한 뒤 닫고 섞는다. 내 차례에는 닫힌 시계를 먼저 클릭해 선택하고, 숫자·종류를 숨긴 상태로 징수/배급을 결정하면 해당 시계가 자동 공개·정산된다. 선택된 `Visual`만 살짝 상승하며 Collider·FocusPoint는 유지한다. AI도 같은 순서로 판단하고 마지막 두 시계에도 직접 선택을 적용한다. 세부 정산 규칙은 Notion을 따른다.
- 공개 `TimeTheftMatch.GetClock`의 `Face`는 비공개·이미 치운 시계·폐기 상태에서 비어 있다. 현재 행동 결과 관찰 중인 시계만 공개를 유지한다. 여벌 열쇠의 `GetPrivateClock`은 확인한 참가자의 지식만 반환하며 플레이어용 임시 문구에만 표시한다. AI의 확인값은 플레이어 View에 전달하지 않는다. AI에는 공개 집계/정렬된 남은 구성·자기 아이템/효과/열쇠 지식·합법 후보 ID·공개 상대 보유 수량/종류만 전달한다. Match·상대 ID/비공개 효과/열쇠 지식·비공개 위치 배열·게임 시드를 주지 않는다. 화면에 공개 구성/결과의 자동 기록 목록을 만들지 않는다.
- 실행 중 수명·Bank·이동량은 분 단위 `int`이며 공개 API 이름에 `Minutes`를 붙인다. 설정의 `InitialLifeHours`와 시계 `Hours`는 시간 단위로 보존한다. 통합 계수기는 누적 `HH:MM`과 고유 색상을 유지하며 24시간마다 순환하지 않는다. 실제 정산과 표시 보간 모두 총량을 보존하고, 모델 계산에는 부동소수점 반올림을 쓰지 않는다.
- 기존 테이블·통합 계수기 외형, 카메라·시차·FocusPoint, Blender 원본·메시·UV·텍스처 및 제작 중인 회중시계 Prefab·개폐 애니메이션은 보호한다. 최종 시계 크기·5개 배치는 모델 완성 뒤 검토한다.
- 시계는 모델에서 관리하는 숫자·종류·가시성·사용 상태를 표현하는 View다. 최종 모델을 연결할 때 규칙 모델을 3D Prefab에 종속시키지 않는다.
- `ItemSlots`는 비활성 배치 샘플이며 아이템 보유량/슬롯 수 규칙이 아니다. 임시 인벤토리는 기존 Action UI에 실행 중 생성하는 텍스트로 표시한다. AI 추가 튜닝·최종 아이템 모델·메모·상대 캐릭터·최종 개폐/셔플/폐기·사운드·VFX는 별도 후속 작업이다.
- 공통 조작은 H 도움말, 우클릭/Esc Focus 복귀, Space 현재 연출 단축, R 재시작이다. 도움말은 일시 정지가 아니다.

## 개발 원칙

- KISS와 YAGNI를 따른다. 현재 필요한 기능을 가장 단순한 방법으로 구현한다.
- 핵심 플레이 루프와 게임의 완성도를 우선한다.
- 불필요한 추상화, 범용 시스템, 과도한 설계를 피한다.
- 기존 아키텍처와 주변 코드의 스타일을 존중한다.
- 수정 범위를 요청에 필요한 부분으로 최소화하고, 관계없는 리팩터링을 하지 않는다.
- 요청하지 않은 기능을 임의로 추가하지 않는다.
- 유지보수성을 고려하되, 7일 프로젝트에서 이해하고 수정하기 쉬운 단순한 구현을 선택한다.

## AI 작업 규칙

1. 작업 전에 관련 코드와 기획 문서를 읽고 현재 구현을 파악한다. 게임 규칙이나 시스템 구현 전에는 [Notion 기획서](https://app.notion.com/p/401b3e6e776c8034a3bfdf083f95c217)를 먼저 확인한다.
2. 한 번에 하나의 작업에 집중하고, 명확하게 요청된 작업은 조사·구현·검증까지 이어서 완료한다.
3. 불명확한 요구사항과 미확정 기획을 임의로 확정하지 않는다. 동작이나 범위에 영향을 주는 불확실성, 요청 범위 밖의 변경, 중요한 설계 판단은 먼저 사용자에게 확인한다.
4. 변경 전에 관련 기능과 의존성을 확인하고 기존 동작과 충돌하지 않도록 한다.
5. 설명이나 검토만 요청받았다면 코드를 변경하지 않는다.
6. 변경에 맞는 범위에서 결과를 검증한다. 코드 변경은 컴파일 오류와 관련 동작·테스트를 확인하고, 문서 변경은 내용·경로·변경 범위를 확인한다. 확인하지 못한 결과를 검증 완료로 보고하지 않는다.
7. 완료 후 변경 사항과 검증 결과, 남은 제약이 있다면 그 내용을 간결하게 보고한다.
8. 불필요한 장문 보고와 반복적인 중간 승인을 피한다. 이미 승인된 범위 안의 작업은 재승인 없이 진행한다.

## 자동 테스트와 검증 정책

- 기존 Unity Test Framework와 두 테스트 어셈블리를 사용한다. 별도 테스트 프레임워크나 관리 체계를 도입하지 않는다. 테스트 수 유지보다 현행 요구사항에 유효한 검사를 우선한다.
- 개별 기능은 컴파일 확인 → 관련 EditMode → 필요한 PlayMode와 관련 회귀 검사 순서로 검증한다. 순수 규칙·아이템 효과는 EditMode를 우선하고, Controller·입력·코루틴·UI 연결은 필요한 메인 PlayMode로 확인한다. 문서만 바꾸면 내용·경로·diff를 확인한다.
- Unity MCP `run_tests`의 `mode`와 `group_names`/`test_names`로 범위를 선택한다. 예: 아이템 작업은 `TimeTheft.Tests.ScrewdriverRulesTests`, `TimeTheft.Tests.TimeTheftItemFoundationTests` 및 영향받는 규칙 검사, 메인 연결은 `TimeTheft.Tests.TimeTheftGameIntegrationTests`. 카메라 변경에는 공통 Look/Focus 검사를 포함한다.
- 통합 마일스톤은 관련 자동 검사 전체와 실제 `TimeTheftGame` 씬의 자동 통합 검사를 실행한다. 전체 실행은 EditMode/PlayMode 각 어셈블리를 대상으로 하며 Legacy 게임을 열지 않는다. Study 자동 검사는 공통 카메라·제작 자산 검증이지 구형 게임 플레이 보장이 아니다.
- 자동화된 포인터·키 입력 검사는 수동 플레이와 구분한다. 기능마다 메인 씬을 열어 직접 클릭·조작하는 수동 플레이를 반복하지 않는다. Unity MCP와 Computer Use도 반복적인 수동 게임 조작의 기본 절차로 사용하지 않는다.
- 6종 아이템·기본 지급·AI 통합 뒤 수동 플레이를 집중 수행한다. 드라이버 수동 검증도 이 시점으로 이월한다. 별도 요청 또는 자동 검사로 확인할 수 없는 중요한 문제가 있을 때만 예외로 하며, 먼저 원인과 필요한 수동 검증 범위를 보고한다. 자동 테스트와 필수 검증을 생략하는 근거로 삼지 않는다.
- 테스트 전 열린 씬·Play·Prefab Stage·미저장 변경·기존 Git 변경과 Editor 설정을 확인한다. 미저장 사용자 작업을 잃을 수 있는 씬 전환이나 Play 진입은 수행하지 않는다. 테스트 설정은 실행용 복제본을 사용한다.
- 종료 시 불필요한 Play를 종료하고 원래 씬·에디터 설정·입력 상태를 가능한 한 복구한다. 기본 개발 씬은 `TimeTheftGame`이다. 테스트가 만든 폰트 캐시·설정·초기화 씬만 식별해 정리하며, 작업 시작 시 있던 아트·폰트·미커밋 변경을 HEAD로 복원하거나 저장으로 덮어쓰지 않는다.
- 결과에는 실행 범위·통과/실패/미실행·수동 검증 이월을 구분해 기록한다. Editor 자동 검사 통과를 독립 Player 빌드나 최종 수동 검증 완료로 보고하지 않는다.

## Unity 작업 규칙

- Unity Editor 상태를 읽거나 수정해야 하면 Unity MCP를 우선 사용한다. 일반 C# 코드처럼 파일 편집으로 충분한 작업은 직접 수정한다.
- Scene, GameObject, Component, Prefab, Inspector 변경은 가능한 경우 Unity MCP로 수행한다. `.unity`와 `.prefab` 직렬화 파일은 직접 수정하지 않는다.
- 여러 Editor가 연결되어 있으면 프로젝트 경로가 현재 저장소와 일치하는 인스턴스를 확인하고 활성 대상으로 고정한다. 다른 프로젝트의 인스턴스에서 임의로 작업하지 않는다.
- Unity MCP가 필요한데 MCP for Unity(Coplay)가 설치되어 있지 않다면 `Packages/manifest.json`을 통해 설치를 시도한다. 설치나 연결을 사용할 수 없으면 다른 방식으로 임의 우회하지 말고 사용자에게 알린다.
- 이미 구성된 stdio MCP를 사용한다. 별도의 HTTP MCP 서버를 임의로 실행하거나 종료하지 않는다.
- Unity 에셋과 해당 `.meta`의 연결을 보존한다. `Library/`, `Temp/` 등 자동 생성 파일을 소스처럼 수정하지 않는다.

## 문서 관리와 커밋

- 게임 기획과 규칙의 원본이자 기준 문서는 [Notion 기획서](https://app.notion.com/p/401b3e6e776c8034a3bfdf083f95c217)다. 세부 게임 규칙, 아이템, 밸런스와 기획 변경 사항은 Notion에서 관리한다.
- [GAME_DESIGN.md](GAME_DESIGN.md)는 Notion 기획서의 위치를 안내하는 문서다. 기획 내용을 이 문서나 `GAME_DESIGN.md`에 중복 작성하지 않는다.
- Notion에 접근할 수 없으면 이를 사용자에게 알리고, 기존 로컬 기획이나 기억을 최신 정보로 가정하지 않는다.
- 확정되지 않은 기획은 확정된 요구사항으로 취급하거나 임의로 구현하지 않는다.
- 코드와 Notion 기획서가 충돌하면 차이를 보고하고, 임의로 기획을 변경하지 않는다.
- AI 작업 원칙과 프로젝트 공통 지침은 `AGENTS.md`에서 관리한다.
- 별도 요청 없이 문서를 세분화하거나 관리 도구·자동화 체계를 추가하지 않는다.
- 커밋은 명시적으로 요청받았을 때만 수행하며, [커밋 스킬](.agents/skills/commit/SKILL.md)을 따른다. 커밋 전 대상 파일, 메시지, 검증 결과, 현재 브랜치를 간략히 보고한다.
