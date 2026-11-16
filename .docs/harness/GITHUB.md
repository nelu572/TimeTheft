# Git 및 Worktree 작업

- 현재 개발의 기본 브랜치는 `main`이다. 작업 전에 현재 브랜치, staged·미커밋·미추적 변경과 관련 Worktree를 확인한다. 기존 사용자 변경과 Stash를 보존한다.
- Worktree는 미커밋 변경과의 충돌을 피하거나 독립된 검증 환경이 필요한 경우에 선택한다. 작업마다 자동으로 생성하지 않는다. Unity 인스턴스 확인은 [Unity 하네스](UNITY.md)를 따른다.
- Worktree 제거 전 다른 채팅이나 Editor가 사용하는지 확인하고, 추적·미추적·무시 파일의 고유 변경과 복구 수단을 확인한다. 일반 `git worktree remove`로 처리할 수 있는 상태에서만 제거한다.
- 커밋은 명시적 요청이 있을 때 [커밋 스킬](../../.agents/skills/commit/SKILL.md)에 따라 수행한다. Push·병합·브랜치 삭제는 해당 작업에 대한 별도 사용자 승인 후 수행한다.
