---
description: UI polish workflow for current dark forum redesign
---

Work on the AI Study Hub Sprint 2 UI redesign incrementally.

Context to preserve:
- Dark premium purple/cyan/glass visual system.
- Orb/glow motif.
- FuOverflow/forum-like community dashboard layout for Home.
- Existing navigation/API/backend contracts.

Before editing:
1. Read `AGENTS.md`.
2. Read the latest UI handoff: `D:\FPT\summer2026\SWP391\previous_session\17_Session_2026-05-27_Sprint2_UI_Redesign_Forum_Handoff.md`.
3. Inspect only the relevant Razor/CSS files for the requested component.

Work rules:
- Do not rewrite the whole app unless explicitly requested.
- Prefer focused edits to 1-2 components.
- Keep MudBlazor providers only at the interactive root as described in the handoff.
- After meaningful changes, update the live session log.

After editing:
- Run `dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo`.
- If appropriate, run `dotnet test "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo --no-build`.
- Summarize changed files and verification.

User request: `$ARGUMENTS`
