# Demo Readiness and Quality Gate

## 1. Purpose

This file defines when a local academic project is ready for defense/demo.

It prevents common defense failures:

```text
- App does not start
- Database is not ready
- Login fails
- Main feature fails
- AI/RAG service is down with no fallback explanation
- Test evidence is missing
- Team cannot explain current status
```

## 2. Readiness Status

Use one of:

```text
READY
READY WITH NOTES
NOT READY
BLOCKED
```

## 3. Pre-Demo Checklist

| ID | Check | Expected | Status | Evidence |
|---|---|---|---|---|
| DEMO-01 | Correct branch checked out | Matches demo/submission branch | Not Checked | |
| DEMO-02 | Repository state known | No unknown accidental changes | Not Checked | |
| DEMO-03 | Required services running | DB, storage, AI/local services ready | Not Checked | |
| DEMO-04 | App starts locally | No startup crash | Not Checked | |
| DEMO-05 | Build succeeds | 0 critical errors | Not Checked | |
| DEMO-06 | Unit/critical tests pass or failures documented | Evidence available | Not Checked | |
| DEMO-07 | Database migration applied | Schema matches app | Not Checked | |
| DEMO-08 | Demo/test account works | Login possible | Not Checked | |
| DEMO-09 | No real secrets shown in docs/demo | Use placeholders | Not Checked | |
| DEMO-10 | Main demo flow works | Core feature succeeds | Not Checked | |

## 4. Feature Quality Gate

For each feature being defended:

| Feature | Requirement Source | Demo Steps | Evidence | Status | Notes |
|---|---|---|---|---|---|
|  |  |  |  | Not Checked |  |

A feature is ready when:

```text
- Requirement is clear.
- UI is accessible.
- Backend/API works.
- Database state is correct.
- Error case is handled.
- Demo steps are documented.
```

## 5. Main Demo Script Template

```text
Demo Flow Name:
Purpose:
Account used:
Precondition:
Steps:
1.
2.
3.
Expected result:
Fallback if failed:
Evidence:
```

## 6. Defense Failure Handling

If something fails during defense:

```text
1. State what failed.
2. Identify whether it is environment-related, service-related, or code-related.
3. Show previous evidence if available.
4. Explain fallback plan.
5. Move to the next prepared demo flow if possible.
```

Useful fallback evidence:

```text
- Test report
- Screenshot
- Log output
- Unit test result
- Database query result
- Previous handoff summary
```

## 7. Quality Gate Decision Table

| Category | Required for READY? | Current Result | Notes |
|---|---:|---|---|
| App starts locally | Yes | Not Checked | |
| Build passes | Yes | Not Checked | |
| Critical tests pass | Yes | Not Checked | |
| Main demo flow works | Yes | Not Checked | |
| Auth/login works | Yes | Not Checked | |
| Role permission safe | Yes | Not Checked | |
| Required services running | Yes | Not Checked | |
| Docs/evidence available | Yes | Not Checked | |
| Non-critical features complete | No | Not Checked | |
| UI polish | No | Not Checked | |

Final status:

```text
READY / READY WITH NOTES / NOT READY / BLOCKED
```

Reason:

```text
...
```

## 8. Final Defense Checklist

```text
- [ ] Project can run locally.
- [ ] Team knows exact startup commands.
- [ ] Required local services are ready.
- [ ] Demo account works.
- [ ] Main demo flow is practiced.
- [ ] Screenshots/evidence are prepared.
- [ ] Test report is available.
- [ ] Known limitations are written down.
- [ ] No real secrets are exposed.
- [ ] Backup plan exists if external/AI service fails.
```
