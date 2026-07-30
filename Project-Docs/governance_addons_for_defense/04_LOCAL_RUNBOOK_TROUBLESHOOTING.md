# Local Runbook and Troubleshooting

## 1. Purpose

This file helps the team recover quickly when the local project fails during development, testing, or defense.

It is not a production incident runbook.  
It is for local demo stability.

Use when:

```text
- App does not start
- Database fails
- Docker service fails
- Login fails
- Upload/ingest fails
- AI/RAG feature fails
- Build/test fails
- Demo environment is unstable
```

## 2. Local Environment Snapshot

Fill this before demo.

```text
Project:
Branch:
Commit:
OS:
Runtime/SDK:
Database:
Local services:
App URL:
Test account:
Required API keys:
```

## 3. Startup Checklist

```text
1. Confirm correct branch.
2. Confirm no unexpected local changes.
3. Start required services.
4. Apply migrations if needed.
5. Build project.
6. Run critical tests.
7. Start app.
8. Login with demo account.
9. Run main demo flow once.
```

## 4. Common Failure Table

| Symptom | Likely Cause | Check | Fix |
|---|---|---|---|
| App does not start | Wrong SDK, missing env, port used | Startup log | Install SDK, set env, change/kill port |
| Database connection fails | DB container not running or wrong connection string | DB health/logs | Start DB, verify config |
| Migration error | Schema mismatch or pending migration | Migration command/log | Apply/rollback migration carefully |
| Login fails | Auth service down, wrong account, inactive user | Auth logs/user table | Start auth service, verify account |
| Upload fails | File invalid, storage issue, size/type limit | Upload response/log | Use valid file, check storage |
| Ingest stuck/failed | AI/embedding service down or extraction failed | Document status/log | Restart service, re-ingest |
| RAG answer poor | Bad chunks, missing document scope, LLM issue | Search result/log | Re-ingest, check sources |
| Tests fail | Broken code or environment dependency | Test output | Fix failing area or document known issue |
| UI blank | Frontend/runtime error | Browser console/server log | Fix component/API error |

## 5. Service Health Checks

Fill with project-specific commands.

```text
Database health:
Command:
Expected:

App health:
Command:
Expected:

AI/Embedding service health:
Command:
Expected:

Storage service health:
Command:
Expected:
```

## 6. Logs to Check

```text
- App/server logs
- Browser console
- Network tab
- Docker/container logs
- Database logs
- Test output
- Migration output
```

## 7. Demo Recovery Plan

If the live demo breaks:

```text
1. Show the exact error briefly.
2. Identify likely layer: UI / backend / database / external service.
3. Use prepared fallback evidence.
4. Explain what the feature normally does.
5. Continue with another prepared flow if possible.
```

Fallback evidence:

```text
- Screenshots
- Previous test reports
- Unit test output
- API response examples
- Database query evidence
- Handoff summary
```

## 8. Troubleshooting Record Template

```text
Issue ID:
Date:
Branch:
Symptom:
Affected feature:
Error message:
Likely cause:
Steps taken:
Final fix:
Evidence:
Prevention:
```

## 9. Post-Demo Notes

After defense/demo, record:

```text
- What worked
- What failed
- Questions from committee
- Bugs found
- Follow-up tasks
- Evidence to update
```
