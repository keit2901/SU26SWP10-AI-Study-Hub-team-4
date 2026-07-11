# One-Command Continue Work Protocol

---

## SWP391 — Resume Rules (đọc trước khi làm gì)

### KHI NHẬN LỆNH `tiếp tục SWP`

| # | Bước | Cấm làm |
|---|------|---------|
| 1 | Đọc `previous_session/handoff_*.md` mới nhất | ❌ Không scan lại admin menu từ đầu |
| 2 | Đọc `previous_session/_CURRENT_TODO.md` | ❌ Không tạo lại list làm việc từ đầu |
| 3 | Checkout branch từ handoff | ❌ Không tạo branch mới |
| 4 | Xác định task đang dở → làm tiếp luôn | ❌ Không hỏi lại "bắt đầu từ đâu" |

### Quy tắc cứng
- **Handoff là nguồn chân lý** — mọi thông tin về task dở, branch, file đã sửa đều có trong đó
- **Không scan lại codebase** — tận dụng handoff + reusable sessions
- **Không tạo lại list** — tiếp tục từ list có sẵn
- **Làm tiếp task đang dở** — không hỏi lại ưu tiên

---

## 1. Purpose

This file defines how an AI Agent should continue unfinished project work in a new session using one short command.

The goal is:

```text
User says one command.
AI loads project context.
AI reads latest session/handoff.
AI identifies unfinished work.
AI resumes the correct workflow without rediscovering the project from zero.
```

This is different from a read-only update command.

---

## 2. Main Command

Recommended command:

```text
tiếp tục <PROJECT_KEY>
```

Example:

```text
tiếp tục SWP
```

Meaning:

```text
Continue the latest unfinished safe task for the SWP project.
```

The AI Agent should not ask:

```text
- Project gì?
- Repo ở đâu?
- Stack là gì?
- Hôm trước làm gì?
- File nào cần đọc?
```

unless the startup files are missing, ambiguous, or outdated.

---

## 3. Difference Between Update and Continue

## 3.1. `cập nhật <PROJECT_KEY>`

Mode:

```text
Read-only context refresh.
```

AI should:

```text
- Read project startup files.
- Read latest session/handoff.
- Verify branch/status with read-only commands.
- Summarize current state.
- Suggest next actions.
- Do not modify code.
```

## 3.2. `tiếp tục <PROJECT_KEY>`

Mode:

```text
Controlled task continuation.
```

AI should:

```text
- First run the read-only startup protocol.
- Identify latest unfinished task.
- Load only the relevant skills/docs.
- Continue the task using the correct workflow.
- Make safe progress if the next step is low-risk.
- Ask for confirmation only before risky/destructive actions.
```

---

## 4. What AI Must Read First

When user says:

```text
tiếp tục SWP
```

AI must read in this order:

```text
1. startup-keywords.md
2. Main project skill file
3. Current session file if it exists
4. Latest handoff file if no current session exists
5. Skill ecosystem / skill organization file if skill choice is unclear
6. Business rules only if task touches business logic
7. Test guide only if task touches testing
8. Relevant feature docs only if needed
```

Do not scan the whole repo from zero unless:

```text
- Startup files are missing.
- Handoff is outdated.
- File paths are invalid.
- Current task references missing files.
- Git state conflicts with handoff.
```

---

## 5. Required Safety Check Before Continuing

Before doing any work, AI must verify:

```text
- Current repo path
- Current branch
- Git status
- Uncommitted changes
- Whether current branch matches handoff
- Whether there are conflicts
- Whether the last task is still valid
```

Recommended read-only commands:

```bash
pwd
git branch --show-current
git status --short --branch --untracked-files=all
git remote -v
```

Do not run these during the initial safety check:

```bash
git pull
git merge
git rebase
git reset --hard
git clean -fd
git push
dotnet test
npm test
docker compose up
database migration
```

unless the user explicitly asked.

---

## 6. Continue Decision Logic

After reading session/handoff, AI must classify the continuation.

## 6.1. Safe to Continue Automatically

AI can continue without asking again if the next step is low-risk, such as:

```text
- Continue editing a non-critical documentation file
- Continue writing a report section
- Continue analyzing code
- Continue preparing test cases
- Continue making a planned small UI/CSS fix
- Continue creating a checklist
- Continue generating a prompt/skill file
```

Still report briefly what it is doing.

## 6.2. Ask Before Continuing

AI must ask or require confirmation before:

```text
- Running database migration
- Changing auth/permission logic
- Pulling/merging/rebasing
- Deleting files/data
- Resetting repo state
- Running destructive commands
- Deploying
- Pushing commits
- Creating PR
- Large refactor
- Changing API contract used by frontend
- Changing production/staging config
```

## 6.3. Stop and Report

AI must stop and report if:

```text
- Handoff and git state conflict
- Current branch is unexpected
- Uncommitted changes may be user/team work
- Required files are missing
- Last task is ambiguous
- There are unresolved merge conflicts
- The next task depends on missing secrets/API keys/services
```

---

## 7. Required Output Before Work

When `tiếp tục <PROJECT_KEY>` is received, AI should first produce a short continuation summary:

```text
## Continue Summary

Project:
Repo:
Branch:
Working tree:
Latest session:
Last completed:
Current unfinished task:
Next planned action:
Risk level:
Skills/docs loaded:
Will modify files now: Yes/No
Need confirmation: Yes/No
```

If the next action is safe, AI can continue after this summary.

If risky, AI should stop and ask confirmation.

---

## 8. Work Continuation Modes

Use one of these modes.

## Mode A — Resume Analysis

Use when the previous task was analysis/review.

```text
- Load relevant criteria.
- Continue comparison.
- Continue gap analysis.
- Produce next findings.
```

## Mode B — Resume Implementation

Use when the previous task was coding.

```text
- Load workflow guide.
- Load existing project update skill.
- Re-open relevant files only.
- Continue planned implementation.
- Keep changes narrow.
```

## Mode C — Resume Testing

Use when the previous task was testing.

```text
- Load comprehensive test guide.
- Load external test tools skill if needed.
- Continue from last passed/failed test point.
- Do not rerun everything unless required.
```

## Mode D — Resume Documentation

Use when the previous task was writing docs/reports/skills.

```text
- Open latest doc file.
- Continue from unfinished section.
- Keep naming and format consistent.
```

## Mode E — Resume Handoff

Use when previous session stopped due to context/time.

```text
- Read handoff.
- Verify branch/status.
- Continue exact next recommended action.
```

---

## 9. Skill Selection for Continue Work

When continuing, choose skills in this order:

```text
1. WORKFLOW_GUIDE_PROFESSIONAL.md
2. Project-specific skill/main project guide
3. Last task’s main skill from handoff
4. Supporting skill only if needed
5. Testing/evidence skill if verifying
6. Handoff/delivery skill at the end
```

Do not activate all skills.

Use the minimum relevant set.

Examples:

## 9.1. Continue Admin Role Fix

```text
1. WORKFLOW_GUIDE_PROFESSIONAL.md
2. skill3_role_based_system_criteria_comparison.md
3. skill2.md
4. SECURITY_DEFENSE_CHECKLIST.md
5. Test guide
```

## 9.2. Continue UI-Backend Integration

```text
1. WORKFLOW_GUIDE_PROFESSIONAL.md
2. skill2.md
3. API_CONTRACT_COMPATIBILITY.md
4. External test tools skill if API testing needed
```

## 9.3. Continue AI/RAG Testing

```text
1. WORKFLOW_GUIDE_PROFESSIONAL.md
2. COMPREHENSIVE_TEST_GUIDE.md
3. AI_RAG_EVALUATION_SAFETY.md
4. EXTERNAL_TEST_TOOLS_SKILL.md
```

---

## 10. How AI Should Decide the Exact Next Task

AI must infer the next task from:

```text
1. Current session file
2. Latest handoff file
3. Last “Next recommended step”
4. Unfinished checklist items
5. Failing tests or blockers
6. User’s latest instruction
```

Priority:

```text
User’s current instruction > current session > latest handoff > checklist > general recommendation
```

If user says only:

```text
tiếp tục SWP
```

AI should choose the most recent unfinished task from handoff/current session.

If multiple tasks are equally likely, AI should present options instead of guessing.

---

## 11. Required Behavior During Work

While continuing work, AI should:

```text
- Keep updates short.
- Avoid re-explaining the whole project.
- Use existing context from startup files.
- Only inspect files related to the current task.
- Preserve architecture and current conventions.
- Avoid unrelated refactor.
- Report actual changes.
- Verify when possible.
```

---

## 12. Required End-of-Work Handoff

At the end of resumed work, AI should produce:

```text
## Work Handoff

Project:
Branch:
Task continued:
Files changed:
What was completed:
What remains:
Tests/checks run:
Results:
Risks/blockers:
Next recommended command:
```

Recommended next command examples:

```text
cập nhật SWP
tiếp tục SWP
SWP chạy test
SWP commit + push
```

---

## 13. One-Command Continue Prompt

Use this prompt if an AI Agent needs explicit instruction.

```text
When I say `tiếp tục <PROJECT_KEY>`, do not rediscover the project from zero.

You must:
1. Read the project startup keyword file.
2. Read the main project skill.
3. Read the current session file if it exists.
4. If no current session exists, read the latest handoff.
5. Verify repo path, branch, and git status using read-only commands.
6. Identify the latest unfinished task.
7. Load only the relevant skills/docs for that task.
8. Continue the task if it is safe.
9. Ask before risky actions such as pull/merge/rebase/reset, migration, deploy, push, PR, auth changes, API breaking changes, or destructive commands.
10. Do not ask me to explain the whole project again unless startup files are missing or conflicting.

Output first:
- Project
- Branch
- Last completed
- Current unfinished task
- Next action
- Risk level
- Whether confirmation is needed

Then continue if safe.
```

---

## 14. Example Commands

## Read-only update

```text
cập nhật SWP
```

Expected:

```text
AI summarizes current project state and suggestions.
No code changed.
```

## Continue latest task

```text
tiếp tục SWP
```

Expected:

```text
AI loads context and continues the latest safe unfinished task.
```

## Continue a specific task

```text
tiếp tục SWP phần Admin role comparison
```

Expected:

```text
AI loads SWP context, then focuses on Admin role comparison task.
```

## Continue and test

```text
tiếp tục SWP rồi chạy test liên quan
```

Expected:

```text
AI resumes task, then runs only relevant tests if safe and available.
```

## Continue but do not edit

```text
tiếp tục SWP nhưng chỉ phân tích, chưa sửa code
```

Expected:

```text
AI loads context and provides analysis/plan only.
```

---

## 15. Final Rule

The command:

```text
tiếp tục <PROJECT_KEY>
```

means:

```text
Resume the latest unfinished work using saved project context.
```

It does not mean:

```text
Start from zero.
Ask the user to resend everything.
Use every skill.
Run risky commands automatically.
```

A good continue command should make the new session feel like the same working session, but with safety checks.
