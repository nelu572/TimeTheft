# TimeTheft 개발 중단 인수인계 — 4.5단계

분석 기준일: **2026-10-22 (한국 시간)**. 기준 작업 폴더: `C:\unity\TimeTheft`.

이 문서는 저장된 코드·씬·프리팹·테스트, Unity MCP의 읽기 조회, Notion 기획서 조회를 근거로 현재 구현을 기록한다. **기획 확정서나 추가 구현 지시가 아니다.** 개발은 일시 중단하며, 이후 사용자의 명시적인 재개 요청부터 작업한다.

## 1. 현재 현황과 분석 범위

- Unity `6000.0.68f1`, C#, URP `17.0.4`, Input System `1.18.0`, uGUI `2.0.0`, Unity Test Framework `1.6.0`이다. 근거: [ProjectVersion](ProjectSettings/ProjectVersion.txt), [manifest](Packages/manifest.json).
- 동일한 규칙 모델을 쓰는 **uGUI 프로토타입과 2.5D 월드 플레이 화면**이 있다. 2.5D 공간만 시험하는 별도 씬도 보존되어 있다.
- 4.5단계까지의 게임 진행·월드 클릭·선택 장치·공개·교환·계수기 변화·결과·재시작 코드가 존재한다. 이 사실과 **처음 접한 사람이 직관적으로 이해하는지**는 별개의 검증 항목이다.
- 이번 분석에서는 코드·씬·프리팹·설정·테스트·`AGENTS.md`를 수정하지 않고 이 Markdown만 작성한다. Play 진입, 새로운 테스트 실행, 빌드, 씬 저장, Notion 수정은 수행하지 않는다.
- Unity MCP로 `TimeTheft@a56a316c` / `C:/unity/TimeTheft/Assets`를 확인했다. 조회 당시 `TabletopGame`이 열린 편집 모드이고 씬은 저장된 상태였다. 다른 연결 프로젝트 `Slash`에는 작업하지 않았다. 인스턴스 ID는 다음 세션에서 다시 확인해야 한다.
- 기존 문서 폴더가 없고 루트에 [AGENTS.md](AGENTS.md), [GAME_DESIGN.md](GAME_DESIGN.md)가 있으므로 인수인계도 루트 한 파일로 둔다.

### 작업본 보존 주의

분석 시작 시 브랜치는 `main`, HEAD는 `db56e3a` (`docs: 게임 기획 기준을 Notion으로 전환`)였다. `AGENTS.md`와 `SampleScene.unity`는 기존 수정 상태이며, **`Assets/TimeTheft/` 전체와 `TabletopGame`·`TabletopStudy` 씬, TextMesh Pro 리소스 등은 Git 미추적 상태**였다.

따라서 HEAD나 새 clone만으로 현재 게임을 복원할 수 있다고 가정하면 안 된다. 새 대화는 이 작업 폴더의 미커밋·미추적 파일까지 포함한 상태에서 시작해야 한다. 임의의 reset/clean/되돌리기를 하지 않는다. 커밋·백업 작업은 이번 분석에서 수행하지 않았다.

## 2. 주요 파일, 씬과 연결 구조

### 2.1 씬과 진입점

| 파일 | 실제 역할과 실행 방식 |
| --- | --- |
| [SampleScene.unity](Assets/Scenes/SampleScene.unity) | `PrototypeController` + `TableView`. uGUI로 플레이 가능한 기존 프로토타입. **현재 빌드 설정의 유일한 활성 씬**이다. |
| [TabletopGame.unity](Assets/Scenes/TabletopGame.unity) | `TabletopGameController` + `TabletopGameView`. Editor에서 직접 열어 Play하는 2.5D 플레이 씬. 빌드 목록에는 없다. |
| [TabletopStudy.unity](Assets/Scenes/TabletopStudy.unity) | 마우스 시차·호버·집중 카메라와 정적 공간 시험. 게임 Controller가 없으므로 라운드나 수치 갱신이 일어나지 않는다. |
| [EditorBuildSettings.asset](ProjectSettings/EditorBuildSettings.asset) | 위 진입 씬 차이의 근거. 현재 빌드가 월드 UX로 시작한다고 가정하지 않는다. 씬 전환 메뉴도 현재 게임 코드에 없다. |

### 2.2 코드의 책임

아래 링크는 모두 현재 파일을 가리킨다. 이후 수정으로 행 번호가 달라질 수 있으므로 클래스·함수 이름을 기준으로 찾는다.

| 파일 / 클래스 | 책임 및 주요 함수 |
| --- | --- |
| [TimeTheftMatch.cs](Assets/TimeTheft/Scripts/TimeTheftMatch.cs) | Unity 비의존 규칙 모델. `BeginRound`, `SelectClock`, `CommitAction`, `ResolveExchange`, `Settle`, `FinishRound`가 상태와 수치를 소유한다. 같은 파일의 `PrototypePolicy`는 임시 생성·배분·손실·동시 탈락 정책, `ClockAi.Decide`는 AI 확률 판단이다. |
| [PrototypeSettings.cs](Assets/TimeTheft/Scripts/PrototypeSettings.cs), [PrototypeSettings.asset](Assets/TimeTheft/PrototypeSettings.asset) | `PrototypeRules`와 연출 시간·난수 시드. **두 플레이 화면이 같은 설정 에셋을 참조**한다. |
| [TabletopGameController.cs](Assets/TimeTheft/Scripts/TabletopGameController.cs) | 월드 화면 진행자. `Start`에서 View 콜백 연결 후 새 Match 생성. `Deal`·`Allocate`·`Resolve` 코루틴이 모델 호출 순서와 대기 시간을 결정한다. H/R/Space 입력, 재시작·중복 입력 차단도 담당한다. |
| [TabletopGameView.cs](Assets/TimeTheft/Scripts/TabletopGameView.cs) | Inspector 참조로 연결된 시계·계수기·도움말·결과 전표를 갱신한다. `ExchangeFrame`, `NumbersFrame`, `CancelPresentation`이 물리 이동·표시 보간·복구를 담당한다. 수명 계산이나 승패 판정을 새로 하지 않는다. |
| [TabletopClockView.cs](Assets/TimeTheft/Scripts/TabletopClockView.cs) | 시계 하나의 숫자·바늘·소유 색·선택 링·뚜껑·미세 움직임·선택 장치 노출. `Show`의 공개 인자에 따라 값 표시를 제한한다. 시계 선택과 유지/교환은 Controller 콜백으로 보낸다. |
| [TabletopDecisionObject.cs](Assets/TimeTheft/Scripts/TabletopDecisionObject.cs) | 선택 물건의 등장·퇴장·호버 상승·누름/회전만 처리. `SetVisible(false)`는 즉시 입력·Collider를 끄고 아트만 잠시 퇴장시킨다. 게임 규칙은 모른다. |
| [TabletopFocusTarget.cs](Assets/TimeTheft/Scripts/TabletopFocusTarget.cs) | Inspector의 `FocusPoint`, FOV, 전환 시간, 강조 대상, `InputEnabled`, `Clicked`, `FocusOnClick`을 제공한다. |
| [TabletopFocusCamera.cs](Assets/TimeTheft/Scripts/TabletopFocusCamera.cs) | Physics Raycast로 호버·클릭 대상을 찾고 카메라 위치·회전·FOV를 보간한다. `Focus`·`ReturnToOverview`와 우클릭/Esc 복귀. 게임 규칙은 참조하지 않는다. |
| [TabletopCameraLook.cs](Assets/TimeTheft/Scripts/TabletopCameraLook.cs) | 마우스 위치에 따른 작은 회전·위치 시차. 집중 중 `SetSuspended(true)`로 중지하고 복귀 후 재개한다. |
| [PrototypeController.cs](Assets/TimeTheft/Scripts/PrototypeController.cs), [TableView.cs](Assets/TimeTheft/Scripts/TableView.cs) | 기존 uGUI 플레이 흐름과 고정 UI. 규칙은 동일 Match를 재사용하지만 표시와 코루틴은 월드 화면과 별도 구현이다. |
| [ClockView.cs](Assets/TimeTheft/Scripts/ClockView.cs), [TableShape.cs](Assets/TimeTheft/Scripts/TableShape.cs) | 기존 uGUI 시계, Button/EventSystem 호버, 임시 시계·테이블 도형을 그리는 `MaskableGraphic`. 월드 시계와 혼동하지 않는다. |

연결은 `입력 → FocusTarget.Clicked → ClockView 콜백 → Controller → Match → View`이다. 자동 데이터 바인딩이나 범용 이벤트 버스가 있는 구조는 아니다. Controller는 View 연출 대기 뒤 다음 Match 메서드를 호출한다. 다만 **카메라 전환 완료 콜백을 게임 규칙 실행 조건으로 기다리지는 않는다.** 정산·배당은 모델에서 결과를 먼저 확정하고 표시만 뒤따라 보간한다.

분리가 완전한 무의존 계층이라는 뜻도 아니다. 월드 View는 Match를 인자로 받고, `TableView`의 정적 색상·`ActionName`을 공용으로 사용한다. 이 때문에 새 매니저나 전면 재작성이 필요하다고 해석하지 않는다.

### 2.3 오브젝트와 프리팹 관리

`TabletopGame`의 주요 Hierarchy는 다음과 같다. 하위 장식·지지대 등은 생략했다.

```text
Environment/Table
  Surface, Frame
  HelpCard/Instructions
  ResultReceipt/{Result, Award, ReplaySeal, FocusPoint}
Opponent/MannequinPlaceholder
ClockSlots/ClockSlot0~2/Clock0~2      ← TabletopClock.prefab 인스턴스
  Visual/{Case, Value, OwnerLabel, Hand, LidPivot, ...}
  DecisionControls/{Keep, Swap}/Artwork/Mechanism
  FocusPoint
Devices
  PlayerTimeCounter/Head/Face/StaticValue
  OpponentTimeCounter/Head/Face/StaticValue
  CentralTimeBank/Face/StaticValue
  CentralTimeBank/RoundCounter/RoundDigits
ItemSlots                           ← 기본 비활성, 배치 미리보기 전용
  PlayerItems/{CanisterAnchor, PouchAnchor, KeyToolAnchor}/LayoutSample
  OpponentItems/{TwinVialsAnchor, SpoolAnchor, AngleToolAnchor}/LayoutSample
CameraRig/Main Camera
Lighting
TabletopGame                        ← Controller + View
```

- [TabletopClock.prefab](Assets/TimeTheft/Prefabs/TabletopClock.prefab)은 월드 시계다. 3개를 씬에 미리 배치하며 런타임 새 시계 GameObject 생성 없이 재사용한다. 교환은 두 루트를 이동하고, 다음 분배·재시작은 초기 자세로 복구한다.
- `Visual`만 호버/선택 상승·뚜껑·짧은 행동 반응을 수행한다. `FocusPoint`, 본체 Collider와 `DecisionControls`는 그 밖에 있어 미세 아트 움직임에 직접 끌려가지 않는다. 각 시계가 **자기 유지/교환 물건 한 쌍**을 가지고, 자기 선택 단계에서만 활성화한다.
- [Clock.prefab](Assets/TimeTheft/Prefabs/Clock.prefab)은 uGUI용이다. `TableView.Initialize`가 초기화 때 3개만 Instantiate하고 라운드·새 게임에서 재사용한다. 고정 Canvas·EventSystem·슬롯은 SampleScene 소유다.
- 테이블·장치·마네킹·아이템 기준점은 씬에 배치된 Transform이다. 월드 게임 코드에는 이 배치를 런타임에 새로 만드는 생성기가 없다.

### 2.4 카메라와 입력

현재 저장된 플레이 씬에서 확인한 값:

| 항목 | 설정 |
| --- | --- |
| 기본 CameraRig | 위치 `(0, 4.25, -4.8)`, 회전 `(34, 0, 0)` |
| Camera | Perspective, FOV `58°` |
| 마우스 반응 | Strength `1`, 좌우 `±7°`, 상하 `±4.5°`, 로컬 XY 이동 `±0.22 / ±0.12`, Response `6` |
| 집중/복귀 | 시계·장치 전환 `0.7초`, 복귀 `0.7초`. 시계·계수기 FOV `42°`, Time Bank `48°` |
| 입력 허용 | 플레이 씬 `Allow Input While Focused = true`. **이동·복귀 도중에는 클릭 불가**, 안정된 기본/집중 상태에서만 월드 클릭 |
| 선택 검출 | 거리 30의 단일 3D Physics Raycast. 가장 먼저 맞은 Collider의 부모 `TabletopFocusTarget`을 검사한다. 기본 레이어 마스크는 전체이며 Trigger는 무시한다. |

`Mouse.current`와 `Keyboard.current`를 직접 읽는다. `Assets/InputSystem_Actions.inputactions`가 존재하지만 현재 게임 조작이 그 액션 에셋에 연결되어 있다고 가정하면 안 된다. 게임 연출·카메라·시계는 `Time.unscaledDeltaTime`을 사용한다. `Time.timeScale = 0`만으로 UX가 일시 정지하지 않는다.

| 입력 | 실제 처리 |
| --- | --- |
| 선택 단계 시계 좌클릭 | 한 번으로 시계 확정과 집중 시작. 별도 확인 버튼 없음 |
| 결정 단계 봉인 패/회전 장치 클릭 | Keep/Swap 즉시 확정. 추가 확인 없음 |
| 계수기/Time Bank 클릭 | 입력이 열린 선택·결정·종료 상태에서 해당 FocusPoint 열람 |
| 우클릭 / Esc | 집중 중 전체 시점 복귀. 일시 정지 메뉴나 게임 종료 메뉴가 아님 |
| H | 도움말 토글. 열면 전체 시점 복귀. 진행 코루틴은 계속 동작 |
| Space | 현재 연출/대기를 단축. 선택·결정 자체를 대신하지 않는다. 매 구간 플래그가 초기화되며 누르고 있기만 해서 모두 연속 스킵되지는 않음 |
| R | 단계와 무관하게 진행 코루틴 중단 후 새 Match. 확인 창 없음 |
| 종료 후 다시하기 봉인 패 | 결과 표시가 열린 뒤에만 Restart 콜백 허용 |

## 3. 현재 코드 규칙과 기획 원본의 차이

기획 원본은 [Notion — TimeTheft 기획서](https://app.notion.com/p/401b3e6e776c8034a3bfdf083f95c217)다. 이번에 연결 도구로 읽었으며, 반환된 최종 편집 시각은 `2026-10-22T02:28:18.564Z`였다. 아래는 기획을 대체한 사본이 아니라 **현재 코드와의 대조 기록**이다. 기획서 자체는 수정하지 않았다.

### 3.1 코드로 결정되어 있는 동작

근거: `TimeTheftMatch`, `PrototypePolicy`, `ClockAi`, 실제 `PrototypeSettings.asset`.

1. 초기 수명은 양측 30h, 판돈은 0h. 매 라운드 시계 3개 값을 각각 1~12의 정수로 선생성한다. 독립 추첨이라 같은 값이 중복될 수 있다.
2. 플레이어가 먼저 인덱스 하나를 선택한다. 그 호출에서 AI가 남은 두 인덱스 중 하나를 무작위로 가져간다. 남은 한 개는 값 비공개로 폐기한다.
3. 자기 시계만 먼저 보여준다. 결정 대기는 시간 제한이 없다.
4. `CommitAction` 호출 때 플레이어 행동 저장 직후 AI 행동도 생성한다. AI가 별도 코루틴에서 미리 생각하거나 타이머 뒤에 선택하는 구조는 아니다. `상대 선택 중`은 현재 표현 문구다.
5. AI는 자기 값·규칙·난수만 받는다. 플레이어 행동이나 플레이어/폐기 시계 값을 판단 자료로 받지 않는다. 자기 값 1일 때 교환 확률 0.18, 12일 때 0.82이며 중간 값은 선형 보간한다. 따라서 호출 시점은 후순위지만 플레이어 행동을 보고 결정하는 알고리즘은 아니다.
6. 두 행동이 다를 때만 소유 **인덱스**를 교환한다. 시계 값 배열 자체를 바꾸는 것이 아니다.
7. 각자 최종 시계 값만큼 **자기** 수명을 잃되 `ActualLoss = min(현재 수명, 시계 값)`이다. 실제 차감량 합계를 판돈에 더한다. 별도 베팅액 선택이나 실시간 수명 감소는 없다.
8. `Settle`에서 차감·판돈·승자를 먼저 결정한다. `FinishRound`에서 다음 라운드/종료로 전환하고, 단독 생존자에게 판돈 전액을 더해 Pot=0으로 만든다.
9. 동시 0h는 Draw, Award=0이고 판돈을 남긴다. 현재 30h씩 시작하므로 총량은 60h이며, 단독 승리 후에는 승자에게 60h가 모인다. 이는 현재 총량 보존 규칙의 결과이고 영구 재화 보상 시스템은 아니다.
10. 새 게임은 30h/30h/0h로 새로 시작한다. 보상·라운드·아이템의 다음 게임 이월은 없다. Seed=0은 새 난수, 0이 아닌 고정 Seed는 같은 입력 순서의 재현용이다.

### 3.2 미확정 기획을 코드가 임시 정책으로 처리한 부분

| 항목 | 기획서 상태 | 현재 구현 | 인수인계 판단 |
| --- | --- | --- | --- |
| 기본 3개, 비공개 자기 확인, 한쪽만 교환, 자기 최종값 차감, 승자 판돈 독식 | 우선 채택안 | 해당 기본 구조 구현 | 코드상 구현 완료. 밸런스/직관성 확정과는 별개 |
| 값 생성 시점·분포 | 선택 전/태엽 시점, 생성 분포 미정 | 라운드 시작 때 독립 균등 정수 선생성 | 임시 구현. 최종 기획으로 승격하지 말 것 |
| 선택 동시/순차 | 미정 | 플레이어 먼저, 즉시 AI 잔여 인덱스 추첨 | 임시 구현 |
| 세 번째 시계 공개 | 미정 | 끝까지 비공개, 배분 후 숨김 | 임시 구현 |
| 초과 손실 | 미정 | 남은 수명까지만 차감 | 임시 구현. 시계 9·수명 3이면 실제 손실 3h |
| 동시 탈락 | 미정 | Draw 종료, 판돈 잔류·획득자 없음 | 임시 구현. 명시적 기획 승인 여부는 이 코드만으로 증명할 수 없음 |
| AI 전략·초기 수명·라운드 길이 | 조정 필요/30h 임시 | 확률 AI·30h·고정 연출 시간 | 임시 수치/정책 |
| 아이템 종류·개수·사용 시기 | 모두 미확정/구상 단계 | 배치 샘플만 존재 | 기획상 미확정 + 실제 시스템 미구현 |
| 보상·공개·정산의 최종 표현 | 미정 | 숫자 보간·임시 뚜껑·결과 전표 | 임시 연출/UX. 최종 아트 방향의 확정 근거로 삼지 않음 |

`PrototypePolicy`라는 이름과 파일 주석도 일부가 임시 정책임을 드러낸다. 위 미정 항목을 오류라고 단정해 바꾸거나, 이미 최종 합의된 규칙으로 간주하면 안 된다.

## 4. 실제 게임 루프: 상태·입력·공개·전환

분석 대상은 `TabletopGameController`와 `TabletopGameView`의 실제 정상 경로다. `RoundStage`에는 별도의 `FinalReveal`·`Payout`·`AwaitResultConfirmation`이 없다. 따라서 enum 이름 하나만으로 화면의 세부 단계를 판단하면 안 된다.

### 4.1 시작부터 종료까지

| 단계 / 진입 함수 | 모델과 입력 | 화면·공개 정보 | 다음 조건 |
| --- | --- | --- | --- |
| 시작 / `Start → RestartGame` | View 콜백 연결, 새 Match. 초기 Stage=`RoundComplete` | 카메라 복귀 요청, 시계·숫자 복구, 결과 숨김, 아직 시계를 선택한 적이 없으면 도움말 표시 | 즉시 `Deal` 자동 호출 |
| 준비 / `Deal → BeginRound` | 값 3개 선생성, Round+1, 소유 인덱스 -1, 손실 0, `Dealing`. 시계·장치 클릭 잠금 | 세 시계 초기 위치·닫힌 뚜껑·`?`, 선택 물건 숨김, 작은 R 번호 갱신. 이 단계에 실제 새 시계 생성/분배 이동 연출은 없음 | MoveDuration 0.4초 뒤 `OpenSelection` |
| 시계 선택 / `Selecting` | 시계 좌클릭 대기. 장치 열람 가능 | 세 시계의 선택 링·호버 밝기·상승. 모든 값 비공개 | 유효한 시계 1회 클릭 필요. 무기한 대기 가능 |
| 배분 / `SelectClock → Allocate` | 플레이어 인덱스 확정, AI 인덱스 즉시 추첨, `Inspecting`. 중복 선택 거부 | 선택한 시계 집중 시작, 플레이어 청록/AI 적갈색 소유 표시, 폐기 시계 숨김, 도움말 닫힘. 아직 양측 값 비공개 | 0.4초 자동 대기 |
| 자기 정보 확인 / `RevealPlayer` | 여전히 `Inspecting`. 별도 확인 입력 없음 | 자기 숫자·바늘 활성화와 0.25초 뚜껑 개방 시작. 상대 숫자·바늘은 숨김 | RevealDuration×0.7 = 0.56초 후 `OpenDecision` |
| 결정 / `Deciding → OpenDecision` | 자기 Keep/Swap 장치만 입력 허용. 장치 열람 가능. AI 행동은 아직 새로 생성하지 않음 | 자기 값 유지, 상대 `?`와 `상대 선택 중`, 봉인 패·회전 장치 등장 | 물건 하나 클릭 필요. 별도 확정 클릭이나 제한 시간 없음 |
| 행동 공개 / `ChooseAction → CommitAction → RevealActions` | 양측 행동 확정, `Revealing`. 물건 입력·Collider 즉시 잠금. 추가 행동 거부 | 양측 OwnerLabel에 행동 동시 표시, 짧은 행동 반응, 물건 0.18초 퇴장, 전체 카메라 복귀. 상대 값은 아직 비공개 | RevealDuration 0.8초 자동 대기 |
| 교환 판정/이동 / `ResolveExchange → Exchange` | `Exchanging`. 다르면 소유 인덱스 즉시 교체; 같으면 그대로 | 교환 시 양측 숫자·바늘 숨김 및 뚜껑 닫힘, 소유 색·이동 문구 변경, 루트 0.6초 이동. 비교환은 제자리 0.4초 대기 | 해당 Animate 완료/단축 |
| 최종 값 공개 / `RevealFinal` | **여전히 `Exchanging`**. 새 입력 없음 | 최종 보유 두 시계의 값·바늘·소유 표시 공개. 폐기 값은 계속 비공개 | RevealDuration×0.65 = 0.52초 자동 대기 |
| 정산 / `Settle → View.Settle` | `Settling`. 실제 손실·수명·판돈·Winner 즉시 계산 | 화면 계수기만 기존값→계산값을 0.9초 보간. 숫자 크기·장치 테두리 밝기 반응. 일반 손실 설명 패널은 없음 | 보간 끝값 적용 후 RoundPause |
| 결과 관찰 대기 / `Pause(RoundPause)` | **여전히 `Settling`**. 결과 확인 입력은 요구하지 않음 | 차감 후 수명·판돈 및 공개 시계 유지 | 1.1초 자동 대기. Space로 단축 가능 |
| 다음 라운드 / `FinishRound → Deal` | 둘 다 생존이면 `RoundComplete`를 거쳐 같은 흐름에서 즉시 `Dealing` | 시계 초기 위치 복구·숫자 숨김·폐기 시계 재등장·R 번호 갱신 | 0.4초 뒤 새 `Selecting` |
| 종료/배당 / `FinishRound` | 탈락 시 `GameOver`. 생존자 배당은 모델에 즉시 적용 | 단독 승자는 추가 0.9초 계수기 보간 후 결과 표시. Draw는 배당 보간 없이 표시. 결과 전에는 다시하기 잠금 | `View.Result` 후 입력 대기 |
| 종료 화면 / `Result` | 새 라운드 자동 호출 없음 | 승패·보상 또는 잔류 판돈 전표와 다시하기. 마지막 두 시계 값도 남음 | 다시하기 패/R이면 새 Match. 자동 재시작 없음 |

상태 가드가 중복·순서 밖 입력을 거부한다. Controller에서도 비활성 상태의 공개 입력 메서드는 막는다. 재시작은 기존 코루틴을 중단하고 시계 자세·뚜껑·선택 물건·숫자 피드백을 복구한다. `OnDisable`은 진행을 중단하며 **중단 지점 재개 기능은 아니다**.

### 4.2 공개 → 정산 → 다음 라운드의 기본 타임라인

**행동 물건을 클릭하여 확정한 순간을 t=0**으로 한다. 설정 에셋 그대로, Space 없음, 프레임 경계 오차를 제외한 근사치다.

| 경과 시간: 교환 있음 / 없음 | 동작 |
| --- | --- |
| 0.00 / 0.00초 | 행동 공개. 카메라 0.7초 복귀 시작. 자기 값만 보임 |
| 0.80 / 0.80초 | 교환 판정. 교환 시 0.6초 이동, 비교환 시 0.4초 대기 |
| 1.40 / 1.20초 | 두 최종 값 공개 호출. 새로 공개하는 뚜껑 개방은 별도 0.25초 |
| 1.92 / 1.72초 | Match 정산 확정, 화면 숫자 0.9초 보간 시작 |
| 2.82 / 2.62초 | 계수기 정확한 정산값 도착. 1.1초 유지 |
| 3.92 / 3.72초 | `FinishRound`. 계속 경기라면 즉시 다음 준비와 값 숨김 |
| 4.32 / 4.12초 | 계속 경기의 다음 시계 선택 가능 |
| 4.82 / 4.62초 | 단독 승리였다면 배당 보간 후 최종 전표 표시. Draw는 3.92 / 3.72초 시점에 표시 |

중요한 해석:

- **다음 라운드가 있는 경우**, 최종값 공개 호출부터 숨김까지 약 2.52초다. 새 뚜껑 개방 0.25초도 그 안에 포함하므로 완전 개방 후에는 약 2.27초다. 게임 종료 시에는 이 값들이 사라지지 않는다.
- 행동 문구는 교환이 있으면 0.8초 뒤 이동 문구로, 비교환이면 1.2초 뒤 최종 소유 문구로 대체된다. 처음 0.7초는 카메라 복귀와 겹친다. 읽기 충분성은 별도 사용성 검증 대상이다.
- `NumbersFrame`은 플레이어/AI 표시 수명을 반올림하고 그 차이로 표시 판돈을 구하므로 중간에도 총량을 보존한다. 마지막 프레임은 정확한 모델 끝값을 적용한다.
- `Winner`는 정산 시작 시 이미 결정되지만, `GameOver`·배당은 `FinishRound`, 결과 전표는 배당 표시 완료 뒤다. `Match.Stage == GameOver`와 `View.IsResultVisible`은 같은 시점이 아니다.
- `Animate`는 SmoothStep과 비배율 시간을 쓰고 최소 duration 0.05초, 스킵 요청은 경과 0.06초 이후 조기 완료를 허용한다. 각 구간 끝값은 적용하되 읽기 시간까지 유지해 주는 기능은 아니다. 위 시간표는 스킵하면 짧아진다.

## 5. 다이제틱 UI의 실제 상태와 UX 검토

### 5.1 무엇이 실제 오브젝트로 표현되는가

현재 `TabletopGame`에는 Canvas가 없다. 다만 월드에 놓인 텍스트를 사용한다는 것과 텍스트 설명 의존이 없다는 것은 다르다.

| 요소 | 현재 역할·구현 | 임시/남은 의존 |
| --- | --- | --- |
| 회중시계 3개 | SpriteRenderer 다이얼·숫자·바늘·소유 색/짧은 문구. 링·호버 상승·선택 상승, 임시 뚜껑 회전. 플레이 데이터와 연결 | 실제 개폐 아트가 아닌 평면 임시 뚜껑. 누구의 최종 값인지 색과 텍스트를 함께 읽어야 함 |
| 유지 봉인 패 | 원형 금속 받침·봉인/잠금 기호·짧은 `유지`, 누름 반응 | 실제 아이템이나 소비 자원이 아닌 행동 입력 물건. 의미 인지 여부 미검증 |
| 교환 회전 장치 | 받침·양방향 화살표·돌출 손잡이·짧은 `교환`, 회전 반응 | 행동 선택의 물리적 표현. 같은 선택끼리는 교환하지 않는 규칙은 외형만으로 전부 설명하지 못함 |
| 두 시간 계수기 | 설치 케이스·지지대와 월드 TMP의 실제 수명 `h`. 변화 때 숫자 보간·테두리/크기 반응 | 정교한 기계 숫자 아트·소리 없음. 최종 숫자는 정확하지만 손실 원인의 이해도는 별도 |
| Time Bank | 측면 캐비닛의 실제 Pot. 손실 누적·배당 감소를 표시하고 집중 열람 가능 | 별도 입출금/보관/밀수 자원 시스템 아님. 코드상 역할은 중앙 판돈 하나 |
| 작은 라운드 표시 | Bank의 `RoundCounter/RoundDigits`, `R 01` 등 | 라운드 번호만 표시. 현재 단계/다음 행동을 직접 쓰지 않음 |
| 도움말 | 첫 시계 선택 전 표시, 선택하면 닫힘. H로 다시 접근 | 현재 5줄은 클릭·봉인/화살표·한쪽만 교환·단축키 안내다. 자기 최종값만큼 자기 수명 차감, 승자 판돈 획득 설명은 없음 |
| 결과/다시하기 | 종료 전표에 승패·배당 또는 Draw 잔류 판돈. 종료 후 다시하기 패 | 월드 TMP의 요약 문장에 의존. 일반 플레이에 상시 새 게임 버튼이나 중앙 `ControlPlaque`는 없음 |
| 아이템 영역 | 플레이어 근거리 3개·상대 원거리 3개 기준점, 서로 다른 실루엣과 접지 그림자 | `ItemSlots` 기본 비활성. 샘플 Collider/입력 없음. 개수는 배치 시험용이며 획득·사용 기능이 아님 |

스프라이트 방향은 월드에 고정되어 있고 카메라를 강제로 보는 Billboard 시스템은 없다. 기본 도형의 두께·지지대와 평면 표면 그래픽을 함께 쓴다. 임시 스프라이트는 PPU 128·Point·밉맵 없음이며, 최종 화면 해상도·PPU·픽셀 정합성을 확정한 설정은 아니다. 한글 월드 텍스트는 `Assets/TimeTheft/Fonts/NanumGothic SDF.asset`을 사용한다.

### 5.2 문제 분류와 확인할 질문

아래의 **코드 사실**과 **사용성 가설**을 구분한다. 이번 분석에서는 새 수동 플레이 테스트나 새 화면 촬영을 하지 않았으므로 가독성·직관성·속도감을 합격/불합격으로 확정하지 않는다.

| ID / 분류 | 확인한 사실과 우려 | 추후 확인할 내용 |
| --- | --- | --- |
| U1 — UX 설계 / 초회 플레이 검증 | 매번 큰 지시문이 없고 초기 선택은 링·호버·도움말에 의존한다. 도움말에는 차감·승리·판돈의 전체 목적 설명이 없다 | 처음 보는 사람이 클릭 방법뿐 아니라 무엇을 예측하고 왜 교환하는지 설명할 수 있는가? 필요한 최초 안내 범위를 결정할 것 |
| U2 — UX 설계 / 전환 속도 검증 | 최종값 약 2.52초, 숫자 도착 후 1.1초 뒤 자동 초기화. 결과 확인 입력이나 월드 화면의 지난 라운드 기록 없음 | 스킵하지 않은 초회 플레이에서 최종 소유·양측 손실·판돈 증가를 말할 수 있는가? 자동/확인 대기 방식을 확정할 것. 곧바로 확인 버튼을 추가하라는 뜻은 아님 |
| U3 — UX + 연출 검증 | 행동 문구 공개의 대부분이 카메라 복귀와 겹치며, 실제 교환일 때 0.8초 뒤 문구 교체 | 상대가 어떤 행동을 했고 왜 교환/비교환이 됐는지 읽히는가? 문구 지속·기호·카메라 타이밍 중 무엇을 보완할지 판단 |
| U4 — 상호작용 UX | 자기 시계는 배분 뒤 본체 `InputEnabled=false`. 결정 중 우클릭/Esc/H/장치 열람으로 집중을 벗어나도 본체 클릭으로 자기 시계에 재집중하는 경로가 없음. 선택 물건은 직접 확정만 함 | 결정 전 수치를 다시 크게 보고 싶은 요구가 있는가? 재열람과 선택 확정을 어떤 입력 의미로 구분할지 결정 |
| U5 — 입력 상태/피드백 검증 | 카메라 전환 중 월드 클릭은 잠금. 기본 선택 후 결정까지 0.96초라 집중 0.7초보다 길지만, Space 단축이나 장치 열람/복귀로 클릭 잠금과 결정 상태가 겹칠 수 있음 | 물건이 보이는데 클릭이 안 되는 짧은 구간이 혼란스러운가? 정상 잠금과 실제 입력 누락을 구분하여 재현 |
| U6 — UX 설계 | H는 진행을 멈추지 않는다. 도움말은 첫 선택 후 R 재시작에서도 자동 재표시하지 않으며 H로 열어야 한다. Esc도 정지 메뉴가 아님 | 연출 중 설명을 읽다가 진행을 놓치는가? 도움말 접근·일시 정지 정책을 결정 |
| U7 — 규칙 미확정 + 정보 전달 | 초과 손실은 수명까지만 차감. 화면 시계값과 실제 손실이 다를 수 있음. Draw 정책도 기획 미정 | 예: 시계 9·수명 3에서 3h 차감을 이해하는가? 이를 연출 부족으로만 덮기 전에 규칙 자체부터 확인 |
| U8 — 연출/실제 화면 검증 | 공개·교환·계수기 피드백은 단순 색·이동·보간이다. 비교환 때는 물리 이동 없이 대기한다 | 개폐·교환/비교환·선택과 확정·정산의 차이가 구별되는가? 소리/기호/미세 움직임으로 해결할 문제인지 확인 |
| U9 — 실제 플레이 테스트 | 평면 스프라이트 원근, 픽셀 떨림, 기울어진 숫자, 임시 물건 의미·크기·가림은 기하 테스트만으로 확정 불가 | 16:9 기본/최대 시차/시계 및 장치 집중에서 확인. 다른 화면비·실제 아트·색 구별 접근성도 후속 검증 |
| U10 — 재시작 UX | R은 결과를 읽는 중이든 연출 중이든 즉시 새 Match를 만든다. 종료 전표 후에는 자동 진행이 멈춤 | 실수 재시작 위험과 단축키 발견 가능성을 판단. 종료 후 입력 대기는 있으므로 일반 라운드 자동 전환과 구분 |

이번 조사에서 정상 경로의 계산 순서가 깨졌다고 확정할 근거는 확인하지 않았다. U2·U3·U4·U6은 현재 코드가 그렇게 동작한다는 **사실**이며, 사용자 의도와 사용성 평가에 따라 수정 필요성이 정해지는 UX 문제다. U7은 우선 기획 정책 문제다. 소리·미세 개폐·광량 등은 표현 문제지만, 자동 진행이나 재열람 경로를 아트만으로 해결할 수 있다고 가정하면 안 된다.

비공개 정보는 `Show(..., false, ...)`의 `?` 표시와 바늘 비활성으로 보호한다. 교환 중에도 숨기고, 폐기 값은 공개하지 않는다. 다만 모델의 `AiValue`·`DebugSnapshot()`은 숨겨진 값에 접근할 수 있으므로 모델 차원의 권한 차단은 아니다. 새 View가 모델 전체를 무조건 표시하면 현재 보장을 깨뜨릴 수 있다.

## 6. 구현 상태 분류

| 항목 | 상태 | 범위/제약 |
| --- | --- | --- |
| 기본 라운드·선택·4조합 교환·수명/판돈 계산·승패·새 게임 | 코드상 구현 완료 | 임시 정책/수치는 3절에서 별도 구분 |
| 월드 시계 선택·한 번의 행동 확정·중복 입력 차단 | 코드상 구현 완료 / UX 검증 필요 | 클릭 가드와 카메라 전환 가드는 서로 다름 |
| 공개 시점·비공개 숫자/바늘·폐기 숨김 | 코드상 구현 완료 | 최종 아트의 불투명도·겹침은 별도 화면 검증 필요 |
| 회중시계 호버·상승·뚜껑·실물 교환·물건 출입 | 임시 구현 | Transform/도형 기반. 최종 개폐 스프라이트·복합 애니메이션 아님 |
| 수명·판돈 계수기 보간·끝값·배당·복구 | 코드상 구현 완료 / UX 검증 필요 | 숫자 정확성과 차감 원인 이해도를 혼동하지 않음 |
| 기본 시차·대상 집중·복귀 | 코드상 구현 완료 / UX 검증 필요 | Inspector FocusPoint/시간/FOV, 기본 파라미터 유지 |
| 도움말·작은 라운드 표시·종료 전표 | 구현되었으나 UX 검증 필요 | 튜토리얼 완성/메뉴/일시 정지 시스템으로 해석하지 않음 |
| 아이템 구역과 6개 실루엣 샘플 | 임시 구현 | 배치 전용, 일반 플레이 기본 숨김, 양측 3개는 수량 규칙 아님 |
| 아이템 데이터·획득·보유·사용·효과·타이밍 | 미구현 / 기획상 미확정 | 현재 Match 상태/진행에 아이템 사용 창이나 효과 처리가 없음 |
| 실제 픽셀아트·마네킹 대체·장치 최종 외형 | 미구현 | 현 도형·스프라이트는 검증용 |
| 게임 전용 사운드와 최종 조명/후처리 품질 | 미구현 또는 최종 검증 미실시 | 현재 게임 스크립트에는 사운드 재생 연결이 없음. 템플릿/패키지 존재를 완성 기능으로 세지 않음 |
| 월드 플레이 씬의 빌드 진입·독립 Player 동작 | 미설정 / 미검증 | 현재 빌드 진입은 SampleScene. Editor 플레이 가능과 별개 |

## 7. 기획 결정과 다음 개발 우선순위

다음은 **검토 순서 제안**이다. 선택지를 확정하거나 구현을 승인한 목록이 아니다.

### A. 아트 작업 전에 해결할 사항

1. **결과 확인 흐름**: U2·U3의 타이밍으로 정보가 전달되는지 기본 시간의 초회 플레이부터 관찰한다. 자동 진행 유지/관찰 시간/확인 대기/다시 볼 정보의 필요성을 먼저 결정한다.
2. **결정 전 재열람과 클릭 의미**: U4·U5. 시계 재집중, 장치 열람, 유지/교환 확정을 어디까지 한 클릭으로 허용할지 결정한다. 먼저 카메라 감도나 새 시스템부터 바꾸지 않는다.
3. **필수 규칙 안내와 도움말 정책**: 자기 값으로 자기 수명 차감·한쪽만 교환·판돈 독식이 전달되는지 확인하고 H/일시 정지/재시작의 역할을 정한다.
4. **임시 규칙의 기획 확인**: 선생성·중복 허용·플레이어 선선택·폐기 비공개·초과 손실·Draw·AI/초기 수명/분포를 Notion과 대조해 결정한다. 코드에 있다는 이유만으로 최종안으로 삼지 않는다.
5. **아이템의 선행 기획**: 목적·종류·최대 보유/배치 수량·사용 횟수/시기·AI와 비공개 정보의 관계를 결정한 뒤 실제 시스템 범위를 정한다. 현재 앵커 3개를 재고 상한으로 사용하지 않는다.
6. **아트 제작 기준**: 읽어야 할 숫자 크기, 핵심 조작 물건의 의미, 기본/집중 시점의 필요한 정보, 목표 화면비·해상도·PPU를 정한다. 시계와 장치의 대규모 레이아웃 변경 필요성도 그때 판단한다.
7. 외부 사용자에게 빌드를 배포해 검증할 경우에는 플레이 대상 씬과 빌드 진입·독립 실행 확인이 선행되어야 한다. 현재 빌드를 월드 UX 테스트로 오인하지 않는다.

### B. 아트 작업과 함께 진행 가능한 사항

- A에서 확정한 의미를 바탕으로 봉인 패/회전 장치·시계·계수기·마네킹을 실제 리소스로 교체한다.
- 임시 뚜껑과 물건 반응·숫자 변화에 맞춰 소리·표면 디테일·미세 움직임·그림자·조명을 조정한다.
- 실제 리소스의 SpriteRenderer 순서, 가림, 눕힌/측면 스프라이트 원근, 숫자 글꼴·PPU·픽셀 떨림을 실제 화면으로 확인한다.
- View 참조와 `Visual`/`Mechanism` 등 교체 지점을 활용한다. 아트 교체를 이유로 Match를 재작성하지 않는다.

### C. 핵심 검증 뒤로 미뤄도 되는 사항

- 기존 uGUI 프로토타입만의 중간 숫자 보간 정밀도 보완. 현재 월드 UX의 계산 모델 문제와는 구분한다.
- 사용하지 않는 템플릿 안내 에셋·설정 정리, 배치 편의 도구의 고도화 등. 현재 검증과 관계없는 정리 때문에 기존 씬·패키지를 바꾸지 않는다.
- 범용 아이템 프레임워크·추가 매니저·대규모 구조 변경은 구체적인 기획/필요가 생기기 전 보류한다.

## 8. 테스트와 검증 근거, 한계

### 이번 분석에서 확인한 것

- 소스 선언은 테스트 파일 **8개**, 메서드 **32개**, TestCase 확장 기준 **EditMode 27 + PlayMode 15 = 42개**다.
- Unity MCP 읽기 조회에서 현재 열린 TabletopGame의 Canvas 수 0, Missing Script 수 0, 핵심 View 참조·카메라 설정·아이템 기본 비활성·샘플 Collider 없음 등을 확인했다. 현재 Console 읽기 조회에서 오류/경고 항목은 0개였다. **이는 새 컴파일·빌드 성공을 뜻하지 않는다.**
- 이전 4.5단계 실행 작업의 상태를 이번에 읽기 조회했다. EditMode job `7ef195f49f90443fb87efec2a86d8b0f`는 `succeeded`, 27/27 완료·실패 목록 없음, PlayMode job `eb9f8be6d8654ce0bc9b37f33acf5202`는 `succeeded`, 15/15 완료·실패 목록 없음이었다. 도구가 개별 결과 본문은 반환하지 않았으므로 이 기록은 **과거 실행 완료 상태**의 근거다. 이번 분석에서 재실행한 결과가 아니다.
- 이번 문서 작성 전후로 기존 Assets/Packages/ProjectSettings/AGENTS/GAME_DESIGN의 **236개 파일 해시가 모두 동일**함을 확인했다. 추가한 파일은 루트 `HANDOFF.md`뿐이다.

### 테스트가 실제로 검사하는 범위

| 테스트 파일 | 코드에 작성된 검사 |
| --- | --- |
| [MatchRulesTests](Assets/TimeTheft/Tests/EditMode/MatchRulesTests.cs) | 교환 4조합, 차감 상한·Draw·배당, 값 범위·배분, 잘못된 순서/중복 입력 거부, 여러 Seed 종료/총량 보존, AI의 숨은 값 독립성·확률 경향·재현 |
| [TabletopSceneTests](Assets/TimeTheft/Tests/EditMode/TabletopSceneTests.cs) | Study 기본/최대 시차의 주요 영역·스프라이트 설정·고정 방향·집중 자세. Game은 시계 프리팹·조작부 텍스트 가독성·참조·Canvas 없음·Missing Script 등 |
| [TabletopItemLayoutTests](Assets/TimeTheft/Tests/EditMode/TabletopItemLayoutTests.cs) | 샘플 기본 숨김·입력 없음·접지·크기·범위·서로 겹침·시계/장치 표시 가림 제한. 기하 투영 기준 |
| [PrototypeFlowTests](Assets/TimeTheft/Tests/PlayMode/PrototypeFlowTests.cs) | 기존 uGUI 실제 레이캐스트·비공개·복수 게임 완주·재시작·슬롯 편집·시계 재사용 |
| [TabletopCameraLookTests](Assets/TimeTheft/Tests/PlayMode/TabletopCameraLookTests.cs) | 포인터 한도·중앙 복원·포커스 상실·화면 밖·Mouse 없음·재활성화·TimeScale=0 |
| [TabletopFocusCameraTests](Assets/TimeTheft/Tests/PlayMode/TabletopFocusCameraTests.cs) | Study 5대상 실제 입력 호버/집중/복귀·중단·무효 대상·비활성화 |
| [TabletopGameFlowTests](Assets/TimeTheft/Tests/PlayMode/TabletopGameFlowTests.cs) | 월드 실제 포인터 입력으로 두 Seed 완주, 비공개·교환/비교환·중복 차단·표시 총량/끝값·배당·Draw·R 재시작·H 도움말·재사용 |
| [TabletopPresentationTests](Assets/TimeTheft/Tests/PlayMode/TabletopPresentationTests.cs) | 아트만 상승·집중 기준점 안정·비공개/뚜껑·실물 교환·숫자 단조 보간·총량/끝값·스킵/재시작·선택 물건 출입/Collider 차단 |

검증 범위를 과장하지 않기 위한 주의:

- 월드 Flow 테스트는 설정 복제본의 시간을 0.05초로 줄이고 TimeScale=0에서 실행한다. Presentation 테스트는 별도 시간으로 일부 중간 연출도 검사하지만, 기본 시간 전체를 초회 사용자가 이해하는지 증명하지 않는다.
- Focus/GameFlow/Presentation 테스트는 `#if UNITY_EDITOR`로 보호되어 있다. PlayMode 통과는 독립 Player 빌드·배포 확인이 아니다. asmdef는 각 테스트 폴더에서 확인한다.
- 배치/가독성 검사는 주로 16:9의 투영 경계와 글자 높이·방향 기준이다. 모든 월드 정보가 모든 시점에서 충분히 읽힌다는 보증이나 실제 픽셀아트 품질 검사는 아니다.
- 아이템 테스트는 배치 시험만 검사한다. 아이템 효과·사용 시기·인벤토리 기능을 구현/검증했다는 뜻이 아니다.
- SampleScene은 같은 모델을 쓰지만 `PrototypeController.Resolve`가 세 숫자를 독립 반올림하므로 **중간 표시 총량이 일시적으로 1h 어긋날 수 있다**. 최종 모델 계산과는 별개이며 기존 Prototype 테스트는 중간 총량을 검사하지 않는다. 월드 `NumbersFrame`의 총량 보존과 구분한다.
- 이번에는 기본 속도의 신규 수동 플레이, 첫 사용자 테스트, 다른 해상도/화면비, 최종 리소스, 사운드, 독립 빌드를 검증하지 않았다. 현재 코드 읽기만으로 U1~U10의 사용성 결론을 확정하지 않는다.

## 9. 새로운 Codex 대화에서 재개할 때

1. **먼저 이 문서와 현재 `AGENTS.md`를 읽고 실제 파일을 재확인한다.** 이 문서 이후 변경된 코드가 있으면 현재 파일을 우선한다. 문서의 미정/제안 항목을 구현 승인으로 해석하지 않는다.
2. 사용자에게 받은 **재개 작업 범위**를 따른다. 현재 상태가 4.5단계라고 해서 다음 작업이 곧바로 5단계 아트라고 가정하지 않는다. 일시 중단 요청 동안 추가 개발·예약 작업을 진행하지 않는다.
3. 작업 폴더의 Git 미추적 파일을 반드시 확인한다. 기존 사용자 변경을 보존하고, 명시 요청 없이 커밋/정리/되돌리기를 하지 않는다.
4. 규칙 변경 전에 최신 Notion을 읽는다. 접근할 수 없다면 알리고, 이 문서나 코드의 임시 정책을 최신 확정 기획으로 대체하지 않는다. 원본 기획은 Notion, 이 문서는 구현 상태 기록이다.
5. Unity Editor 조회/변경은 이미 구성된 **stdio Unity MCP**를 우선한다. 인스턴스 목록과 프로젝트 경로를 확인해 TimeTheft에 고정한다. 이름/해시가 바뀌었다고 Slash 등 다른 프로젝트로 작업하지 않는다. 별도 HTTP 서버를 켜거나 MCP 불가를 파일 직렬화 편집으로 우회하지 않는다.
6. 실제 월드 플레이는 `Assets/Scenes/TabletopGame.unity`에서 확인한다. Study에서 게임이 진행되지 않는 것은 그 씬의 의도다. SampleScene과 두 프리팹의 용도를 구분한다.
7. View의 시계 3개·장치·도움말·결과 전표 참조와 대상 `FocusPoint`를 유지한다. 다음 사용자 요청이 없는 한 카메라 감도/기본 구도를 임의 조정하지 않는다.
8. 시계 배열 인덱스는 물리 시계 ID다. 교환 뒤 소유 인덱스와 Transform 위치가 바뀌므로 화면 좌우 순서와 동일시하지 않는다. 다음 라운드/재시작에는 초기 자세로 복구된다.
9. 공개 전 `AiValue`·폐기 값·`DebugSnapshot`을 일반 화면에 바인딩하지 않는다. 선택 전 `PlayerClock/AiClock=-1`이므로 `PlayerValue/AiValue`를 미리 읽지 않는다. `DidExchange`·행동 필드는 유효한 단계에서만 사용한다.
10. 카메라 이동 완료를 Match 진행의 필수 조건으로 새로 묶지 않는다. 반대로 `Stage`만 보고 표시가 완료됐다고 가정하지도 않는다. 특히 `GameOver`와 `IsResultVisible`, 정산 모델값과 보간 표시값은 다르다.
11. `ItemSlots`는 배치 샘플이며 3개 보유/사용 창/소비 규칙이 없다. Inspector에서 미리보기용으로 켠 상태를 실제 인벤토리로 착각하지 않는다.
12. 변경이 승인된 뒤에는 관련 테스트와 실제 기본 속도 UX를 구분해 검증한다. 이전 테스트 도구 실행에서 `EditorSettings.enterPlayModeOptions`나 `ProjectSettings/SceneTemplateSettings.json` 같은 부수 변경이 생긴 적이 있으므로, 다음 실행 전후 설정 diff를 확인한다. 현재 파일을 확인하지 않고 무조건 삭제/복구하지 않는다.

재개 시 첫 검토 대상으로 권하는 것은 **U2/U3의 결과 관찰 흐름과 U4의 결정 전 재열람**이다. 이를 포함한 기획·UX 판단은 사용자와 검토한 뒤 확정하며, 이번 인수인계는 그 판단이나 다음 구현을 대신하지 않는다.
