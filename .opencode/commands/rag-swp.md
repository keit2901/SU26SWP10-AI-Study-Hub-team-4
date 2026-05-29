---
description: RAG/backend workflow for AI Study Hub
---

Work on AI Study Hub RAG/backend features safely.

Before editing:
1. Read `AGENTS.md`.
2. Read relevant handoff/plan files under `D:\FPT\summer2026\SWP391\previous_session\`, especially RAG/document plans if the task touches ingestion, embeddings, search, or AI chat.
3. Verify current git status.

Safety:
- Do not print or commit secrets.
- Do not run destructive DB/storage commands without explicit user confirmation.
- Preserve API contracts unless the user approves changes.
- Log migrations, package/config changes, and side-effect commands in the live session log.

After editing:
- Run `dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo`.
- Run relevant tests, preferably `dotnet test "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo --no-build` after successful build.
- Summarize verification and any DB/runtime prerequisites.

User request: `$ARGUMENTS`
