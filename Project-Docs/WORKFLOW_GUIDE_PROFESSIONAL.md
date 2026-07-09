# Professional AI Agent Workflow Guide

---

## SWP391 — Quy trình làm việc Admin Backend

> **Branch:** 1 branch duy nhất: `feature/admin-upgrade` từ `main`  
> **Nguyên tắc:** Các phần chạy đồng bộ, không tách lẻ. Code phần sau kế thừa phần trước.  
> **PR:** Xong phần nào PR phần đó (mỗi tab 1 PR riêng)  
> **Todo:** File `previous_session/_CURRENT_TODO.md` — cập nhật real-time  
> **List làm việc:** Chỉ tick ✅ khi tab đó hoàn thành

| # | Bước | Mô tả |
|---|------|-------|
| 0 | **Todo** | Tạo list Todo real-time, cập nhật liên tục khi làm |
| 1 | **Pull** | `git checkout main && git pull origin main` |
| 2 | **Branch** | `git checkout -b feature/admin-upgrade` từ main (dùng chung 1 branch) |
| 3 | **Code** | Backend + UI từng tab, mỗi tab commit nhỏ riêng, các tab chạy đồng bộ với nhau |
| 4 | **Build** | `dotnet build` — 0 errors |
| 5 | **Unit test** | NUnit test + bảng kết quả |
| 6 | **API test** | PowerShell từng bước + bảng evidence |
| 7 | **UI test flow** | Bảng test flow cho user test trên browser |
| 8 | **Push + PR** | Xong tab nào PR tab đó, 1 branch nhiều PR, title + description tiếng Việt có dấu |
| 9 | **Update** | Cập nhật List làm việc khi tab done |
| 10 | **Session** | Cập nhật `_CURRENT_TODO.md` + file Project-Docs mỗi khi làm phần mới |

### Quy tắc
- Chỉ làm khi có lệnh, không tự ý
- 1 branch duy nhất cho tất cả admin tabs
- Các tab code sau kế thừa code trước, chạy đồng bộ
- Mọi mô tả (PR, commit, code, test) đều tiếng Việt có dấu (UTF-8)
- Mỗi khi làm phần mới: cập nhật `_CURRENT_TODO.md` + tất cả file Project-Docs liên quan
- Icon Todo: ✅ done, 🔄 in progress, ⏳ pending

### List làm việc vs List Todo

| | List làm việc | List Todo |
|---|-------------|-----------|
| **Mục đích** | Tổng quan toàn bộ admin tabs | Từng bước nhỏ khi làm 1 tab |
| **Khi update** | Chỉ tick ✅ khi tab hoàn thành 100% (PR merged) | Cập nhật real-time liên tục khi làm |
| **Phạm vi** | 9 tab admin sidebar | 1 tab cụ thể đang làm |
| **File** | `_CURRENT_TODO.md` (phần trên) | `_CURRENT_TODO.md` (phần dưới) |
| **Icon** | ✅ Done / 🔄 In Progress / ⏳ Ready | ✅ Done / 🔄 In Progress / ⏳ Pending |

---

## 1. Purpose

This guide defines a professional workflow for using an AI coding agent with an existing software project.

It is designed for important projects, team projects, academic projects, production-like systems, and long-term codebases.

This guide is project-agnostic. It can be used for many kinds of projects:

```text
- Backend systems
- Frontend applications
- Full-stack applications
- Mobile applications
- Desktop applications
- AI / RAG systems
- Admin dashboards
- E-commerce systems
- SaaS platforms
- Internal tools
- School or capstone projects
- Open-source repositories
```

The AI Agent must not assume the project is any specific system, framework, repository, role structure, branch workflow, or technology stack.

The AI Agent must make decisions based on both:

```text
1. User intent and user-provided requirements
2. Evidence from the actual project
```

This means the user tells the AI Agent what needs to be done, while the existing project tells the AI Agent how it should be done.

---

## 2. Core Philosophy

The AI Agent must behave like a careful professional developer joining an existing project.

The correct mindset is:

```text
Understand the task.
Inspect the project.
Respect the current architecture.
Use the project’s real conventions.
Use the user’s requirements as the task goal.
Define the scope clearly.
Plan before changing risky code.
Implement narrowly.
Test honestly.
Report exactly what changed.
Only commit, push, create PR, or deploy when requested.
```

The AI Agent must not behave like it is generating a new project from scratch unless the user explicitly asks for that.

---

## 3. Smart Source-of-Truth Hierarchy

The AI Agent must not blindly follow only one source.

For each task, the AI Agent must combine user instructions with project evidence.

Use this hierarchy:

```text
Level 1 — Explicit user instruction for the current task
Level 2 — Current project codebase and configuration
Level 3 — Project documentation, README, API docs, comments, diagrams, team workflow docs
Level 4 — Existing tests, build scripts, CI/CD files, logs, and runtime behavior
Level 5 — Standard engineering best practices
```

### 3.1. How to Use the Hierarchy

User decides the goal.

Examples:

```text
- Make this UI work.
- Fix this bug.
- Implement this checklist.
- Compare the system with this rubric.
- Update the backend for this screen.
- Follow this GitHub issue.
```

Project decides the implementation style.

Examples:

```text
- Which framework is used
- Which folder structure is used
- Which API response format is used
- Which auth/role middleware exists
- Which database ORM is used
- Which branch naming convention is used
- Which test/build command is correct
- Which coding style should be followed
```

Best practice fills the gap only when neither the user nor the project provides enough guidance.

Do not override existing project conventions just because a different pattern is usually better.

---

## 4. Conflict Resolution Rules

If user instruction and project evidence conflict, do not silently choose one.

Report the conflict clearly.

Example:

```text
Conflict found:
- User requested endpoint: POST /users/lock
- Current project style uses: PATCH /admin/users/:id/status

Recommendation:
Use the existing project style unless the user explicitly wants to change API conventions.
```

### 4.1. When User Instruction Overrides Project

User instruction overrides project convention when the user explicitly says so.

Example:

```text
User says: "Đổi format response mới theo chuẩn này cho toàn bộ module."
```

Then the AI Agent may change the convention, but must report the impact.

### 4.2. When Project Overrides Assumptions

Project evidence overrides AI assumptions.

Example:

```text
AI assumes project uses JWT.
Project code shows session cookies.
Correct action: follow session cookie auth.
```

### 4.3. When to Stop and Ask

Stop and ask before continuing if conflict affects:

```text
- Authentication
- Authorization
- Payment/order logic
- Database migration
- Data deletion
- Production deployment
- Breaking API contract
- Large refactor
- Team branch workflow
```

---

## 5. Universal Project-Agnostic Rule

The AI Agent must not hard-code or assume:

```text
- Project name
- Repository path
- Repository URL
- Localhost port
- Default branch name
- Feature checklist
- Role list
- Login credentials
- Framework
- Language
- Build command
- Test command
- Deployment command
- Database type
- Docker setup
- Environment variables
- Folder structure
- API response format
- UI framework
- Coding style
- PR workflow
- Team workflow
```

The AI Agent must discover these from:

```text
- User instruction
- README
- package/config files
- source code
- routing files
- CI/CD files
- Docker files
- environment examples
- test files
- project documentation
- existing branch naming
- existing commit/PR style
```

Use placeholders when needed:

```text
<PROJECT_NAME>
<REPO_PATH>
<DEFAULT_BRANCH>
<WORKING_BRANCH>
<FEATURE_NAME>
<TASK_ID>
<LOCALHOST_URL>
<BUILD_COMMAND>
<TEST_COMMAND>
<LINT_COMMAND>
<USER_REQUIREMENTS>
<USER_CHECKLIST>
<TEST_ACCOUNT>
<ENV_FILE>
```

---

## 6. What the User May Provide

The user may provide one or more of:

```text
- Feature checklist
- Bug report
- GitHub issue
- SCRUM ticket
- Assignment rubric
- Teacher feedback
- Client feedback
- Team leader instruction
- UI screenshot
- Figma design
- Frontend code
- Backend code
- API documentation
- Database schema
- Error logs
- Failing test output
- Pull request comments
- Security checklist
- Performance issue
- Accessibility requirement
- Existing workflow guide
```

The AI Agent must transform the provided material into a clear working checklist, then adapt the implementation to the real project.

---

## 7. What Must Be Generalized

Any project-specific instruction must be generalized unless the current project or user explicitly provides it.

### 7.1. Checklist / Requirement List

Bad:

```text
Always follow this fixed Admin checklist.
```

Good:

```text
Follow the checklist provided by the user or discovered from the project requirement files.
If the user provides no checklist, extract one from the task description and current project behavior.
```

### 7.2. Project Name

Bad:

```text
This workflow is for AI Study Hub.
```

Good:

```text
This workflow applies to the current project.
Detect the project name from README, repository metadata, package files, documentation, or user instruction.
```

### 7.3. Technology Stack

Bad:

```text
Always run dotnet build.
Always use React.
Always use Supabase.
```

Good:

```text
Detect the project stack from repository files before choosing commands or implementation style.
```

Stack discovery examples:

```text
package.json                  -> Node.js / frontend / backend scripts
pnpm-lock.yaml                -> pnpm likely used
yarn.lock                     -> yarn likely used
package-lock.json             -> npm likely used
pom.xml                       -> Maven / Java
build.gradle                  -> Gradle / Java or Kotlin
*.sln / *.csproj              -> .NET
requirements.txt              -> Python
pyproject.toml                -> Python
go.mod                        -> Go
Cargo.toml                    -> Rust
composer.json                 -> PHP
Dockerfile / docker-compose   -> container workflow
```

### 7.4. Branch Name

Bad:

```text
Always pull main.
```

Good:

```text
Use the target branch specified by the user, project documentation, or repository default.
If unknown, inspect branches and ask before risky work.
```

Common possibilities:

```text
main
master
develop
dev
staging
release/*
feature/*
```

### 7.5. Localhost / Port

Bad:

```text
Always use http://localhost:5240.
```

Good:

```text
Use the dev server URL from README, startup logs, environment config, package scripts, launch settings, Docker compose, or user instruction.
```

### 7.6. Credentials

Bad:

```text
Store real admin email and password in the workflow.
```

Good:

```text
Never store real credentials in the workflow guide.
Use test credentials only if the project provides safe seed accounts.
Otherwise use placeholders.
```

Placeholders:

```text
<TEST_USER_EMAIL>
<TEST_USER_PASSWORD>
<ADMIN_TEST_ACCOUNT>
<MODERATOR_TEST_ACCOUNT>
```

### 7.7. Repository Path

Bad:

```text
Repo path: D:\Github\SomeSpecificProject
```

Good:

```text
Use the repository path provided by the user or the current working directory.
Never assume a fixed local path.
```

### 7.8. Tool Names

Bad:

```text
Use @observer and @explorer.
```

Good:

```text
Use the available tools in the current AI environment to inspect files, images, logs, repository structure, and code.
Do not require tool names that may not exist in another environment.
```

### 7.9. Build / Test / Run Commands

Bad:

```text
Always run dotnet build and dotnet test.
```

Good:

```text
Detect the correct commands from project scripts and documentation.
If scripts exist, prefer project-defined scripts over invented commands.
```

Examples:

```text
npm run build
npm test
pnpm test
yarn test
mvn test
gradle test
dotnet build
dotnet test
pytest
go test ./...
cargo test
```

### 7.10. Commit / Push / PR Flow

Bad:

```text
Always commit and push after each task.
```

Good:

```text
Prepare a commit message and PR description when useful.
Only commit, push, or create PR when the user explicitly asks.
Follow the team’s workflow if provided.
```

### 7.11. Deployment

Bad:

```text
Deploy after finishing.
```

Good:

```text
Do not deploy unless the user explicitly requests deployment and confirms the target environment.
For production/staging deployment, require extra confirmation and safety checks.
```

---

## 8. Standard Workflow Overview

Every task should follow this workflow:

```text
Phase 0: Understand task and inputs
Phase 1: Check repository safety
Phase 2: Sync latest code if requested or required
Phase 3: Confirm or create working branch
Phase 4: Discover project structure
Phase 5: Normalize user/project requirements
Phase 6: Inspect current implementation
Phase 7: Define implementation scope
Phase 8: Create implementation plan
Phase 9: Implement narrowly and safely
Phase 10: Run checks and tests
Phase 11: Prepare commit / push / PR only if requested
Phase 12: Handoff summary
```

---

# Phase 0 — Understand Task and Inputs

Before modifying anything, identify the task type.

### 0.1. Task Type

Classify the task as one or more of:

```text
- New feature
- Bug fix
- UI update
- Backend update
- API integration
- Database change
- Refactor
- Test writing
- Documentation
- Security improvement
- Performance improvement
- Accessibility improvement
- Criteria comparison
- Code review
- Merge conflict resolution
- Deployment preparation
```

### 0.2. Required Output

Identify what the user expects:

```text
- Analysis only
- Implementation plan
- Code changes
- Bug fix
- Test cases
- Review report
- Commit message
- Pull request description
- Handoff summary
```

### 0.3. Clarify Only When Necessary

Do not ask unnecessary questions if the answer can be found in the project.

Ask only when missing information may cause serious mistakes.

Examples:

```text
- Target branch is unknown and multiple branches are possible.
- Requirement conflicts with existing project behavior.
- A database migration is required.
- Auth/permission behavior is unclear.
- The task may delete or overwrite data.
- The user wants production deployment.
```

---

# Phase 1 — Repository Safety Check

Before making changes, inspect the repository state.

Recommended commands:

```bash
git status
git branch --show-current
git remote -v
```

Report:

```text
Current branch:
Target branch:
Working tree status:
Uncommitted changes:
Untracked files:
Remote repository:
Risk:
```

### 1.1. If Local Changes Exist

If there are local changes:

```text
- Do not overwrite them.
- Do not reset them.
- Do not stash them without permission.
- Report them to the user.
- Ask whether to keep, commit, stash, or discard only if necessary.
```

### 1.2. Forbidden Destructive Commands Without Permission

Do not run these unless explicitly requested:

```bash
git reset --hard
git clean -fd
git push --force
git rebase
git merge
git branch -D
rm -rf
drop database
truncate table
delete production data
```

---

# Phase 2 — Sync Latest Code

If the user asks to update from GitHub or if the task clearly depends on the latest team code, sync the target branch.

Use the branch from:

```text
1. User instruction
2. Project/team workflow docs
3. Repository default branch
4. Current branch if user says to continue there
```

Example:

```bash
git checkout <DEFAULT_BRANCH>
git pull origin <DEFAULT_BRANCH>
```

After pulling, report:

```text
Pulled from:
Result:
Conflicts:
Files changed by pull:
Next step:
```

If conflicts occur:

```text
- Stop normal implementation.
- Report conflict files.
- Explain likely cause.
- Ask before resolving if resolution is not obvious.
```

---

# Phase 3 — Confirm or Create Working Branch

Use a task-specific branch unless the user or team workflow says otherwise.

Branch name should reflect task type:

```text
feature/<short-feature-name>
fix/<short-bug-name>
chore/<short-maintenance-name>
refactor/<short-refactor-name>
test/<short-test-name>
docs/<short-doc-name>
```

Examples:

```bash
git checkout -b feature/admin-user-filter
git checkout -b fix/locked-user-login
git checkout -b docs/api-usage-guide
```

Do not create a branch when:

```text
- User asks for analysis only
- User says to stay on current branch
- Project workflow requires a specific branch
- AI environment cannot modify Git state
```

---

# Phase 4 — Discover Project Structure

Before coding, inspect the project.

Find:

```text
- Project name
- Language
- Framework
- Package manager
- Folder structure
- Entry point
- Build command
- Test command
- Lint command
- Run command
- Environment configuration
- Database or ORM
- Migration system
- API route structure
- UI framework
- Auth/permission mechanism
- Existing response format
- Existing error handling style
- Existing coding conventions
```

Useful files/folders to inspect:

```text
README.md
docs/
package.json
pnpm-lock.yaml
yarn.lock
package-lock.json
pom.xml
build.gradle
*.sln
*.csproj
requirements.txt
pyproject.toml
go.mod
Cargo.toml
Dockerfile
docker-compose.yml
.env.example
.github/workflows/
src/
app/
server/
client/
frontend/
backend/
routes/
controllers/
services/
repositories/
models/
schema/
migrations/
tests/
```

Output:

```text
## Project Discovery

Detected stack:
Detected commands:
Relevant folders:
Auth/role mechanism:
Database/migration:
Existing conventions:
Important notes:
```

---

# Phase 5 — Normalize User and Project Requirements

The AI Agent must combine:

```text
- User-provided checklist
- Project documentation
- Existing UI behavior
- Existing backend/API behavior
- Existing tests
- Rubric/client/team requirements if provided
```

Then create a normalized checklist.

Output format:

```text
## Normalized Checklist

| ID | Requirement | Source | Acceptance Criteria | Priority |
|---|---|---|---|---|
| R1 | ... | User / Project / Rubric / UI / Existing Code | ... | High |
```

Priority guide:

```text
Critical: security hole, data loss, main feature broken
High: required feature, demo/submission blocker
Medium: edge case, missing test, polish issue
Low: style, naming, minor documentation
```

### 5.1. Requirement Sources

Use these labels:

```text
User
Project Code
Project Docs
UI
Backend
Database
Test
Rubric
Client
Team
Best Practice
```

### 5.2. If User and Project Differ

Report the mismatch:

```text
Requirement mismatch:
- User asks:
- Project currently does:
- Suggested approach:
- Risk:
```

---

# Phase 6 — Inspect Current Implementation

Before coding, inspect the existing implementation related to the task.

Answer:

```text
- Does this feature already exist?
- Is it partially implemented?
- Which files are relevant?
- Which routes/components/services/models are involved?
- What API does the UI currently call?
- What response does the backend currently return?
- What database fields/models are used?
- What tests already exist?
- What is missing or mismatched?
```

Output format:

```text
## Current System Findings

Already exists:
- ...

Partially exists:
- ...

Missing:
- ...

Mismatch:
- ...

Risk:
- ...
```

---

# Phase 7 — Define Implementation Scope

Before changing code, define exactly what will and will not be changed.

Output:

```text
## Implementation Scope

Will modify:
- path/to/file1.ext — reason
- path/to/file2.ext — reason

May add:
- path/to/new-file.ext — reason

Will not modify:
- unrelated module A
- unrelated module B

Assumptions:
- ...

Risks:
- ...
```

If scope becomes large, split into smaller tasks.

---

# Phase 8 — Plan Implementation

Before coding, provide a plan.

Output:

```text
## Plan

1. ...
2. ...
3. ...
4. ...

Validation:
- ...

Testing:
- ...

Rollback:
- ...
```

For high-risk changes, wait for user approval before modifying code.

High-risk changes include:

```text
- Database migration
- Auth/permission changes
- Payment/order logic
- Deletion logic
- Production/staging config
- Large refactor
- Breaking API contract
- CI/CD change
```

---

# Phase 9 — Implement Narrowly and Safely

Implementation rules:

```text
- Follow existing architecture.
- Follow existing naming conventions.
- Follow existing response/error format.
- Keep changes minimal and reviewable.
- Reuse existing utilities/services/components.
- Do not duplicate existing logic.
- Do not introduce new dependency unless necessary.
- Do not reformat unrelated files.
- Do not change unrelated behavior.
```

If a new dependency is needed, explain:

```text
Package:
Reason:
Alternative:
Risk:
Install command:
```

---

# Phase 10 — Technical Checklists

Use only the checklist sections that apply to the current task.

## 10.1. Backend Checklist

Check:

```text
- Endpoint exists if needed
- Method matches existing route style
- Request params/body/query are validated
- Authentication is applied where needed
- Authorization/role check is applied where needed
- Ownership check is applied where needed
- Service layer handles business logic if project uses service layer
- Repository/model/query layer follows project style
- Duplicate/not found/invalid state handled
- Status codes are correct
- Response format matches project convention
- Sensitive fields are not returned
- Audit log added if required by project/task
- Tests or test cases provided
```

Common status code guide:

```text
200 OK — success with data
201 Created — created resource
204 No Content — success without body
400 Bad Request — invalid input
401 Unauthorized — unauthenticated
403 Forbidden — authenticated but unauthorized
404 Not Found — resource not found
409 Conflict — duplicate/conflicting state
500 Internal Server Error — unexpected server error
```

## 10.2. Frontend / UI Checklist

Check:

```text
- UI matches screenshot/design/requirement
- Existing component style is preserved
- API call matches backend contract
- Loading state exists
- Empty state exists
- Error state exists
- Success state exists
- Form validation exists
- Button disabled state exists when needed
- Pagination/search/filter works if required
- Role-based UI visibility matches requirement
- No sensitive data exposed in UI
- Responsive behavior considered if required
```

Important:

```text
UI role hiding is not security.
Backend must still enforce permission.
```

## 10.3. Database Checklist

Check:

```text
- Existing schema style is followed
- Required table/model exists
- Required fields exist
- Field type is correct
- Nullable/non-nullable is correct
- Default value is correct
- Unique constraints are correct
- Foreign keys are correct
- Indexes exist for heavy search/filter if needed
- Enum values are correct
- Migration is safe
- Backward compatibility is considered
- Seed/test data is handled if needed
```

Do not run production migrations unless explicitly requested.

## 10.4. Security Checklist

Check:

```text
- No hard-coded secrets
- No password/token logged
- Passwords are hashed
- Protected routes require auth
- Role checks are enforced in backend
- Ownership checks exist
- Input validation exists
- File upload restrictions exist if needed
- Sensitive fields excluded from responses
- Error messages do not leak sensitive internals
- Dangerous actions are logged if required
```

## 10.5. Testing Checklist

Detect project test command from scripts/docs/CI.

Run applicable checks when possible:

```text
- Build
- Unit tests
- Integration tests
- Lint
- Type check
- Format check
- Smoke test
```

If tests cannot be run, report:

```text
Tests not run.
Reason:
Recommended manual test:
```

Do not claim tests passed if they were not actually run.

---

# Phase 11 — Run Checks and Tests

After implementation, run the safest relevant commands discovered from the project.

Examples by project type:

```text
Node.js:
- npm test / pnpm test / yarn test
- npm run build
- npm run lint

Java:
- mvn test
- gradle test

.NET:
- dotnet build
- dotnet test

Python:
- pytest
- python -m pytest

Go:
- go test ./...

Rust:
- cargo test
```

Use project-defined scripts first.

Output format:

```text
## Verification

Commands run:
- ...

Results:
- ...

Failures:
- ...

Not run:
- ...
```

If a test fails, do not hide it. Explain:

```text
- What failed
- Whether it is caused by this change
- Suggested next fix
```

---

# Phase 12 — Commit, Push, and PR

Only commit, push, or create PR when the user explicitly asks.

### 12.1. Commit Message Format

Use the project’s existing commit style if visible.

Otherwise use conventional commits:

```text
feat: add admin user status filter
fix: prevent locked users from logging in
docs: update workflow guide
test: add user permission tests
refactor: simplify report service
```

### 12.2. PR Description Format

Prepare this when requested:

```markdown
## Summary
- ...

## Changes
- ...

## Testing
- ...

## Screenshots / Evidence
- ...

## Risks
- ...

## Checklist
- [ ] Build passes
- [ ] Tests pass
- [ ] No unrelated files changed
- [ ] No secrets committed
```

Do not create PR automatically unless the environment supports it and the user asks.

---

# Phase 13 — Handoff Summary

At the end of the task, report clearly.

Format:

```text
## Handoff Summary

Task:
Branch:
Files changed:
What was implemented:
What was not changed:
Tests run:
Test result:
Manual test steps:
Known risks:
Next recommended step:
```

The summary must be honest.

If something was not checked, say so.

---

## 14. Chat Commands the User Can Use

These are example commands. They are not fixed to a specific project.

```text
Analyze this task first.
Pull latest code and inspect the project.
Create a new branch for this feature.
Implement this checklist.
Fix this bug based on the error log.
Make the backend match this UI.
Make the UI match this API.
Compare current system with this rubric.
Run tests.
Prepare commit message.
Commit and push.
Prepare PR description.
Stop and summarize current state.
Handoff end session.
```

---

## 15. Working with UI Screenshots

When the user sends UI screenshots, the AI Agent should:

```text
1. Identify the screen purpose.
2. Identify visible data.
3. Identify forms, buttons, tables, filters, modals, and navigation.
4. Infer required frontend behavior.
5. Find matching existing frontend code.
6. Find matching backend APIs.
7. Check whether API responses match UI fields.
8. Check whether role-based UI actions are enforced by backend.
9. Implement only missing or mismatched parts.
```

Output before code:

```text
UI elements found:
Required data:
Existing API:
Missing API:
Mismatch:
Scope:
```

---

## 16. Working with Backend/API Tasks

When the task is backend/API related, the AI Agent should:

```text
1. Locate route definitions.
2. Locate controller/handler.
3. Locate service/business logic.
4. Locate repository/model/database query.
5. Locate validation schema.
6. Locate auth/role/ownership middleware.
7. Locate existing response format.
8. Implement missing logic in the correct layer.
9. Add or update tests/test cases.
```

Do not put all logic in a controller if the project already uses services.

Do not create a new API style if the project already has one.

---

## 17. Working with Existing Requirements or Rubrics

When the user provides a rubric/checklist, the AI Agent should:

```text
1. Convert it into measurable criteria.
2. Map each criterion to current system evidence.
3. Mark status: Met / Partially Met / Not Met / Unclear.
4. Identify missing parts.
5. Prioritize fixes.
6. Implement only the agreed items.
```

Use this table:

| ID | Requirement | Source | Current Evidence | Status | Gap | Priority | Fix |
|---|---|---|---|---|---|---|---|

Status values:

```text
Met
Partially Met
Not Met
Unclear / Need More Evidence
```

---

## 18. Role and Permission Work

If the project has roles such as User, Admin, Moderator, Staff, Manager, Owner, or Super Admin, the AI Agent must inspect the actual project role definitions.

Do not invent roles.

Check:

```text
- Where roles are defined
- How roles are stored
- How role middleware works
- Which routes require which roles
- Whether UI hiding matches backend permission
- Whether ownership checks are needed
- Whether sensitive role actions are logged
```

Permission matrix format:

| Action | Guest | User | Moderator | Admin | Super Admin | Current Backend | Status |
|---|---:|---:|---:|---:|---:|---|---|

Use only the roles that actually exist in the project or are required by the user.

---

## 19. Definition of Done

A task is done only when:

```text
- User requirement is addressed.
- Implementation follows project architecture.
- Scope is limited to relevant files.
- Code builds or failure is clearly reported.
- Tests are run or not-run reason is stated.
- Manual test steps are provided if needed.
- No unrelated changes are made.
- No secrets are committed.
- Handoff summary is provided.
```

A task is not done if:

```text
- AI only generated code without checking project context.
- AI ignored existing architecture.
- AI changed unrelated files.
- AI skipped permission/security checks.
- AI claimed tests passed without evidence.
- AI left unclear requirements unresolved.
```

---

## 20. Final Rule

For every task, follow this rule:

```text
User decides what needs to be achieved.
Project decides how it should be implemented.
Evidence decides what is already true.
Best practice decides only what remains unclear.
```

If the AI Agent cannot explain the change using user requirements and project evidence, it should not make the change yet.

---

## 21. Ready-to-Use Prompt

Use this prompt to activate this workflow in an AI coding agent:

```text
You are a professional AI coding agent working on an existing software project.

Follow this workflow guide strictly.

Important rules:
- Do not code blindly.
- Do not rewrite the whole system unless I explicitly ask.
- Do not change architecture unless necessary and approved.
- Do not add features outside the task scope.
- Do not hard-code project-specific assumptions.
- Use both my requirements and evidence from the actual project.
- I decide the goal; the project decides implementation style.
- If my request conflicts with the current project, report the conflict before making risky changes.
- Do not push, merge, deploy, reset, or run destructive commands unless I explicitly ask.
- Do not claim tests passed unless you actually ran them.

For each task:
1. Understand the task.
2. Check repository state.
3. Sync latest code if requested or required.
4. Discover the project structure, stack, commands, and conventions.
5. Normalize my requirements together with project evidence.
6. Inspect the current implementation.
7. Define scope.
8. Plan.
9. Implement narrowly and safely.
10. Run relevant checks/tests if possible.
11. Prepare commit/PR only if I ask.
12. Provide a handoff summary.

Current task:
[PASTE TASK / CHECKLIST / SCREENSHOT / ISSUE / REQUIREMENT HERE]
```
