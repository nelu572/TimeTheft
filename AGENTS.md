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
- 아이템과 직접 쓰는 메모는 아직 구현하지 않았다. 과거 규칙의 아이템 기록을 현행 요구사항으로 해석하지 않는다.

| 경로 | 역할 |
| --- | --- |
| `Assets/Scenes/TimeTheftGame.unity` | 유일한 메인 개발·플레이 씬. 기본 Build Profiles 목록의 첫 활성 씬이다. |
| `Assets/Scenes/TabletopGame.unity` | 이전 3개 시계·유지/교환 월드 프로토타입. 참고·Legacy 회귀 검증용이며 신규 게임을 병행 개발하지 않는다. |
| `Assets/Scenes/SampleScene.unity` | 이전 uGUI 프로토타입. 빌드 목록에서는 비활성으로 보존한다. |
| `Assets/Scenes/TabletopStudy.unity` | 배치·Focus 실험용. 게임 진행과 분리해 보존한다. |
| `Assets/Scenes/CounterStructureStudy.unity` | 통합 계수기의 독립 제작·실험용. 고정 표시값을 사용한다. |
| `Assets/TimeTheft/Scripts/TimeTheftMatch.cs` | 현행 규칙·시계 상태·턴·수명·Time Bank의 유일한 소유자. 순수 C#이다. |
| `Assets/TimeTheft/Scripts/TimeTheftGameController.cs` | 입력·AI 호출·공개/정산 코루틴·재시작을 연결한다. |
| `Assets/TimeTheft/Scripts/TimeTheftGameView.cs` | 씬에 배치한 시계·계수기·행동 UI·결과 전표를 표시한다. |
| `Assets/TimeTheft/Scripts/ClockTokenView.cs`, `Assets/TimeTheft/Prefabs/ClockToken.prefab` | 제작 중 모델과 독립된 임시 시계 표시부. 메인 씬에서 5개를 재사용한다. |
| `Assets/TimeTheft/TimeTheftSettings.asset` | 현행 규칙의 잠정 수치·숫자 분포·선공·AI 성향·연출 시간·재현용 시드. |
| `Assets/TimeTheft/Scripts/TabletopCameraLook.cs`, `TabletopFocusCamera.cs`, `TabletopFocusTarget.cs` | 기존 자유 시차·호버·Focus·열람 입력. 카메라 및 시차 설정을 임의로 변경하지 않는다. |
| `Assets/TimeTheft/Scripts/Legacy/` | 구형 진행·View·UI·설정. 구형 규칙 모델은 `TimeTheft.Legacy.LegacyMatch`이며 메인 진입점과 분리한다. MonoBehaviour 이름/네임스페이스와 에셋 GUID는 구형 씬 참조를 위해 유지한다. |
| `Assets/TimeTheft/PrototypeSettings.asset`, `Prefabs/Clock.prefab`, `Prefabs/TabletopClock.prefab` | 구형 프로토타입·Study에서 쓰는 설정과 표시 자산. 제작 중인 최종 회중시계 모델이 아니다. |
| `Assets/TimeTheft/Table/Prefabs/TimeTheft_Table_T3.prefab` | 기존 메시·UV·텍스처를 보존하는 T3 테이블. |
| `Assets/TimeTheft/Counter/IntegratedTimeCounter.prefab` | 기존 Bank·Player·Opponent 통합 계수기. 메인 인스턴스의 TMP 세 개만 현행 View에 바인딩한다. |
| `Assets/TimeTheft/Tests/` | 현행 규칙·메인 통합·공통 카메라·Study 검사. 이전 규칙 테스트는 `Legacy` 카테고리로 구분한다. |
| `.agents/skills/commit/` | 명시적으로 요청받은 커밋 작업 지침. |

### 씬·표시·아트 보호 기준

- 실제 게임은 `TimeTheftGame`을 열어 Play한다. 새 씬이나 중복 진행 시스템을 만들지 않는다. `TabletopGame`·`SampleScene`에는 현행 규칙을 추가하지 않는다.
- 메인 씬의 `TimeTheftGame` 루트는 새 Controller와 View만 사용한다. `ClockSlots`는 검증용 임시 시계 5개, `Action UI`는 징수·배급, `Turn Feedback`은 수치 변화 표시를 담당한다.
- 메인 씬에 남겨 둔 과거 배분 기준점과 숨긴 피드백 도형은 보존용 Transform/표시 자료다. 구형 Controller·View·DecisionUI·RoundFeedback 컴포넌트는 메인에 연결하지 않는다.
- 새 묶음의 숫자·종류는 설정의 `Preview Duration`(기본 5.5초) 동안 자동 공개한 뒤 닫고 섞는다. 내 차례에는 닫힌 시계를 먼저 클릭해 선택하고, 숫자·종류를 숨긴 상태로 징수/배급을 결정하면 해당 시계가 자동 공개·정산된다. 선택된 `Visual`만 살짝 상승하며 Collider·FocusPoint는 유지한다. AI도 같은 순서로 판단하고 마지막 두 시계에도 직접 선택을 적용한다. 세부 정산 규칙은 Notion을 따른다.
- 숨겨진 값은 View에 전달하지 않는다. `TimeTheftMatch.GetClock`의 `Face`는 비공개·이미 치운 시계·폐기 상태에서 비어 있다. 현재 행동 결과 관찰 중인 시계만 공개를 유지한다. AI에는 순서나 위치가 없는 `AiObservation`만 전달한다. 화면에 공개 구성/결과의 자동 기록 목록을 만들지 않는다.
- 실행 중 수명·Bank·이동량은 분 단위 `int`이며 공개 API 이름에 `Minutes`를 붙인다. 설정의 `InitialLifeHours`와 시계 `Hours`는 시간 단위로 보존한다. 통합 계수기는 누적 `HH:MM`과 고유 색상을 유지하며 24시간마다 순환하지 않는다. 실제 정산과 표시 보간 모두 총량을 보존하고, 모델 계산에는 부동소수점 반올림을 쓰지 않는다.
- 기존 테이블·통합 계수기 외형, 카메라·시차·FocusPoint, Blender 원본·메시·UV·텍스처 및 제작 중인 회중시계 Prefab·개폐 애니메이션은 보호한다. 최종 시계 크기·5개 배치는 모델 완성 뒤 검토한다.
- 시계는 모델에서 관리하는 숫자·종류·가시성·사용 상태를 표현하는 View다. 최종 모델을 연결할 때 규칙 모델을 3D Prefab에 종속시키지 않는다.
- `ItemSlots`는 비활성 배치 샘플이며 아이템 보유량/슬롯 수 규칙이 아니다. 아이템·메모·상대 캐릭터·최종 개폐/셔플/폐기·사운드·VFX는 별도 후속 작업이다.
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
