# TimeTheft

Unity 6 (`6000.0.68f1`)로 만드는 1인 게임잼 프로젝트.

- 시작 씬: `Assets/Scenes/TimeTheftTitle.unity`
- 메인 게임: `Assets/Scenes/TimeTheftGame.unity`
- 개발·검증 씬: `Assets/Scenes/_Development/`
- 작업 지침: [AGENTS.md](AGENTS.md) · [Unity](.docs/harness/UNITY.md) · [Git/Worktree](.docs/harness/GITHUB.md)
- 기획 원문: [Notion 안내](.docs/project/GAME_DESIGN.md) · 구현·검증 기록: [HANDOFF](.docs/project/HANDOFF.md)

```text
.agents/skills/commit/
.docs/{harness,project}/
.vscode/
Assets/
  Animations/
  Art/{Characters,Props,UI,Rendering}/
  Fonts/
  Input/
  Prefabs/
  Scenes/_Development/
  Scripts/{Core,Gameplay,Presentation,Camera,UI,Editor,Development}/
  Settings/
  Tests/{EditMode,PlayMode}/
  TextMesh Pro/
Packages/
ProjectSettings/
Tools/
```

`TextMesh Pro`는 공급 라이브러리 구조와 Resources 경로를 보존한다. 제작 중 원본·이전 모델과 검증 자료는 사용 여부가 불확실하면 해당 오브젝트 아래에 보존한다.
