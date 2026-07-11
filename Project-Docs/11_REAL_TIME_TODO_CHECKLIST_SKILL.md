# Real-Time Todo Checklist Management Skill

---

## SWP391 — List làm việc vs List Todo

### Cách dùng

| | List làm việc | List Todo |
|---|-------------|-----------|
| **Dùng khi** | Tổng quan toàn bộ admin tabs | Đang làm 1 tab cụ thể |
| **Cập nhật** | Chỉ tick ✅ khi tab hoàn thành 100% | Real-time liên tục từng bước |
| **Icon** | ✅ Done, 🔄 In Progress, ⏳ Ready | ✅ Done, 🔄 In Progress, ⏳ Pending |
| **File** | `_CURRENT_TODO.md` (phần List làm việc) | `_CURRENT_TODO.md` (phần Live Todo) |

### Format List làm việc
```markdown
# List làm việc — Admin Sidebar
| ID | Tab | Status |
| ADM-01 | Dashboard | 🔄 In Progress |
| ADM-02 | Users & Permissions | ⏳ Ready |
```

### Format Live Todo
```markdown
# Live Todo — ADM-01: Dashboard
| ID | Task | Status |
| DS-01 | Pull + tạo branch | ✅ Done |
| DS-02 | Wire KPIs | 🔄 In Progress |
```

---

## 1. Purpose

This skill defines how an AI Agent should create, maintain, and update a live Todo checklist while working on a project.

This is not just a static checklist. The Todo list must work like a real-time task board during the session:

```text
- What is planned
- What is being worked on now
- What is blocked
- What is ready for testing
- What is done
- What evidence proves it is done
- What should be done next
```

Use this skill for software project work, AI coding agent sessions, local demo preparation, testing, debugging, documentation, and project defense.

---

## 2. Core Rule

The Todo checklist must be updated during work, not only at the end.

The AI Agent must update the Todo state when:

```text
- A task starts
- A task is split
- A task is blocked
- A task is completed
- A test is run
- A test fails
- A test passes
- A new issue is discovered
- Scope changes
- User changes priority
- Work is handed off to the next session
```

The Todo checklist should show the real current state of the work.

---

## 3. Relationship With Other Skills

This skill supports other project skills. It should not replace them.

Works with:

```text
WORKFLOW_GUIDE_PROFESSIONAL.md
skill2.md
skill3_role_based_system_criteria_comparison.md
09_EXTERNAL_TEST_TOOLS_SKILL.md
02_DEMO_READINESS_QUALITY_GATE.md
10_SKILL_ORGANIZATION_AND_LOAD_ORDER.md
ONE_COMMAND_CONTINUE_WORK_PROTOCOL.md
ONE_COMMAND_PROJECT_STARTUP_PROTOCOL.md
```

Use this skill whenever work spans multiple steps or sessions.

---

## 4. When to Use

Use this skill when the user says:

```text
- Làm Todo checklist
- Cập nhật Todo real-time
- Vừa làm vừa cập nhật checklist
- Theo dõi tiến độ giúp tôi
- Tạo task board
- Cập nhật trạng thái từng task
- Đánh dấu task nào done/pending/blocked
- Làm tiếp theo Todo
- Resume từ Todo
```

Also use it automatically when:

```text
- The task has more than 3 steps
- The work spans multiple files
- The work may continue across sessions
- The task involves testing and evidence
- The task is for demo/defense readiness
- The task has multiple roles/modules
```

---

## 5. When Not to Use

Do not create a full Todo board for:

```text
- A one-line answer
- A tiny fix with one obvious step
- A pure translation
- A quick explanation
```

If the user explicitly asks for live Todo tracking, always use this skill.

---

## 6. Todo Status Values

Use only these statuses:

```text
Backlog
Ready
In Progress
Blocked
Review
Testing
Done
Skipped
Cancelled
```

| Status | Meaning |
|---|---|
| Backlog | Valid task, not prioritized or not ready |
| Ready | Clear enough to start |
| In Progress | AI is currently working on it |
| Blocked | Cannot continue because of missing info, dependency, service, conflict, or decision |
| Review | Work is done but not verified yet |
| Testing | Needs verification or tests are running |
| Done | Completed with evidence |
| Skipped | Intentionally not done now, with reason |
| Cancelled | No longer needed |

Important:

```text
Do not mark Done without evidence.
```

If no test/evidence exists, use `Review` or `Testing`, not `Done`.

---

## 7. Priority Values

Use only:

```text
Critical
High
Medium
Low
```

Priority guide:

```text
Critical:
- Main demo flow broken
- Security/permission hole
- Data loss risk
- Build cannot run
- Login/main feature cannot work

High:
- Required feature missing
- UI cannot connect to backend
- Important role/business logic missing
- Important test failing

Medium:
- Edge case missing
- Missing evidence
- Missing test case
- Inconsistent response/UX

Low:
- Minor style
- Minor documentation
- Nice-to-have improvement
```

---

## 8. Todo Type Values

Use these types:

```text
Feature
Bug
Security
Backend
Frontend
API
Database
Testing
Documentation
AI/RAG
Refactor
DevOps
Demo
Research
Decision
```

---

## 9. Standard Live Todo Table

Use this as the default Todo format.

| ID | Task | Type | Source | Priority | Status | Area | Evidence | Next Action |
|---|---|---|---|---|---|---|---|---|
| TODO-001 |  |  |  |  | Ready |  |  |  |

---

## 10. Extended Todo Table

Use this for larger projects.

| ID | Task | Type | Source | Priority | Status | Area | Depends On | Acceptance Criteria | Evidence | Next Action | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|

---

## 11. Todo ID Rules

Use stable IDs.

Recommended:

```text
TODO-001
TODO-002
TODO-003
```

Category-specific examples:

```text
BACKEND-001
FRONTEND-001
SECURITY-001
TEST-001
DEMO-001
RAG-001
```

Rules:

```text
- Do not reuse IDs.
- Do not randomly renumber tasks.
- If a task is split, keep the parent and add child IDs.
```

Example:

```text
TODO-004 — Admin Backend
TODO-004.1 — Add search API
TODO-004.2 — Add status filter
TODO-004.3 — Add lock/unlock permission check
```

---

## 12. Live Update Rules

## 12.1. Before Starting Work

Before implementation, create or load the Todo board.

Output:

```text
## Live Todo — Before Work

| ID | Task | Priority | Status | Next Action |
```

Mark the first selected task as `In Progress`.

## 12.2. While Working

When progress happens, update the board.

Examples:

```text
TODO-001: Ready -> In Progress
TODO-001: In Progress -> Testing
TODO-001: Testing -> Done
TODO-002: Ready -> Blocked
TODO-003: Backlog -> Ready
```

Use short update format:

```text
Todo update:
- TODO-001 moved to In Progress.
- TODO-002 added as Blocked because API contract is unclear.
```

## 12.3. When a New Issue Is Found

If new work appears during coding/testing, add it as a Todo item.

Example:

```text
New Todo added:
- TODO-006: Fix backend response mismatch for Admin user table.
  Source: API inspection
  Priority: High
  Status: Ready
```

## 12.4. When a Task Is Blocked

Blocked tasks must include a reason and next unblock action.

Example:

```text
TODO-004 moved to Blocked.
Reason: Admin API endpoint depends on missing role definition.
Unblock action: Confirm allowed Admin/Moderator permissions.
```

## 12.5. When a Task Is Done

Done tasks must include evidence.

Example:

```text
TODO-003 moved to Done.
Evidence:
- File changed: Controllers/AdminUsersController.cs
- Test: Manual API test returned 403 for normal user
- Notes: Backend now blocks non-admin access
```

---

## 13. Real-Time Update Frequency

The AI Agent should update the Todo board at these checkpoints:

```text
1. After initial task analysis
2. Before modifying files
3. After completing each meaningful subtask
4. After running tests/checks
5. When blocked
6. Before handoff
```

Avoid updating after every tiny line of code. Update after meaningful work units.

---

## 14. Todo Board Storage

If the project supports file writing, store the live Todo board in a file.

Recommended names:

```text
TODO.md
_CURRENT_TODO.md
SESSION_TODO.md
docs/TODO.md
previous_session/_CURRENT_TODO.md
```

For session-based projects, recommended:

```text
previous_session/_CURRENT_TODO.md
```

If the project already has a session/handoff system, put the Todo file near session files.

---

## 15. Todo File Template

```markdown
# Current Todo Board

## Project

Project:
Branch:
Last updated:
Session:

## Summary

| Status | Count |
|---|---:|
| Backlog |  |
| Ready |  |
| In Progress |  |
| Blocked |  |
| Review |  |
| Testing |  |
| Done |  |

## Todo Board

| ID | Task | Type | Source | Priority | Status | Area | Evidence | Next Action |
|---|---|---|---|---|---|---|---|---|

## Blockers

| ID | Blocker | Impact | Needed Decision/Action |
|---|---|---|---|

## Recently Completed

| ID | Task | Evidence |
|---|---|---|

## Next Recommended Action

```text
...
```
```

---

## 16. Todo Creation Workflow

When creating a Todo checklist:

```text
1. Identify source:
   - user request
   - handoff
   - current session
   - business rules
   - role comparison
   - bug report
   - test result
   - UI/backend mismatch
2. Extract raw tasks.
3. Remove duplicates.
4. Split large tasks.
5. Add priority.
6. Add status.
7. Add area.
8. Add acceptance criteria if important.
9. Add next action.
10. Save/output the Todo board.
```

---

## 17. Todo Update Workflow

When updating Todo during work:

```text
1. Load existing Todo board if available.
2. Keep existing IDs stable.
3. Update status of current task.
4. Add newly discovered tasks.
5. Add evidence to completed tasks.
6. Move verified tasks to Done.
7. Move unverified completed tasks to Review or Testing.
8. Update blockers.
9. Update next recommended action.
10. Save/output updated Todo board.
```

---

## 18. Todo From Handoff

When starting from a handoff file:

```text
1. Extract completed work.
2. Extract unfinished work.
3. Extract blockers.
4. Extract next recommended steps.
5. Convert unfinished work into Todo items.
6. Mark current next task as Ready or In Progress.
7. Preserve evidence from the handoff.
```

---

## 19. Todo From Test Results

When tests are run:

```text
PASS -> attach as evidence to related Todo.
FAIL -> create or update Bug/Testing Todo.
BLOCKED -> create Blocked Todo with reason.
NOT RUN -> create Testing Todo if required.
```

Example:

```text
Test result:
- Admin API permission test failed.

Todo update:
- Added TEST-004: Fix Admin API permission test failure.
  Priority: Critical
  Status: Ready
  Evidence: test output path
```

---

## 20. Todo From Role/System Comparison

When role comparison finds gaps:

```text
Critical/High gaps -> Ready Todo
Medium gaps -> Backlog or Ready
Low gaps -> Backlog
Unclear items -> Decision or Research Todo
```

---

## 21. Todo From External Test Tools

When using external tools:

```text
- Failed ZAP issue -> Security Todo
- Gitleaks finding -> Critical Security Todo
- Newman failed request -> API Todo
- Playwright failed flow -> UI/E2E Todo
- Lighthouse major issue -> Frontend/Accessibility Todo
- k6 threshold fail -> Performance Todo
```

Each Todo must include:

```text
Tool:
Report path:
Evidence:
Recommended fix:
```

---

## 22. WIP Limit

To prevent messy work, use a WIP limit.

Recommended:

```text
Maximum In Progress tasks: 1-2
```

If user asks to work on many things, keep only the current task In Progress and keep the rest Ready or Backlog.

---

## 23. Next Action Rule

Every non-Done Todo must have a next action.

Bad:

```text
TODO-005: Admin backend missing.
Next Action: ...
```

Good:

```text
TODO-005: Admin backend missing.
Next Action: Inspect AdminUsersController and Users.razor API calls.
```

---

## 24. Acceptance Criteria Rule

Important tasks must have acceptance criteria.

Example:

```text
Task: Implement Admin user lock/unlock backend.

Acceptance Criteria:
- Admin can lock active normal user.
- Admin can unlock locked normal user.
- Normal user cannot call the endpoint.
- Admin cannot lock Super Admin.
- Response matches UI expectation.
- Manual/API test evidence exists.
```

---

## 25. Live Todo Output During Work

Use this concise format during active work:

```text
## Live Todo Update

| ID | Task | Priority | Status | Next Action |
|---|---|---|---|---|
| TODO-001 | ... | High | In Progress | ... |
| TODO-002 | ... | Medium | Ready | ... |
| TODO-003 | ... | High | Blocked | ... |
```

Then continue working.

---

## 26. End-of-Session Todo Handoff

At the end of a session, output:

```text
## Todo Handoff

Completed:
- TODO-001 — evidence:
- TODO-002 — evidence:

Still In Progress:
- TODO-003 — current state:
- Next action:

Blocked:
- TODO-004 — reason:
- Needed decision:

Ready Next:
- TODO-005
- TODO-006

Recommended next command:
tiếp tục SWP
```

This allows the next session to resume from Todo, not from memory.

---

## 27. One-Command Continue Integration

When user says:

```text
tiếp tục SWP
```

The AI Agent should also look for:

```text
_CURRENT_TODO.md
TODO.md
SESSION_TODO.md
```

If found:

```text
1. Load Todo board.
2. Find In Progress task.
3. If none, find highest priority Ready task.
4. Continue that task.
```

Priority for resume:

```text
1. User’s current instruction
2. In Progress Todo
3. Blocked Todo if user provided unblock info
4. Critical Ready Todo
5. High Ready Todo
6. Latest handoff next action
```

---

## 28. Ready-to-Use Prompt

Use this prompt when asking AI to manage Todo real-time.

```text
Create and maintain a live Todo checklist while working.

Rules:
- Do not make a vague checklist.
- Every Todo must have ID, task, type, source, priority, status, area, evidence, and next action.
- Keep IDs stable.
- Update Todo status during work, not only at the end.
- Use statuses: Backlog, Ready, In Progress, Blocked, Review, Testing, Done, Skipped, Cancelled.
- Do not mark Done without evidence.
- Keep only 1-2 tasks In Progress.
- Add new Todo items when new issues are discovered.
- At the end, produce a Todo Handoff so the next session can resume.

Current task:
[PASTE TASK HERE]
```

---

## 29. Short Prompt

```text
Vừa làm vừa cập nhật live Todo. Mỗi task phải có ID, priority, status, evidence và next action. Khi bắt đầu task thì chuyển In Progress, khi xong nhưng chưa test thì Review/Testing, khi có evidence thì Done. Nếu phát sinh việc mới thì thêm Todo mới. Cuối session tạo Todo Handoff để session sau tiếp tục được.
```

---

## 30. Final Rule

A Todo checklist is useful only if it reflects the real current state.

If the Todo board is not updated while work changes, it is not a live Todo board.
