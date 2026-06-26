# AI Study Hub / SWP391 OpenCode Instructions

## Project snapshot
- Repo/worktree commonly used for active integration: `D:\FPT\summer2026\SWP391_parallel\s2_integration`.
- Main solution: `AI_Study_Hub_v2/AI_Study_Hub_v2.sln`.
- App: .NET 8 Blazor/MudBlazor AI Study Hub with document upload, RAG chatbot, profile, documents, and admin areas.
- Current active branch from latest handoff: `sprint2/integration`.

## Required context workflow
- At session start, read the newest relevant handoff under `D:\Project\AI_Study_Hub\SU26SWP10-AI-Study-Hub-team-4\SU26SWP10-AI-Study-Hub-team-4\previous_session\`, plus:
  - `D:\Project\AI_Study_Hub\SU26SWP10-AI-Study-Hub-team-4\SU26SWP10-AI-Study-Hub-team-4\previous_session\rule.md`
  - `D:\Project\AI_Study_Hub\SU26SWP10-AI-Study-Hub-team-4\SU26SWP10-AI-Study-Hub-team-4\previous_session\skill.md`
- Latest loaded handoff as of 2026-06-17: `handoff_backend_2026-06-17.md`.
- Maintain a live session log in `D:\Project\AI_Study_Hub\SU26SWP10-AI-Study-Hub-team-4\SU26SWP10-AI-Study-Hub-team-4\previous_session\` after meaningful actions.
- If `_CURRENT_SESSION.md` is already owned by another active task, create a topic-specific `_CURRENT_SESSION_<TOPIC>.md` instead of overwriting it.
- Do not edit old closed `NN_Session_*_Handoff.md` files. Write corrections in the active live log or a new handoff.

## Safety rules
- Do not print, commit, or copy secrets. Previous chat/config contexts may contain local credentials.
- Do not revert user/uncommitted changes unless explicitly requested.
- Before risky operations (delete, drop DB, force push, destructive migration), log pre-flight and ask the user.
- Only commit, amend, push, or create PRs when explicitly requested.

## UI direction from latest handoff
- Keep dark premium purple/cyan/glass tone.
- Preserve orb/glow visual motif.
- Home should resemble a FuOverflow/forum community dashboard:
  - horizontal top navigation
  - centered fixed-width content
  - announcement/banner card
  - toolbar/community header
  - main discussion/table content on the left
  - right sidebar profile/status/online cards
- Preserve backend/API contracts and existing app functionality.

## Common verification commands
Run from repo root `D:\FPT\summer2026\SWP391_parallel\s2_integration` unless noted.

```powershell
git status --short --branch --untracked-files=all
```

```powershell
dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo
```

```powershell
dotnet test "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo --no-build
```

```powershell
Get-NetTCPConnection -LocalPort 5240 -ErrorAction SilentlyContinue
```

## Current live preview convention
- App preview may run at `http://localhost:5240/`.
- If stopping a prior app, inspect PID/port first and only stop the dev app.
- A prior PID file convention was `C:\Users\pc\AppData\Local\Temp\opencode\aistudy-live\app.pid`.

## Productivity guidance
- Prefer incremental edits to focused files/components.
- For UI work, tune only the relevant Razor/CSS component first.
- For broad discovery, use search before reading large files.
- For multi-step tasks, keep todos current and update the session log after meaningful file edits, commands, decisions, errors, or completed milestones.
