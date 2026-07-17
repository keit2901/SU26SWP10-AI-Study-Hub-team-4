# Startup Keywords — One Command Continue Rules

## Purpose

This file makes one-command project resume work reliably across new AI Agent sessions.

The key command is:

```text
tiếp tục SWP
```

This command means:

```text
Do not rediscover the project from zero.
Load the saved project context.
Read the latest session/handoff.
Verify repo state.
Continue the unfinished task.
```

---

## Mandatory Commands

| User says | Meaning | Mode |
|---|---|---|
| `cập nhật SWP` | Refresh project state only | Read-only |
| `tiếp tục SWP` | Resume unfinished work | Continue work |
| `tiếp tục SWP nhưng chỉ phân tích` | Resume context but do not edit | Analysis only |
| `tiếp tục SWP phần <task>` | Resume specific task | Continue focused task |

---

# Rule 1 — `cập nhật SWP`

When the user says:

```text
cập nhật SWP
```

You MUST do this exact workflow:

1. Read this startup keyword file.
2. Read the main SWP project skill.
3. Read the current session file if it exists.
4. If no current session exists, read the newest handoff/session file.
5. Verify repo status with read-only commands only:
   - `pwd`
   - `git branch --show-current`
   - `git status --short --branch --untracked-files=all`
   - `git remote -v`
6. Summarize:
   - project
   - repo
   - branch
   - working tree status
   - latest session
   - last completed work
   - current unfinished task
   - next recommended actions
7. Do NOT modify code.
8. Do NOT run build/test/docker/migration.
9. Do NOT pull/merge/push/rebase/reset.
10. Do NOT ask the user to resend project context unless startup files are missing.

---

# Rule 2 — `tiếp tục SWP`

When the user says:

```text
tiếp tục SWP
```

You MUST do this exact workflow:

1. First run the `cập nhật SWP` workflow internally.
2. Identify the latest unfinished task from:
   - current session file
   - latest handoff file
   - latest "next recommended step"
   - unfinished checklist
   - user’s current instruction
3. Load only the relevant skill/docs for that task.
4. Produce a short Continue Summary.
5. Continue the task if the next step is safe.
6. Ask confirmation only if the next action is risky.

You MUST NOT stop after only reading files unless:
- latest task is ambiguous,
- git state conflicts with handoff,
- required files are missing,
- uncommitted changes may be overwritten,
- next action is risky.

---

## Continue Summary Format

Before doing work, output:

```text
## Continue Summary

Project:
Repo:
Branch:
Working tree:
Latest session/handoff:
Last completed:
Unfinished task:
Next action:
Risk level:
Will edit files now: Yes/No
Need confirmation: Yes/No
```

If `Need confirmation: No`, continue immediately.

---

## Safe Actions That Can Continue Automatically

These are safe to continue after the summary:

```text
- read relevant files
- inspect code
- analyze current implementation
- continue documentation
- continue checklist/test case writing
- continue a small planned code change
- continue local non-destructive implementation
```

---

## Risky Actions Requiring Confirmation

Ask before:

```text
- git pull
- git merge
- git rebase
- git reset
- git clean
- git push
- creating PR
- database migration
- deleting data/files
- auth/permission breaking change
- API breaking change
- large refactor
- starting/stopping Docker if not requested
- deployment
```

---

## What “tiếp tục SWP” Does NOT Mean

It does NOT mean:

```text
- Ask the user what the project is
- Re-scan the whole repo from zero
- Only survey and then stop
- Use every skill
- Ignore handoff/current session
- Run risky commands automatically
```

It means:

```text
Resume the latest unfinished work from saved context.
```

---

## If No Unfinished Task Is Found

If there is no clear unfinished task, output:

```text
Không tìm thấy task đang dở rõ ràng.

Tôi tìm thấy các hướng có thể tiếp tục:
1.
2.
3.

Bạn muốn tôi tiếp tục hướng nào?
```

Do not invent a task.
