# Mars Colony — прототип

Играбельный веб-срез казуального сити-билдера. Спецификации — в `../wiki/saas/projects/mars-colony/`, техническая спека сборки — `spec-prototype-build.md`.

## Правило проверки перед сдачей

**Прежде чем сказать «готово», опиши, чем это проверишь, и запусти проверку.** Не «я реализовал», а «я реализовал, прогнал вот это, вот результат».

```
npm run check   # формат, типы, тесты с порогом покрытия
npm run e2e     # браузерная проверка, если трогал интерфейс
```

Модульные тесты не рендерят. Однажды приложение не отрисовывалось вообще при ста сорока зеленых тестах — бесконечный цикл перерисовки. Поэтому любая правка интерфейса проверяется браузером, а не рассуждением.

## Источник истины

Числа игры живут в `src/domain/config/` и приходят туда из `../wiki/saas/projects/mars-colony/mars-colony-frame.md`. **Противоречие каркасу — баг кода, а не каркаса.** Если для прохождения теста хочется поменять число в конфиге, остановись: почти наверняка неверен код.

Имена параметров задаются конфиг-таблицами ТЗ. Тест `spec-drift.test.ts` падает, если параметр из спеки не найден в коде и не записан в список отложенного с причиной.

## Архитектура

Доменный слой не знает об интерфейсе. `src/state/` — тонкая обертка над доменом, `src/ui/` только показывает, `src/sim/` вызывает те же доменные функции, что и игра.

Ни одного игрового числа вне `domain/config`. Появилось число в JSX — конфиг перестал быть источником истины.

## Гейт

Хук на коммит прогоняет формат, типы и тесты. Красное не коммитится — это механика, а не рекомендация.

## Язык

Комментарии и сообщения коммитов по-русски. Буква «ё» не используется.


<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:46cd31e7 -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

**Architecture in one line:** issues live in a local Dolt DB; sync uses `refs/dolt/data` on your git remote; `.beads/issues.jsonl` is a passive export. See https://github.com/gastownhall/beads/blob/main/docs/core-concepts/sync-concepts.md for details and anti-patterns.

## Agent Context Profiles

The managed Beads block is task-tracking guidance, not permission to override repository, user, or orchestrator instructions.

- **Conservative (default)**: Use `bd` for task tracking. Do not run git commits, git pushes, or Dolt remote sync unless explicitly asked. At handoff, report changed files, validation, and suggested next commands.
- **Minimal**: Keep tool instruction files as pointers to `bd prime`; use the same conservative git policy unless active instructions say otherwise.
- **Team-maintainer**: Only when the repository explicitly opts in, agents may close beads, run quality gates, commit, and push as part of session close. A current "do not commit" or "do not push" instruction still wins.

## Session Completion

This protocol applies when ending a Beads implementation workflow. It is subordinate to explicit user, repository, and orchestrator instructions.

1. **File issues for remaining work** - Create beads for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **Handle git/sync by active profile**:
   ```bash
   # Conservative/minimal/default: report status and proposed commands; wait for approval.
   git status

   # Team-maintainer opt-in only, unless current instructions forbid it:
   git pull --rebase
   bd dolt push
   git push
   git status
   ```
5. **Hand off** - Summarize changes, validation, issue status, and any blocked sync/commit/push step

**Critical rules:**
- Explicit user or orchestrator instructions override this Beads block.
- Do not commit or push without clear authority from the active profile or the current user request.
- If a required sync or push is blocked, stop and report the exact command and error.
<!-- END BEADS INTEGRATION -->

## Память разработки (dev-memory)



- Старт сессии: прочитай BRIEF.md, затем `bd ready`.

- Работай через Beads: берёшь задачу — `bd update <id> --claim`, закончил — закрой; новое всплыло — заведи.

- Конец сессии и перед рискованными шагами — чекпоинт МОЛЧА: обновить BRIEF.md, привести задачи Beads в соответствие с реальностью, `bd export -o .beads/issues.jsonl`, строка в memory/log.md, git commit.

- НЕ сохранять: секреты и ключи API. Remote к git не добавлять никогда.
