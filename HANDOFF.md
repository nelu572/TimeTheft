# TimeTheft 개발 인수인계

기준일: 2026-10-25. 현행 구현은 [Notion의 분 단위 징수/배급 규칙 v0.4](https://app.notion.com/p/401b3e6e776c8034a3bfdf083f95c217)과 사용자 추가 지시 v0.5(시계 먼저 선택)를 함께 반영했다. Notion은 수정하지 않았다. 이 문서는 실제 구현·진입점·보존 범위와 검증 결과를 기록하며 게임 기획을 대신하지 않는다.

## 1. 지금 열어야 할 씬

**`Assets/Scenes/TimeTheftGame.unity`를 열고 Play한다.** 이 씬 하나를 현행 게임의 개발 기준으로 사용한다. 기본 Build Profiles 공유 씬 목록에서도 첫 번째이자 유일한 활성 씬이다. 별도 활성 Build Profile은 없다. 비활성 `SampleScene` 항목은 보존한다.

새 묶음의 시계 5개가 **기본 5.5초 자동 공개**된 뒤 입력 없이 닫힘·셔플이 진행된다. 내 차례에는 **닫힌 시계 직접 클릭 → 비공개 선택 강조 → 징수 / 배급 결정 → 해당 시계 자동 공개·정산** 순서로 진행한다. AI도 남은 위치 중 하나를 먼저 고른 뒤 공개 집계만으로 징수/배급을 정한다. 시계 선택만으로 숫자·종류는 공개되지 않으며 행동 버튼은 선택 후에만 열린다. 두 개가 남아도 같은 선택 단계를 거치며, 선택되지 않은 마지막 시계는 열지 않고 폐기한다. 다음 묶음도 자동 공개로 이어진다. H 도움말, 우클릭/Esc 장치 Focus 복귀, Space 현재 연출 단축, R 새 게임과 종료 전표 다시하기를 사용할 수 있다. 도움말은 일시 정지가 아니다.

관찰 시간은 `Assets/TimeTheft/TimeTheftSettings.asset` Inspector의 **Preview Duration**에서 조절한다. 준비 완료 버튼은 제거했다. 관찰 중에는 시계·행동 선택을 받지 않으며 Space로 관찰 시간을 단축하지 않는다. 공개된 구성이나 과거 결과의 자동 기록 목록은 없다.

## 2. 최초 v0.3 전환 당시 분석·씬 선택 기록

| 씬 | 조사 당시 구현·결합 | 전환 후 역할 |
| --- | --- | --- |
| `TimeTheftGame` | 기존 월드 Controller/View와 3개 시계·유지/교환 규칙. T3 테이블과 실제 통합 계수기 TMP 세 개, 입력 UI·결과 전표·카메라·Focus 연결까지 갖춤 | 현행 5개 시계 교대 게임. 완성도가 높은 계수기 연결과 아트·시점을 재사용해 유일한 메인으로 선정 |
| `TabletopGame` | 같은 월드 진행 코드를 사용하지만 분리된 임시 시간 장치를 사용. 선택·배분·교환·정산 전체 진행 가능 | 이전 규칙의 참고·Legacy 회귀 검증. 현행 기능을 병행 추가하지 않음 |
| `SampleScene` | 고정 Canvas·EventSystem·3개 시계 생성/재사용·유지/교환·정산·결과를 갖춘 uGUI 프로토타입 | 이전 규칙의 참고·Legacy 회귀 검증. 빌드 비활성 보존 |
| `TabletopStudy` | 월드 배치·호버·Focus 카메라 검사. 게임 Controller 없음 | 공간·카메라 독립 실험. 보존 |
| `CounterStructureStudy` | 통합 계수기 구조·표면·폰트·시점 제작 도구와 고정 표시값. 게임 진행 없음 | 계수기 제작 전용. 보존 |

조사 당시 열린 씬은 `TimeTheftGame`, 작업 트리는 깨끗했고 브랜치는 `main`이었다. Unity 6000.0.68f1, URP 17.0.4, Input System 1.18.0, uGUI 2.0.0, Test Framework 1.6.0이며 네트워크 게임 코드·아이템·메모는 없다. 기존 stdio Coplay Unity MCP의 프로젝트 경로를 확인하고 TimeTheft 인스턴스에 고정해 씬/Prefab을 편집했다. 새 패키지·서버·씬을 추가하지 않았다.

기존 흐름은 순수 C# Match → Controller 코루틴 → View였으며 `PrototypeSettings`를 uGUI/월드가 공유했다. `TimeTheftMatch`의 3칸 배열·양측 배분·Keep/Swap 동시 확정·교환·양측 차감·판돈 독식과 관련 View·UI·테스트가 구형 규칙에 직접 결합되어 있었다. 단순히 3을 5로 바꾸는 대신 현행 모델과 진행/표시를 교체했다.

## 3. 현행 코드와 책임

| 파일·클래스 | 책임 |
| --- | --- |
| `Assets/TimeTheft/Scripts/TimeTheftMatch.cs` / `TimeTheftMatch` | Unity 오브젝트를 모르는 순수 C# 모델. 시계 생성·셔플·공개 범위·사용/폐기·행동 확정·턴·수명·Bank·승패의 유일한 소유자 |
| 같은 파일 / `AiObservation`, `TimeTheftAi` | 공개 집계·수명·Bank로 행동을 판단. 시계 선택은 별도로 남은 위치 ID와 AI RNG만 받아 균등 선택. 숨은 숫자·종류·순서와 Match 참조를 받지 않음 |
| `Assets/TimeTheft/Scripts/TimeTheftSettings.cs` / `GameRules`, `TimeTheftSettings` | 잠정 수치·분포·선공·AI 성향·연출 시간·시드. 게임 시작 때 규칙을 복사해 외부 변경으로 진행 중 모델을 바꾸지 않음 |
| `Assets/TimeTheft/Scripts/TimeTheftGameController.cs` | 키 입력·버튼 콜백·AI 호출·공개/이전/폐기 코루틴. 시계 RNG와 AI RNG 분리. 중복 입력 거부와 재시작/비활성화 때 진행 취소 |
| `Assets/TimeTheft/Scripts/TimeTheftGameView.cs` | 모델의 공개 상태를 시계·화면 고정 행동 UI·수치 변화·기존 결과 전표에 적용. 통합 계수기에 분 단위 누적 HH:MM과 실제 수령 대상을 표시. 고유 색상과 보간 총량을 유지 |
| `Assets/TimeTheft/Scripts/ClockTokenView.cs` | 숫자·일반/빨강 문자판·동일한 덮개·임시 닫힘/셔플 표시. 기존 FocusTarget의 클릭을 물리 위치 ID로 전달. 선택된 Visual만 0.055 상승하며 닫힌 문자판·Collider 기준점은 유지 |
| `Assets/TimeTheft/Prefabs/ClockToken.prefab` | 최종 모델과 별개로 만든 재사용 임시 표현. Sprite/월드 TMP와 SortingGroup으로 공개 숫자·종류를 표시. 루트의 Collider·FocusTarget은 선택 입력 전용이며 클릭 시 카메라 집중은 하지 않음 |
| `TabletopCameraLook`·`TabletopFocusCamera`·`TabletopFocusTarget` | 기존 시차·호버·장치 열람·복귀·UI 클릭 관통 차단을 재사용. 소스와 기존 Inspector 설정 유지 |

모델의 단계는 `BatchComplete → BatchPreview → Shuffling → AwaitingClock → AwaitingAction → Revealing → Settling`이다. 정산 중 한쪽이 0분이 되면 모델은 즉시 `GameOver`가 되고, View는 정산 연출 뒤 결과 전표를 연다. 화면 연출 완료와 모델 종료 시점을 구분한다. 종료 Bank는 그대로 남고 승자에게 지급하지 않는다.

`GetClock`은 최초 공개 및 현재 행동의 공개 시계만 `Face`를 반환한다. 이미 치운 시계와 비공개/폐기 시계는 값을 전달하지 않는다. AI가 받는 집계는 메모리에서 유지하는 공개 정보이며 화면 자동 기록지가 아니다. `SelectClock(actor, index)`는 실제 위치를 고르고 `AwaitingAction`으로 진행하지만 공개하지 않는다. `CommitAction`이 선택한 시계를 `Revealing`으로 전환하면 Controller가 자동 공개·정산한다. `CurrentClock`은 사용 개수와 무관한 선택 ID다. 사용 여부는 시계별로 보관한다. 현재 시계는 결과 관찰 중에만 보이고 다음 턴에는 치운다.

### 현재 잠정 설정

설정 에셋은 **`Assets/TimeTheft/TimeTheftSettings.asset`**이다.

- `InitialLifeHours` 총수명 30h, 시작 의무 납부 후 1200분(20:00)씩·Bank 1200분. 기획상 기본 수명 확정은 아니다. 설정은 납부 뒤 양측이 살아 있도록 10h 초과만 허용한다.
- 숫자는 1~12 균등·중복 허용. `MinimumHours` / `MaximumHours`로 범위를, `NumberWeights`로 각 숫자의 상대 가중치를 바꾼다. 가중치 배열을 비우면 균등이다. 숫자는 12에 고정된 규칙이 아니다.
- 추가 2개 종류는 각각 빨강 확률 0.5. 보장된 일반 2개·빨강 1개와 별개다.
- 플레이어 선공은 `FirstParticipant`에서 변경할 수 있다. 묶음 경계에서도 전역 턴 교대를 유지한다. 4턴 묶음이므로 현재는 같은 참가자가 새 묶음에서도 시작한다. 선공을 묶음마다 교체하는 별도 정책은 추가하지 않았다.
- AI는 먼저 남은 위치 ID를 균등 선택한 뒤, 공개 종류별 평균값·분 단위 수명·Bank로 징수와 배급의 순수명 우위 변화를 추정한다. 빨간 배급은 상대 지급이므로 기대 이득에서 빼며, Bank 잔액 한도와 보유 수명을 반영한다. `AiRiskTolerance` 기본 0.5. 최적 전략이나 확정 밸런스가 아니다.
- `PreviewDuration` 기본 5.5초. 매 묶음에 같은 자동 관찰 시간을 적용한 뒤 임시 닫힘·셔플로 연결한다. 플레이어/AI 선공 모두 같다.
- `RandomSeed=0`은 새 난수, 그 외는 재현용이다. 연출 시간은 같은 에셋에서 조절한다.

### v0.4 분 단위 정산과 v0.5 순서의 연결

- 실행 모델은 `PlayerLifeMinutes` / `AiLifeMinutes` / `BankMinutes` / `TransferredMinutes`를 분 단위 `int`로 보관한다. `InitialContributionMinutes=600`이며 양측 초기 납부는 그대로 10h씩이다.
- 설정은 `InitialLifeHours=30`, 시계 숫자는 `ClockFace.Hours`로 시간 단위를 유지한다. 게임 시작과 정산 경계에서만 정수 상수로 변환한다. 징수 요청량은 `Hours × 60`, 배급 요청량은 `Hours × 20`분이다. 실제량은 잔여 수명 또는 Bank 잔액과 `Math.Min`한다. 예: 1h 배급=00:20, 7h 배급=02:20.
- 일반 징수는 상대→Bank, 빨간 징수는 나→Bank, 일반 배급은 Bank→나, 빨간 배급은 Bank→상대다. 빨간 배급은 내 수명을 감소시키지 않는다. `TransferParticipant`를 기준으로 실제 수령/지급 주체와 변화량을 표시한다.
- `GameTime.Format`이 정수 `/60`, `%60`으로 누적 `HH:MM`을 만든다. 24시간마다 초기화하지 않는다. 규칙 계산에 부동소수점 반올림은 없고, 연출 보간만 별도 계산 후 모델의 정확한 끝값을 적용한다. 변화 없는 큰 정수까지 float로 변환하지 않도록 차이값을 double로 보간한다.
- `InitialLife → InitialLifeHours`, `withdrawButton → rationButton`는 `FormerlySerializedAs`로 기존 설정·씬 참조를 유지한 뒤 Unity에서 저장했다. `TimeAction`의 두 번째 값은 배급 `Ration`이며 런타임 모델은 저장 데이터가 아니다. Legacy의 시간 자료형·유지/교환은 변경하지 않았다.
- 수명·시계 설정은 분 변환과 전체 합계가 int 범위를 넘지 않도록 검증한다. 동작 중 수명·Bank가 음수가 되거나 시간이 새로 생성되지 않는다. Bank=0에서도 배급 버튼을 잠그는 새 규칙은 추가하지 않았다.
- 통합 계수기는 이미 TMP 문자열과 숫자/콜론 글리프를 지원하므로 외형·폰트·메시·UV·재질을 재설계하지 않았다. 시계 Prefab은 그대로 두고 선택 강조만 기존 `ClockTokenView` 코드에 연결했다.

## 4. 최초 v0.3 전환의 재사용·교체·보존 기록

- **재사용:** T3 테이블, 통합 계수기와 TMP 세 개, Player/Opponent 색상, 기존 카메라/시차/Focus, Canvas·EventSystem, 도움말 카드·결과 전표·다시하기, 계수기 옆 변화량 표시.
- **교체:** 메인의 구형 Controller·View, 3개 시계 선택/배분, 유지·교환 UI, 동시 행동, 교환 판정, 기존 정산·판돈 지급. `ClockSlots`의 구형 임시 인스턴스 3개를 새 임시 시계 5개로 바꿨다.
- **Legacy 분리:** 구형 규칙 전용 C# 12개를 `Assets/TimeTheft/Scripts/Legacy/`로 이동했다. `.meta` GUID와 MonoBehaviour 이름·네임스페이스를 보존해 기존 씬/Prefab을 유지한다. 구형 모델은 `TimeTheft.Legacy.LegacyMatch`로 이름과 네임스페이스를 분리했다. 구형 전용 회귀 테스트는 `Legacy` 카테고리다.
- **메인 구형 코드 연결 제거:** `TabletopGameController`·`TabletopGameView`·`TabletopDecisionUI`·`TabletopRoundFeedback`은 메인에 없다. 이들을 비활성화해 새 진행과 함께 두지 않았다. 남겨 둔 과거 배분 기준점과 숨긴 피드백 도형은 동작하지 않는 보존용 자료다.
- **보존:** `TabletopGame`·`SampleScene`·두 Study 씬, 기존 두 Clock Prefab·PrototypeSettings, 테이블·계수기 제작 원본과 아트. 기존 씬은 삭제하지 않았다. Blender·제작 중 모델·UV·텍스처·개폐 애니메이션은 건드리지 않았다.

최종 시계 크기와 테이블 위 5개 배치를 확정한 작업이 아니다. 원형 임시 표시의 일렬 배치는 규칙과 정보 공개 검증용이며 기존 카메라를 유지한다.

## 5. 문서 역할

| 문서 | 처리 |
| --- | --- |
| `AGENTS.md` | 공통 작업·하네스·Unity MCP·자산 보호·커밋 지침은 유지. 현행 구조와 충돌하는 이전 규칙/메인 연결 설명을 교체 |
| `HANDOFF.md` | 현재 씬·클래스·잠정 설정·Legacy 경계·실제 검증·제약을 기록. 구형 개발 단계와 완료 판정은 이번 현행 상태로 대체. 이전 전문은 Git 이력에 보존 |
| `GAME_DESIGN.md` | 기존 Notion 링크 안내 역할 유지. 기획 전체를 복제하지 않음 |
| `Assets/TimeTheft/Counter/README.md` | 계수기 제작·재생성 사용법은 유지. 현행 View·Time Bank·Legacy 구분과 분 단위 누적 HH:MM 표시 설명을 갱신 |
| 계수기 `Source/Galmuri/OFL.md` | 폰트 라이선스. 변경하지 않음 |

루트 README나 별도 시스템/작업 로그 문서는 새로 만들지 않았다. 추가 자동화·관리 체계·스킬 변경도 없다.

## 6. 검증 근거

- 과거 v0.3 최초 전환은 EditMode 49개·PlayMode 36개, 자동 공개/직접 선택 UX 개선은 EditMode 56개·PlayMode 39개를 통과했다. 당시 인출·행동 선확정 방식의 기록이며 현행 규칙과 구분한다. 이번 작업은 그 미커밋 변경 위에 최소 수정으로 이어 진행했다.
- 이번 Unity Editor 컴파일 오류 0. **EditMode 75/75 통과**: 분 정산·v0.5 순서 규칙 45개, 현행 씬 검사 2개와 기존 28개를 포함한다.
- **메인 PlayMode 8/8 통과**: 실제 월드 클릭으로 선선택·선택 강조·비공개 유지·행동 버튼 활성화를 검사하고, 행동 직후 선택한 시계만 공개되는지 확인했다. 양 종류 배급의 실제 수령 대상·02:20 지급·32:20 수명과 17:40 Bank 표시도 검사한다. 큰 분 정수에서도 보간 중 변하지 않는 수명과 전체 총량이 정확한지 추가 검증했다.
- **구현 당시 전체 PlayMode 41개 완료: 40개 통과·1개 실패, 제외/삭제 0개.** 실패는 기존 `TabletopFocusCameraTests.FiveAuthoredTargetsHoverClickFocusAndReturnThroughRealInput`의 Study 호버 검사(:90)다. 전체 실행에서는 창 포커스가 없는 상태로 `HoveredTarget=null`이 되었고, 같은 테스트의 단독 재실행은 1/1 통과했다. 재차 전체 실행에서도 같은 검사만 실패했다. 해당 테스트가 최초 한 번만 창 포커스를 주는 데 비해 다른 검사 도우미는 표본마다 포커스와 입력 Update를 명시하는 차이가 있다. 아래 Git 마무리 검증에서는 Unity 창을 전면에 유지한 동일 코드로 전체 41/41을 통과했다. 창 포커스에 의존하는 테스트 환경 문제로 분류하며 카메라 코드·Study 자산·단언을 수정하거나 이 검사를 제외하지 않았다.
- 기존 자동 5.5초 관찰·닫힘/셔플·직접 선택·AI 비공개 정보 금지·마지막 두 개 중 선택·1개 미공개 폐기·새 묶음·교대·승패·재시작/비활성화·UI가 먼저 파괴되는 종료 검사를 유지했다. 기존 Legacy/Study/카메라 테스트를 삭제하지 않았다.
- 규칙 테스트는 일반/빨강·양측 징수와 배급, 1·2·4·7·12h의 정확한 20X분, Bank 잔액 80분 한도·0, 잔여 수명 20분만 징수, 총량, 24:00/49:20/100:00, 설정 복사·숫자 분포·정수 오버플로 제한, 여러 Seed 완주와 AI 배급 위험 판단을 검사한다.

메인 씬을 별도 Play하여 기본 설정(총수명 30h, 관찰 5.5초)에서 입력 없이 `AwaitingClock`으로 진행하고 5개가 닫힌 채 행동 UI가 비활성인 것을 확인했다. 이후 **저장하지 않는 실행용 설정 복제본**(총수명 40h, 시계 7h, Seed 635, 결과 관찰만 8초)으로 빨간 시계 위치 1을 선택했다. 선택 시 모델 Face와 숫자·종류 표시는 비공개, 선택 강조와 행동 버튼만 활성화됐다. 배급 직후 같은 위치가 공개됐고 140분이 상대에게 지급돼 나 30:00·상대 32:20·Bank 17:40, `Bank → 상대 · 실제 02:20`과 변화량 ±02:20을 실제 1280×720 화면에서 확인했다. AI의 다음 턴까지 지나 사용 수 2개·총량 4800분·남은 시계 비공개를 확인했다. 설정 에셋은 총수명 30h·기본 연출 시간을 유지한다.

Test Runner 종료 후 임시 `PresentationMouse`만 남아 실제 키보드 이벤트와 형식이 충돌하는 Editor 입력 오류도 발견했다. 도메인 재로드만으로 복구되지 않아 같은 프로젝트의 Editor를 다시 열었고, native Keyboard·Mouse·Touchscreen·Pen 복구를 확인했다. 이후 기본 설정의 메인 Game View에서 실제 마우스로 위치 1 선택 → 비공개 강조/행동 UI → 배급 클릭 → 같은 일반 1h 시계 자동 공개·00:20 지급 → AI 일반 9h 배급·03:00 지급까지 검증했다. 최종 나 20:20·상대 23:00·Bank 16:40, 총 3600분이며 Console 오류 0건이다. 테스트용 장치를 프로젝트 코드에 추가하거나 입력 설정을 바꾸지 않았다.

이번 v0.4/v0.5 작업 시작 시점 대비 Table·Counter·Prefabs·Art의 문서를 제외한 **330개 파일 해시가 동일**하다. 메인 Camera·Look·Focus 직렬화 설정도 동일하다. 테스트가 바꾼 Enter Play Mode 옵션·Run In Background와 동적 폰트 캐시는 복원했다. 최종 Editor는 Play를 종료하고 `TimeTheftGame`을 연 상태이며 씬은 저장되지 않은 변경이 없다.

실행 식별자: EditMode `c4746d2efc9040afbb8ef290b539ad26`; 전체 PlayMode `1abf46ba7f784bedb14c07ac9e82d730`, `668d052f086a4b77aeec5e778e3d3c38`(각 40/41, 메인 8/8 포함); Study Focus 단독 통과 `02816464ee774bd5b7c564430c65629d`. 초기 전체 실행 1회는 Test Runner 일반 오류로 중단됐으며 위 결과는 재실행으로 41개가 모두 완료된 기록이다.

### 마일스톤 Git 마무리 검증 (2026-10-25)

- 시작 브랜치는 `main`, HEAD는 `052fa50`이며 스테이징 없이 76개 경로에 변경·추가·삭제가 있었다. 원격 `main`도 읽기 전용 `ls-remote`로 같은 커밋임을 확인했다. 미커밋 상태였던 5개 시계 기반 전환·Legacy 분리까지 포함해야 현행 v0.4/v0.5 실행과 보존 씬의 참조가 완결된다.
- 현행 정산·선택 순서·AI·5개 시계 진행·메인 바인딩·테스트를 검토했다. Legacy C# 12개는 모델 이름/네임스페이스 분리와 필요한 참조 변경이며 기존 `.meta` GUID를 보존한다. GUID 중복·신규 에셋의 `.meta` 누락은 없다. 테이블·계수기·카메라·공통 Focus·다른 4개 씬·기존 Clock Prefab과 제작 원본에 기능 변경은 없다.
- **EditMode 75/75 통과**(2.34초, `21da28d95a054fbd88a342d3fdbeda07`), **전체 PlayMode 41/41 통과**(166.43초, `dfcaeffbdb204f39b88a3b062e0ca761`, 메인 8/8 포함). 제외/삭제 0개다. PlayMode 시작 뒤 Unity Game View를 전면에 두었으며 전체 실행 진행 기록에서도 Editor 포커스가 유지됐다. 이 환경에서는 기존 Focus 실패가 재현되지 않았다.
- 최종 Console 오류·경고 0건. Play를 종료하고 `TimeTheftGame`으로 복귀했으며 씬의 미저장 변경은 없다. native 키보드·마우스 등 실제 입력 장치도 유지됐다. 테스트가 변경한 Enter Play Mode 옵션과 동적 폰트 캐시, 자동 생성 기본 SceneTemplate 설정만 복원/정리했다. 무시되는 기존 임시 테스트 씬은 참조·빌드 미포함을 확인하고 보존했다. 새 문서·임시 보고 파일·기획 변경은 없다.
- 커밋 범위는 현행 코드·메인 씬·임시 시계 Prefab·설정·회귀 테스트·Legacy 이동 및 `AGENTS.md`·이 문서·계수기 README다. 연결된 한 마일스톤으로 `feat: 분 단위 배급 정산과 시계 선선택 흐름 적용`에 정리한다. 제작 중 아트·Blender 자료·테스트 산출물은 포함하지 않으며 Push는 하지 않는다.

### 검증의 한계

- 독립 Player 빌드·배포는 실행하지 않았다. Editor 컴파일·테스트·기본 속도 실행 완료를 배포 빌드 성공으로 해석하지 않는다.
- 중단된 Test Runner가 남긴 `Assets/InitTestScenea785b778-ae2e-4afe-a129-257bf04747df.unity`가 있다. Git에서 무시되는 테스트 초기화 씬이며 빌드 씬 목록에는 없다. 자동 승인 검토가 씬 에셋 삭제 권한 불명확을 이유로 정리를 거부해 보존했다. 메인 개발 씬으로 사용하지 않는다.
- 화면은 16:9 기준이며 다른 화면비·해상도·최종 모델·색 구별 접근성·초회 사용자 이해도는 별도 검증이다. 숫자 외에 일반/빨강 글자를 함께 표시하지만 접근성 완성을 의미하지 않는다.
- 과거 자동 시계 방식의 기본 속도 15턴 기록은 현재 밸런스의 근거가 아니다. 현행 완주는 위 PlayMode에서 실제 포인터로 별도 검증한다.
- 원형 표시의 셔플/개폐/폐기는 기능 검증용이다. 최종 3D 개폐·손 동작·서사·사운드·VFX가 아니다.
- 초기 수명·숫자 분포·선공·묶음 선공 교체·AI 성향·턴 길이·배급 반복과 Bank 소진의 밸런스는 미확정이다. Bank=0 시 배급 비활성화, 아이템 효과/시점·메모지는 별도 기획 사항이며 이번에 추가하지 않았다.

## 7. 다음 작업과 최종 모델 연결

1. **밸런스/UX 플레이 테스트:** 현행 씬 하나에서 징수/배급 선택 이유와 실제 이전액 이해도, Bank=0 빈도, 게임 길이, 공개 관찰 시간, 선공 영향부터 확인한다. 규칙 변경은 Notion과 대조해 결정한다.
2. **회중시계 제작 완료 후 통합:** 새 `ClockToken.prefab` 또는 그 대체 Prefab의 `Visual` 아래에 완성 모델을 넣고 `ClockTokenView`의 Face/Lid/Dial/Number/Kind 표시 참조와 SelectionTarget을 연결한다. 루트 Collider·클릭 전용 FocusTarget 및 물리 위치 ID를 유지한다. 현 컴포넌트는 SpriteRenderer 문자판 색을 쓰므로 3D 문자판 재질을 쓸 때는 이 표시 코드만 조정한다. `Show(ClockState)`의 공개 경계와 `TimeTheftGameView.clocks` 5개 계약을 유지한다. Match나 턴 로직을 모델에 넣지 않는다.
3. **최종 배치/연출:** 완성 크기로 5개 배치를 재검토하고 개폐·셔플·사용 제거/폐기·상대 행동·수치 이전 연출을 개선한다. 기존 테이블·계수기·카메라 설정을 변경하려면 작업 목적과 영향부터 확인한다.
4. **후속 기획:** 직접 쓰는 메모지·아이템은 새 턴 기획이 정해진 뒤 별도 구현한다. 구형 아이템 지급/예약 시스템을 자동 이식하지 않는다.
5. **배포 검증:** 대상 플랫폼 결정 후 독립 Player 빌드·입력·화면비를 확인한다.

재개 시 먼저 `AGENTS.md`와 이 문서를 읽고 실제 Git 변경을 확인한다. 최신 규칙은 Notion을 다시 확인한다. 새 씬을 만들거나 `TabletopGame`을 현행 게임으로 착각하지 않는다. v0.4/v0.5 마일스톤의 Git 정리 범위와 검증은 위 마무리 기록을 기준으로 한다.
