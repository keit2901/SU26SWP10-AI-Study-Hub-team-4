# Skill: One Command Continue Work

## 0. Purpose

This skill forces the AI Agent to resume unfinished work from saved project context when the user says:

```text
tiếp tục SWP
```

This skill exists because a normal instruction like:

```text
Tiếp tục project AI Study Hub
làm backend
```

is too vague and may cause the AI Agent to only survey the code instead of continuing the actual unfinished task.

---

## 1. Trigger

Activate this skill when user says any of:

```text
tiếp tục SWP
tiếp tục project SWP
tiếp tục AI Study Hub
tiếp tục project AI Study Hub
làm tiếp SWP
resume SWP
continue SWP
```

If the user adds a specific task, focus on that task:

```text
tiếp tục SWP phần Admin Backend
tiếp tục SWP phần test
tiếp tục SWP phần role comparison
```

---

## 2. Mandatory Behavior

When this skill is triggered, the AI Agent MUST NOT start by asking the user to explain the project again.

The AI Agent MUST:

```text
1. Load startup keywords.
2. Load main project skill.
3. Load current session if it exists.
4. Otherwise load latest handoff.
5. Verify repo/branch/status using read-only commands.
6. Identify unfinished task.
7. Choose the correct task skill.
8. Continue if safe.
```

---

## 3. Required Read Order

Read in this order:

```text
1. startup-keywords.md
2. main project SKILL.md / skill.md
3. _CURRENT_SESSION.md if exists
4. latest handoff_*.md or SESSION.md if no current session exists
5. WORKFLOW_GUIDE_PROFESSIONAL.md only if coding work is needed
6. skill2.md only if updating existing code
7. skill3_role_based_system_criteria_comparison.md only if roles/permissions are involved
8. COMPREHENSIVE_TEST_GUIDE.md only if testing is involved
9. BUSINESS_RULES.md only if business logic is involved
```

Do not load all documents blindly.

---

## 4. Required Read-Only Checks

Run only safe read-only commands first:

```bash
pwd
git branch --show-current
git status --short --branch --untracked-files=all
git remote -v
```

Do not run build/test/docker/migration/pull/merge/push during the initial resume check.

---

## 5. Continue Summary Required

Output this before continuing:

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
Main skill selected:
Supporting docs:
Will edit files now: Yes/No
Need confirmation: Yes/No
```

If the next step is safe, continue after this summary.

If risky, stop and ask confirmation.

---

## 6. How to Choose the Main Skill

Use only one main task skill.

| Situation | Main Skill |
|---|---|
| Existing UI/backend update | existing-project / skill2 |
| Role/Admin/Mod/User comparison | role-comparison / skill3 |
| External tool testing | external-test-tools |
| Demo readiness | demo-readiness |
| API mismatch | api-contract |
| DB migration | database-migration |
| AI/RAG quality | ai-rag-evaluation |
| Skill organization | skill-organization |

Do not activate all skills.

---

## 7. If User Says Only “làm backend”

If user says:

```text
làm backend
```

while in a continue session, do NOT treat it as a full backend rewrite.

Instead:

```text
1. Use the latest handoff/current task.
2. Determine which backend part was unfinished.
3. If unclear, list backend areas and ask user to choose.
```

Possible backend areas:

```text
- Admin Backend
- Auth
- User Management
- Reports
- Documents
- Dashboard statistics
- Audit log
- API contract
- Role/permission
```

---

## 8. If User Says “làm Admin Backend”

Do not survey forever.

Workflow:

```text
1. Read current Admin UI/backend files.
2. Identify Admin Backend requirements from UI, business rules, and handoff.
3. Make a checklist:
   - already exists
   - missing
   - mismatch
   - risky
4. Pick the first Critical/High item that is safe.
5. Implement it if no confirmation is needed.
6. Report files changed and tests needed.
```

---

## 9. Stop Conditions

Stop and ask if:

```text
- no handoff/current session found
- multiple unfinished tasks are equally likely
- branch does not match handoff
- working tree has unknown changes
- task requires migration
- task requires auth/permission breaking change
- task requires push/merge/rebase/reset
- required API keys/services are missing
```

---

## 10. Final Handoff

At the end of resumed work, output:

```text
## Work Handoff

Task continued:
Files changed:
What was completed:
What remains:
Tests/checks run:
Results:
Risks/blockers:
Next recommended command:
```

Suggested next commands:

```text
tiếp tục SWP
cập nhật SWP
SWP chạy test liên quan
SWP commit + push
```

---

## 11. Final Rule

The command:

```text
tiếp tục SWP
```

means:

```text
Resume the latest unfinished work from saved project context.
```

It does not mean:

```text
Read a few files, survey forever, and stop.
```
