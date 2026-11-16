# Unity 작업 및 검증

## Unity 작업 규칙

- Unity Editor 상태를 읽거나 수정할 때 Unity MCP를 우선 사용한다. 일반 C#처럼 파일 편집으로 충분한 작업은 소스에서 처리한다.
- Editor 인스턴스의 프로젝트 절대 경로가 작업 대상과 일치하는지 확인하고 활성 대상으로 고정한다. 이미 구성된 stdio MCP를 사용한다. 별도의 HTTP MCP 서버를 임의로 실행하거나 종료하지 않는다.
- MCP가 필요한 작업에서 Coplay가 설치되지 않았다면 [Packages/manifest.json](../../Packages/manifest.json)을 통해 설치를 시도한다. 설치나 연결이 불가능하면 상태를 알리고 해당 Editor 작업을 중단한다.
- Scene·GameObject·Component·Prefab·Inspector 변경은 가능한 경우 Unity MCP로 수행한다. `.unity`와 `.prefab` 직렬화 파일의 직접 편집은 금지한다.
- 에셋을 이동할 때 연결된 `.meta`를 함께 유지한다. 기존 GUID·씬 및 Prefab 참조·asmdef와 asmref·namespace·Resources 키를 보존한다. 승인된 변경이 이 경계에 영향을 주면 기존 참조의 이관까지 검증한다. `Library/`와 `Temp/` 등 생성물은 작업 소스에서 제외한다.
- Editor 조작 전 열린 씬·Play/일시정지·Prefab Stage·미저장 변경·관련 설정을 확인한다. 사용자 작업을 보호할 수 있는 상태에서만 씬 전환이나 Play에 진입한다. 검증용 설정은 실행용 사본을 사용하고, 작업으로 시작한 Play는 종료하고 작업 전 씬·Editor 설정·입력 상태를 가능한 범위에서 복구한다. 테스트 생성물은 이번 실행에서 만든 것으로 확인된 범위에서만 정리한다.

### 변경 범위에 따른 검증

- 코드 변경은 컴파일을 확인한 뒤 기존 Unity Test Framework에서 영향받는 검사를 실행한다. 순수 로직은 관련 EditMode, 입력·씬·UI 연결은 관련 PlayMode를 우선한다. 여러 흐름이 함께 바뀌면 관련 통합 검사로 범위를 넓힌다.
- 실행 경로나 조작감처럼 자동 검사만으로 확인하기 어려운 변경은 필요한 수동 플레이 또는 독립 Player 검증을 선택한다. 자동 검사, 수동 플레이, Player 확인의 범위와 결과를 각각 기록한다.
- 문서 변경은 내용·경로·링크·Git diff로 검증한다.

## 현재 기술 구조

C#과 Unity 6 `6000.0.68f1`을 사용한다. 주요 패키지는 URP `17.0.4`, Input System `1.18.0`, uGUI `2.0.0`, Test Framework `1.6.0`, Timeline `1.8.10`, Coplay `v10.3.0`이다. [Editor 버전](../../ProjectSettings/ProjectVersion.txt) · [패키지 목록](../../Packages/manifest.json)

| 위치 | 역할 |
| --- | --- |
| [TimeTheftTitle.unity](../../Assets/Scenes/TimeTheftTitle.unity) → [TimeTheftGame.unity](../../Assets/Scenes/TimeTheftGame.unity) | 현재 활성 빌드 씬 순서. [빌드 씬 설정](../../ProjectSettings/EditorBuildSettings.asset) |
| [Gameplay/Match](../../Assets/Scripts/Gameplay/Match) · [Gameplay/AI](../../Assets/Scripts/Gameplay/AI) | 시계 정보, 턴, 정산, 아이템, AI 판단을 다루는 게임 로직. |
| [TimeTheftGameController](../../Assets/Scripts/Gameplay/TimeTheftGameController.cs) · [UI](../../Assets/Scripts/UI) · [Presentation](../../Assets/Scripts/Presentation) · [Camera](../../Assets/Scripts/Camera) | 모델 호출·진행·입력 연결 및 현재 3D 표시. 메인 씬에는 H2 회중시계, 공급 장치, 아이템 모델·해치, 상대 캐릭터, 계수기와 테이블이 연결돼 있다. |
| [개발 씬](../../Assets/Scenes/_Development) · [Development](../../Assets/Scripts/Development) | Study·Validation·시각 검토 및 제작 도구. |
| [TimeTheft.Runtime.asmdef](../../Assets/Scripts/Core/TimeTheft.Runtime.asmdef) · [Tests](../../Assets/Tests) | Runtime 소속 폴더는 asmref로 연결하며 타이틀·아이템 애니메이션·Counter Study·테스트의 별도 어셈블리를 사용한다. EditMode와 PlayMode 테스트는 기존 어셈블리에서 실행한다. |

현재 게임 로직과 3D 표현은 개발 기준점이다. 승인된 표현 변경에서는 기존 로직과 데이터의 재사용 범위를 확인하고 입력·카메라·UI·연출 등 연결에 미치는 영향을 검증한다. 에셋과 제작 원본의 보존을 포함한 현재 개발 방향은 [인수인계](../project/HANDOFF.md#개발-방향)를 참고한다.

## 과거 구현·검증 기록

11월 3일 구조 개편 전후의 테스트, 참조 검사, Player 검증과 기존 실패는 [인수인계의 개발 이력](../project/HANDOFF.md#개발-이력)에 기록되어 있다. 결과는 각 실행 시점과 검사 범위에 적용된다.
