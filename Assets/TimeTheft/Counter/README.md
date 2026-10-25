# 통합 시간 계수기 · 비주얼 v1

최근 픽셀 가독성 개선본을 기준으로 정리한 제작 에셋이다. `TimeTheftGame.unity`의 `TimeTheftGameView`에 세 TMP 표시부를 연결해 현행 분 단위 게임의 수명·Time Bank를 표시한다. 이전 유지/교환 게임의 전용 씬·진행·테스트는 제거했다. `CounterStructureStudy`가 비교용으로 참조하는 `TabletopClock.prefab`과 표시 컴포넌트는 보존한다. 현행 코드·씬 진입점과 검증 결과는 루트 [HANDOFF.md](../../../HANDOFF.md)를 참고한다. 계수기 제작 자산과 최종 아트 승인 상태는 그대로 유지한다.

## 기준 에셋과 제작 원본

| 위치 | 역할 |
| --- | --- |
| `IntegratedTimeCounter.prefab` | 상단 Bank / 하단 수명쌍을 결합한 유일한 기준 Prefab |
| `Meshes/` | 기존 UV를 보존한 76개 공유 메시. 모델 내 128개 메시 인스턴스, 7,284삼각형 |
| `Textures/` | 실제 머티리얼이 사용하는 512×512 PNG 4장 |
| `Materials/`, `Shaders/` | 표면용 무광 확산 셰이더와 비트맵 글리프 셰이더. 발광·Bloom 없음 |
| `Layers/` | 아틀라스별 Base / Shading / Wear PNG와 편집 가능한 OpenRaster(.ora) 4개 |
| `Source/CounterModelSource.json` | 최종 계층·배치·메시·UV·렌더러·임시 텍스트의 재생성 원본 |
| `Source/UV/UVManifest.json`, `Templates/` | 332 UV 아일랜드와 원래 배치·템플릿. 6텍셀 패딩 |
| `Source/geometry.json` | 도관의 연속 표면 좌표를 계산하는 원래 분할 정점 원본 |
| `Source/MaterialSource_ImageGen.png`, `GenerationPrompt.txt` | 팔레트 색면 입력 및 생성 출처 |
| `Source/palette.json`, `Palette.png`, `Ownership/` | 재질별 색 단계와 아틀라스 부품 대응 |
| `Fonts/` | Galmuri7 비트맵 라벨 및 자체 픽셀 7세그먼트 숫자. PNG·메트릭·TMP 자산 |
| `Source/Galmuri/` | Galmuri7 원본 BDF와 SIL OFL 1.1 원문·저작권 표시 |
| `Study/` | 관찰 코드, 배치 가이드 재질·주변 임시 글꼴, 최소 비교 이미지 1장 |
| `Editor/` | 모델 원본 Export/Rebuild, 비트맵 Import, 참조·시점 검증 |
| `Tools/Counter/` (저장소 루트) | 표면·글리프 재생성 및 독립적인 PNG/UV 이음새 검사 |

ORA는 Krita/GIMP 등으로 레이어 편집할 수 있다. Aseprite에서는 분리된 PNG 레이어를 가져와 작업할 수 있다. 수작업 수정본을 보존하려면 재생성 전에 편집 원본을 저장한다. 작성기 실행은 해당 PNG/ORA를 다시 출력한다.

## 열기와 수정

`Assets/Scenes/CounterStructureStudy.unity`를 열고 Play한다. F 정면, Q/E 사선, G 게임, T 탑뷰, P 자유 시차, H 안내 토글을 사용한다. 크기·깊이 슬라이더는 실험용이다.

왼쪽 가장자리의 실제 메시 중점으로 본체 중심을 정렬한다. 긴 축은 가장자리와 평행하며 안쪽 이동량은 0.18이다. 기준 루트 위치는 약 `(-3.87, 1.0075, 0)`, yaw 270°, size 0.6, depth 1이다. 게임 카메라와 시차 Inspector 설정은 기존 값을 유지한다.

메인 `TimeTheftGame`에서는 최대 자유 시차의 Bank 글자 잘림을 피하기 위해 인스턴스만 0.12 더 안쪽인 `(-3.75, 1.0075, 0)`에 둔다. yaw 270°·scale 0.6과 길이 방향 중점은 유지한다. 기준 Prefab과 Study 배치는 변경하지 않는다.

모델 수정 뒤 `TimeTheft > Counter` 메뉴에서 제작 원본을 Export한다. JSON의 메시/계층에서 Rebuild하면 현재 자산과 GUID를 재사용한다. 기준 Prefab·메시·재질·폰트와 `.meta`도 함께 보존해야 한다. 이 원본은 이전 A/B/C 생성 체인에 의존하지 않는다. JSON에는 최종 메시와 UV가 포함되며 UV를 새로 펴지 않는다. Unity 내부 instance/file ID 필드는 제작 설정으로 기록하거나 복원하지 않는다.

Python 3 + `Tools/Counter/requirements.txt`의 NumPy/Pillow가 필요하다. 저장소 루트에서 실행한다.

```powershell
python -B Tools/Counter/paint_counter.py
python -B Tools/Counter/build_display.py
python -B Tools/Counter/verify_counter.py --output <프로젝트 밖 검증 폴더>/texture-verification.json
```

표면 작성기는 기존 UV와 원본 색면을 고정 팔레트에 매핑한다. 폰트 작성기는 Galmuri7 원본 글리프와 자체 숫자 형상을 PNG/메트릭으로 출력한다. Unity에서 `Import bitmap display sources`를 실행하면 TMP 자산에 반영한다. 숫자는 문자열로 갱신 가능하며 표면 텍스처에 굽지 않았다. Study 씬은 `00:00 / 30:00 / 30:00` 고정 검증값, 메인 씬은 분 정수 게임 값의 누적 `HH:MM` 표시와 고유 색상을 사용한다. Focus와 Collider는 메인 씬 인스턴스에만 연결한다.

`CounterValidation.Verify(외부폴더)`는 참조·UV·필터·폰트·도관·배치를 확인하고, `CaptureViews(외부폴더)`는 게임/정면/사선/탑뷰/시차 4끝점을 촬영한다. 촬영 후 카메라와 시차 상태를 복구한다. 결과 JSON과 대량 캡처는 저장소에 포함하지 않는다.

## 주요 변경 이력과 선택 이유

| 단계 | 판단 |
| --- | --- |
| A/B/C 구조 비교 | 기본 도형 기반의 서로 다른 외함을 검토. 실제 테이블과 평행한 사이드 배치를 채택 |
| 산업용 단일 후보 | B의 묵직한 외함에 A의 판금·원형 측면 연결부·층진 받침을 결합 |
| 2모듈 재설계 | 큰 화면 하나에 섞이던 정보를 상단 공용 Bank와 하단 대칭 수명쌍으로 구분 |
| 배치·도관 조정 | 실제 가장자리 중점 정렬, 소폭 안쪽 이동, Bank 확대, 메인 도관·보조 케이블 연결 |
| UV·픽셀 시제품 | 공유 UV 메시와 4개 아틀라스 제작. 작은 노이즈와 매끈한 표시부가 인게임에서 약하게 보임 |
| 현재 v1 | 자주 보이는 면의 큰 픽셀 덩어리, 절제된 무광 팔레트, Galmuri7 라벨·픽셀 숫자로 통일 |

과거 후보 Prefab·중간 메시·텍스처·백업 씬·단계별 생성 도구는 현행 참조를 분리한 뒤 제거했다. 이전 상태 전체는 정리 전 ZIP 및 Git bundle로 프로젝트 밖에 보관했다. 저장소에는 [최소 게임 비교 이미지](Study/PixelReadabilityComparison.png) 한 장만 남긴다. 이미지 좌측은 이전 표면/표시부, 우측은 현재 기준본이다.

## 검증과 남은 사항

- 정리 전후 동일 게임 카메라에서 계수기 영역의 픽셀이 동일하다. 기존 모델·UV·색상·조명·배치를 유지한다.
- 제작 원본에서 182개 노드와 76개 메시를 복원하고 다시 Export한 JSON의 바이트가 동일하다. 기존 Prefab 내부 오브젝트 ID와 에셋 GUID를 유지한다. TMP가 자동 생성하는 MeshFilter는 저장될 수 있다.
- 최종 PNG/편집 레이어/글리프·메트릭 20개를 새 작성기로 재생성했을 때 SHA-256이 동일하다.
- 4개 아틀라스는 Point / Mipmap 없음 / 무압축이다. 레이어 합성과 ORA merged PNG 불일치 0, 도관 경계 누락 0, 팔레트 1단계 초과 이음새 차이 0을 확인했다.
- Unity 컴파일, 기존 EditMode 테스트 28개, 실험 씬 Play 및 시차 토글을 확인했다. 최종 참조에 과거 실험 에셋이 없다.
- Study의 기존 배치에는 오른쪽·아래 최대 시차에서 Bank 라벨의 글리프 샘플 모서리 3개가 화면 밖으로 나가는 제한이 남는다. 메인 씬은 위의 안쪽 이동으로 세 숫자와 라벨이 네 시차 끝점에서 화면 안에 들어오도록 조정했다. 작은 사선 라벨의 방향성 앨리어싱은 후속 아트 검토 대상이다.
- `TimeTheftGame`에 데이터 바인딩·정산 표시·Focus·시차를 통합했다. Study 씬과 기준 Prefab은 독립 제작용으로 보존한다. 최종 수작업 표면 마감과 아트 승인은 별도 작업이다.

Galmuri 원본과 파생 비트맵은 포함된 SIL OFL을 유지한다. 원본 출처는 Galmuri 프로젝트(https://github.com/quiple/galmuri)이며, 외부 게임의 표면 텍스처를 복제해 포함하지 않았다.
