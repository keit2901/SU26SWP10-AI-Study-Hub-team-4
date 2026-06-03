---
description: Close or pause SWP391 session with handoff discipline
---

Prepare a SWP391 session handoff according to `D:\FPT\summer2026\SWP391\previous_session\rule.md`.

Steps:
1. Read the active live session log under `D:\FPT\summer2026\SWP391\previous_session\`.
2. Verify final state with relevant evidence:
   - `git status --short --branch --untracked-files=all`
   - build/test results if code changed
   - runtime/port state if preview was used
3. Update the live log sections:
   - Status
   - Progress log
   - Files changed
   - Commands run
   - Decisions locked
   - Open questions/risks
   - Next step with paste-and-run instructions
   - Quick Facts
4. If the user requested full close, rename the live log to the next `NN_Session_YYYY-MM-DD_<topic>_Handoff.md`.
5. Do not commit unless the user explicitly asks.

Keep the handoff concise, evidence-based, and free of secrets.

User arguments, if any: `$ARGUMENTS`
