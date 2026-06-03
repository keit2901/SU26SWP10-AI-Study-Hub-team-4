---
description: Verify SWP391 build/test/runtime state
---

Verify AI Study Hub / SWP391 state without changing source files.

Run from repo root:
1. `git status --short --branch --untracked-files=all`
2. `dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo`
3. If build passes, run `dotnet test "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo --no-build`
4. Check port 5240 with `Get-NetTCPConnection -LocalPort 5240 -ErrorAction SilentlyContinue`

Report concise results with:
- branch and changed/untracked files summary
- build result
- test result
- port 5240 state
- recommended next step

If any command fails, stop and explain the failure before attempting fixes.

User arguments, if any: `$ARGUMENTS`
