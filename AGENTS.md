# TimeTheft AI 개발 지침

## 프로젝트 기본 정보

- 프로젝트명: TimeTheft
- 개발 형태: 7일 게임잼, 1인 개발
- 핵심 목표: 제한된 기간 안에 핵심 플레이 루프를 완성하고 완성도 있는 게임을 제작한다.
- 사용자와의 소통 및 프로젝트 문서는 한국어로 작성한다.

## 기술 스택과 현재 구조

- 엔진: Unity 6, `6000.0.68f1` ([버전 정보](ProjectSettings/ProjectVersion.txt))
- 언어: C#
- 주요 패키지: URP `17.0.4`, Input System `1.18.0`, Unity UI(uGUI) `2.0.0`, Unity Test Framework `1.6.0` ([패키지 목록](Packages/manifest.json))
- 기본 게임 루프를 검증하는 uGUI 프로토타입과 2.5D 플레이 씬이 구현되어 있다. 두 화면 모두 순수 C# `TimeTheftMatch`와 `PrototypeSettings`를 재사용한다. 기존 화면은 `PrototypeController`·`TableView`·`ClockView`, 월드 화면은 `TabletopGameController`·`TabletopGameView`·`TabletopClockView`가 담당한다. 아이템 기능은 아직 없다.

| 경로 | 현재 역할 |
| --- | --- |
| `Assets/Scenes/SampleScene.unity` | 빌드 진입 씬. 카메라·조명과 프로토타입의 고정 UI·EventSystem·시계 배치 기준점을 씬에서 관리한다. |
| `Assets/Scenes/TabletopStudy.unity` | 2.5D 공간·카메라 검증용 별도 씬. 게임 로직과 연결하지 않았으며 빌드 씬 목록에도 추가하지 않았다. |
| `Assets/Scenes/TabletopGame.unity` | 2.5D 오브젝트를 클릭하여 실제 게임을 진행하는 플레이 씬. Editor에서 이 씬을 열고 Play한다. 기존 빌드 진입 씬은 유지한다. |
| `Assets/TimeTheft/Scripts/` | 게임 규칙, 진행, 화면 표현과 설정 코드를 보관한다. |
| `Assets/TimeTheft/Prefabs/Clock.prefab` | 교체 가능한 시계 표시 프리팹. 실행 시 `TableView`가 3개를 생성하고 라운드·새 게임에서 재사용한다. |
| `Assets/TimeTheft/Prefabs/TabletopClock.prefab` | 월드 시계·임시 개폐 뚜껑·공개 바늘·소유 표시·봉인 패/회전 교환 장치. 플레이 씬에 3개를 미리 배치하고 재사용한다. |
| `Assets/TimeTheft/PrototypeSettings.asset` | 기존 규칙의 임시 수치와 연출 시간, 재현용 난수 시드를 보관한다. |
| `Assets/TimeTheft/Tests/` | 규칙 EditMode 테스트와 UI 레이캐스트·게임 진행·배치 기준점 PlayMode 테스트를 보관한다. |
| `Assets/TimeTheft/Art/Placeholders/` | 테스트 씬용 단순 도형 스프라이트·머티리얼. PPU 128, Point 필터, 밉맵 없음은 임시 설정이다. |
| `Assets/Settings/` | PC·모바일용 URP 설정과 볼륨 프로파일을 보관한다. 대상 플랫폼이 확정되었다는 뜻은 아니다. |
| `Assets/InputSystem_Actions.inputactions` | 기본 입력 액션 에셋이다. |
| `Assets/TutorialInfo/` | 템플릿 안내용 에셋과 C# 스크립트를 보관한다. |
| `Packages/` | 패키지 의존성과 잠금 파일을 관리한다. |
| `ProjectSettings/` | Unity 프로젝트 설정을 관리한다. |
| `.agents/skills/commit/` | 프로젝트의 커밋 작업 지침을 보관한다. |

구조와 버전이 바뀌면 실제 파일을 기준으로 이 내용을 갱신한다. 아직 없는 게임 아키텍처를 전제로 작업하지 않는다.

고정 화면은 `SampleScene`의 `TimeTheft Prototype/TimeTheftCanvas/Table`에서 편집한다. `Slots` 아래의 `DealSlot0~2`, `PlayerSlot`, `AiSlot`, `DiscardSlot`은 시계 이동의 기준점이며, 시계는 같은 테이블의 `ClockRoot` 아래에 생성된다. `TableView`의 Inspector 참조를 유지하고, 고정 UI를 런타임에 다시 생성하지 않는다. 기존 uGUI 프로토타입은 보존하고 월드 UX는 별도 `TabletopGame` 씬에서 작업한다.

2.5D 배치는 `TabletopStudy`에서 편집한다. `Environment/Table`, `Opponent`, `ClockSlots`, `Devices`, `ItemSlots`는 모두 씬에 직접 배치된 임시 표시이며 런타임에 생성하거나 게임 수치를 갱신하지 않는다. 시계는 테이블에 눕혀 배치하고, 계수기는 가장자리에 브래킷으로 고정한 케이스, 왼쪽 시간 보관 장치는 안쪽을 향한 독립 캐비닛으로 구성한다. 장치의 `Face` 아래 스프라이트·정적 텍스트는 가독성 검증용이며 게임 데이터와 연결하지 않았다. 기본 구도는 `CameraRig` Transform과 자식 Camera의 FOV에서, 마우스 추적은 `TabletopCameraLook`의 Strength·Horizontal/Vertical Degrees·Horizontal/Vertical Travel·Response에서 조절한다. 기본값은 16:9, Perspective FOV 58°, 하향 34°이며 좌우 ±7°·상하 ±4.5° 회전과 CameraRig 로컬 XY 기준 좌우 ±0.22·상하 ±0.12 단위 이동을 함께 보간한다(Response 6). 시계와 장치는 고정 월드 방향을 유지하며 카메라를 따라 회전하지 않는다. SortingGroup으로 각 오브젝트 내부 표시 순서를 묶었다.

테스트 씬의 `TabletopFocusCamera`는 호버 색상 강조와 클릭 집중, 우클릭·Esc 복귀만 담당한다. `TabletopFocusTarget`은 시계 3개, 왼쪽 보관 장치, `PlayerItemSlot2`의 임시 물건에 연결되어 있다. 각 대상 자식 `FocusPoint`의 위치·회전(파란 Z축이 시선), 대상 컴포넌트의 Transition Duration·Override Field Of View·Field Of View와 카메라 컴포넌트의 Return Duration을 Inspector에서 편집한다. 집중·복귀 중에는 기본 마우스 추적을 중단하고, 복귀가 끝난 뒤 기존 기준 구도에서 재개한다. 모든 장치를 기본 시점에서 읽도록 강제하지 않으며, 측면 장치는 집중 시점에서 가독성을 확인한다. 픽셀 해상도·PPU 확정과 실제 UX·게임 로직 연결은 후속 단계다.

`TabletopGame`에서는 시계를 한 번 클릭하여 선택하고, 해당 시계 옆의 유지/교환 조작부를 클릭하여 기존 행동을 확정한다. `TabletopGame` 루트의 View Inspector에서 시계 3개·카메라·수명 계수기·판돈 보관 장치·작은 라운드 표시·도움말·종료 결과 전표 참조를 관리한다. 수치는 월드 TextMeshPro로 표시하며 일반 HUD Canvas는 사용하지 않는다. 선택 가능·소유·공개 상태는 시계 색상과 짧은 소유 표시로 구분하고 폐기 시계는 숨긴다. 게임 값은 `TimeTheftMatch`만 소유하며 View는 공개 시점에 전달받은 값만 표시한다. 카메라 전환 완료가 규칙 진행 조건은 아니며 전환 중 마우스 입력만 제한한다. 집중 중 월드 조작부 입력은 플레이 씬의 Allow Input While Focused에서 허용한다. 시계 선택·유지/교환·다시하기 대상의 Focus On Click은 끄고 Clicked 이벤트로 연결하며, 장치 열람은 기존 집중 동작을 사용한다. 큰 중앙 안내판과 상시 새 게임 조작부는 없다. 최초 선택 전에는 작은 조작 안내 카드가 보이고 선택하면 닫히며 H로 다시 열거나 닫는다. 라운드 번호는 Time Bank의 작은 표시로, 진행 단계는 선택 강조·선택 물건의 등장·시계 공개·계수기 변화로 전달한다. 우클릭/Esc로 전체 시점 복귀, Space로 진행 대기 단축, R로 새 게임을 시작한다. 최종 판돈 지급이 완료되면 결과 전표와 다시하기 봉인 패만 표시한다.

플레이 씬의 `ItemSlots`는 기본 비활성화된 배치 검증 영역이다. Inspector에서 켜면 `PlayerItems`와 `OpponentItems` 아래 각 배치 기준점의 `LayoutSample`을 비교할 수 있다. 현재 양측 3개씩의 서로 다른 임시 실루엣을 배치했으나 보유 수량 규칙은 아니다. 원형 용기·주머니·열쇠형 도구·병 거치대·실패·꺾인 도구는 기본 도형과 평면 스프라이트로 구성하며 크기·회전·접지 그림자를 직접 편집한다. 슬롯 테두리와 샘플 입력·Collider는 없으며 게임 효과는 없다. 빈 위치는 테이블 표면으로 남는다. `TabletopStudy`의 공간 검증 환경은 별도로 보존한다.

유지/교환 입력은 기존 Clicked 이벤트와 Match.CommitAction을 재사용한다. 시계 프리팹의 각 `DecisionControls/Keep`·`Swap`에는 봉인 패와 회전 화살표 장치를 배치한다. `TabletopDecisionObject`는 아트의 출입·호버 상승·누름/회전만 담당하고 게임 규칙은 알지 못한다. 결정 즉시 Collider와 입력을 막고 짧게 퇴장하며, 다음 라운드·재시작 시 아트와 장치 자세를 복구한다. 배치와 반응 강도·시간은 Inspector에서 편집한다.

시계의 `Visual` 자식만 호버·선택 상승과 짧은 행동 반응을 수행한다. 선택 물건의 `DecisionControls`는 `Visual` 밖에 두어 시계 상승에 끌려가지 않고 테이블 위에 놓인다. `FocusPoint`와 시계 선택 Collider는 루트에 남겨 미세 연출이 집중 시점을 흔들지 않게 한다. `TabletopClockView`의 Hover/Selected Lift·Motion Response·Reveal/Action Duration·Lid Open Degrees·Pulse Scale은 Inspector에서 조절한다. 숫자와 바늘은 기존 공개 호출에서만 활성화하고 임시 뚜껑 개폐를 덧붙인다. 실제 교환은 기존 코루틴에서 View.ExchangeFrame을 호출해 두 물리 시계의 루트 위치를 교체하며, 물리 ID와 게임의 소유 인덱스를 구별한다. View의 Exchange Arc·Exchange Lift로 궤적을 조절한다. 다음 분배·재시작에는 처음 배치로 복구한다.

정산과 판돈 지급은 Match에서 즉시 계산하고, 같은 진행 코루틴이 View.NumbersFrame으로 계수기를 보간한다. 표시 중에도 수명+판돈 총량을 보존하며 완료·스킵 시 정확한 결과값을 적용한다. 숫자 크기·장치 테두리 밝기는 Number Pulse Scale·Indicator Brightness에서 조절한다. 단계별 시간은 기존 PrototypeSettings의 Move/Reveal/Settlement Duration·Round Pause를 사용하며 새 패키지는 없다. 실제 픽셀아트·사운드·최종 조명은 후속 단계다.

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
