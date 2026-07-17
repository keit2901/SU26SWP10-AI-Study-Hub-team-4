# Skill Ecosystem Orchestration Guide

## 1. Purpose

This guide defines how multiple project skills should work together as one connected workflow system.

It is not a single task skill.  
It is a meta-guide for managing, updating, combining, and improving all skills over time.

Use this guide when:

```text
- Multiple skill files exist.
- A new skill is added.
- Existing skills overlap.
- One skill depends on another skill.
- The AI Agent needs to know which skill to use first.
- The user wants all skills to support each other instead of working separately.
- The workflow must stay professional and consistent across important projects.
```

The goal is to make all skills behave like a coordinated system, not isolated prompt files.

---

## 2. Core Principle

All skills must support the same professional workflow.

The core rule is:

```text
Each skill should solve one clear problem,
but every skill must also connect smoothly with the other skills.
```

No skill should work in a way that contradicts another skill.

No skill should duplicate another skill unless it clearly extends it.

No skill should hard-code project-specific assumptions unless the user or the current project provides them.

---

## 3. Current Skill Ecosystem

This section should be updated whenever a skill is added, removed, renamed, or significantly changed.

## 3.1. Workflow Guide

File:

```text
WORKFLOW_GUIDE_PROFESSIONAL.md
```

Purpose:

```text
Defines the main professional workflow for AI Agent work on existing projects.
It controls repository safety, project discovery, requirement normalization, scope, plan, implementation, testing, commit/PR, and handoff.
```

Use when:

```text
- Starting any real coding task
- Pulling latest code
- Reading project structure
- Planning a feature or fix
- Testing and handing off work
```

Relationship with other skills:

```text
This is the foundation workflow.
All other skills should follow its safety rules, project-discovery rules, and handoff rules.
```

---

## 3.2. Existing Project Update Skill

Example file:

```text
skill2.md
```

Purpose:

```text
Handles tasks where the project already has UI/backend/system code and the AI Agent must pull or inspect the current GitHub code, then update only the required part.
```

Use when:

```text
- UI and backend already exist
- The user says to pull latest code
- The user wants to update a feature based on the current system
- The user wants to avoid rewriting from scratch
```

Relationship with Workflow Guide:

```text
Must follow repository safety, branch strategy, project discovery, scope definition, testing, and handoff from WORKFLOW_GUIDE_PROFESSIONAL.md.
```

Relationship with Role-Based Comparison Skill:

```text
If the update involves roles, permission, Admin, Moderator, User, or system logic, first use the Role-Based Comparison Skill to identify what is missing or unsafe.
Then use this skill to implement the update.
```

---

## 3.3. Role-Based System Criteria Comparison Skill

Example file:

```text
skill3_role_based_system_criteria_comparison.md
```

Purpose:

```text
Compares the current system against criteria by role and system logic.
It separates Admin, Moderator, User, Super Admin, UI-backend consistency, database consistency, permission matrix, and system logic.
```

Use when:

```text
- The user wants to compare system features with a rubric or requirement
- The user asks whether Admin/Moderator/User parts are enough
- The user wants to check role permissions
- The user wants to find gaps before coding
- The user wants a priority fix plan
```

Relationship with Workflow Guide:

```text
Uses project discovery and requirement normalization from WORKFLOW_GUIDE_PROFESSIONAL.md.
Should not directly code unless the user asks after the comparison.
```

Relationship with Existing Project Update Skill:

```text
This skill identifies gaps.
Existing Project Update Skill implements the selected fixes.
```

---

## 3.4. External Criteria / Gap Analysis Skill

If there is a separate external criteria audit skill, register it here.

Purpose:

```text
Compares the system with external rubrics, templates, standards, or client/teacher requirements.
```

Use when:

```text
- The comparison is broader than roles
- The criteria include UI, security, API, database, report format, testing, maintainability, or documentation
```

Relationship with Role-Based Comparison Skill:

```text
If external criteria include roles, permissions, or user types, combine it with the Role-Based System Criteria Comparison Skill.
```

---

## 4. Skill Selection Rules

When a new task arrives, choose the skill based on the task type.

## 4.1. If the user asks to code or update an existing project

Use:

```text
1. WORKFLOW_GUIDE_PROFESSIONAL.md
2. skill2.md or the existing project update skill
```

Add:

```text
- Role-Based Comparison Skill if roles/permissions are involved
- External Criteria Skill if a rubric or requirement is involved
```

---

## 4.2. If the user asks to compare the system with criteria

Use:

```text
1. WORKFLOW_GUIDE_PROFESSIONAL.md for project discovery
2. External Criteria / Gap Analysis Skill
3. Role-Based Comparison Skill if criteria involve roles
```

Do not immediately code.

Output should be:

```text
- Criteria mapping
- Evidence
- Status
- Gap
- Priority
- Recommended fixes
```

---

## 4.3. If the user asks about Admin / Moderator / User / permissions

Use:

```text
1. Role-Based System Criteria Comparison Skill
2. WORKFLOW_GUIDE_PROFESSIONAL.md for project discovery
```

If the user then says to fix:

```text
Use Existing Project Update Skill to implement only selected fixes.
```

---

## 4.4. If the user asks to pull latest GitHub code and continue work

Use:

```text
1. WORKFLOW_GUIDE_PROFESSIONAL.md
2. Existing Project Update Skill
```

Do not use comparison skills unless the task involves evaluating criteria.

---

## 4.5. If the user asks to add a new skill

Use this guide.

The AI Agent must:

```text
1. Identify what problem the new skill solves.
2. Check whether an existing skill already covers it.
3. Decide whether to create a new skill or update an existing skill.
4. Register the new skill in this guide.
5. Define how it connects to existing skills.
6. Update the skill selection rules if needed.
7. Update conflict rules if the new skill may overlap with old skills.
```

---

## 5. Skill Dependency Model

Skills should be layered.

## 5.1. Foundation Layer

The foundation layer controls safety and process.

```text
WORKFLOW_GUIDE_PROFESSIONAL.md
```

All coding-related skills must follow this layer.

---

## 5.2. Analysis Layer

The analysis layer checks what should be done.

Examples:

```text
- External criteria audit
- Role-based system comparison
- UI-backend gap analysis
- Security review
- Test coverage review
```

Analysis skills should usually produce:

```text
- Findings
- Evidence
- Gap
- Priority
- Fix plan
```

They should not immediately rewrite the system.

---

## 5.3. Implementation Layer

The implementation layer changes code.

Examples:

```text
- Existing project update skill
- Bug fix skill
- Feature implementation skill
- UI integration skill
- API integration skill
```

Implementation skills should use analysis outputs as input.

They should produce:

```text
- Code changes
- Test results
- Handoff summary
```

---

## 5.4. Delivery Layer

The delivery layer prepares final project output.

Examples:

```text
- Commit message
- PR description
- Handoff summary
- Demo checklist
- Submission checklist
- Documentation update
```

Delivery must be based on what was actually changed and tested.

---

## 6. Skill Combination Patterns

## 6.1. Pattern A — Criteria First, Code Later

Use when the user says:

```text
Compare this system with the rubric, then fix missing parts.
```

Flow:

```text
1. Workflow Guide: discover project.
2. External Criteria Skill: extract criteria.
3. Role-Based Skill: separate criteria by role if needed.
4. User selects what to fix.
5. Existing Project Update Skill: implement selected fixes.
6. Workflow Guide: test and handoff.
```

---

## 6.2. Pattern B — UI Exists, Backend Must Match

Use when the user says:

```text
UI already exists. Make backend work with it.
```

Flow:

```text
1. Workflow Guide: inspect project.
2. Existing Project Update Skill: identify UI fields/actions/API calls.
3. Role-Based Skill: check permissions if UI has role-based actions.
4. Implement missing/mismatched backend only.
5. Test UI-backend flow.
6. Handoff summary.
```

---

## 6.3. Pattern C — Role/Permission Audit Before Fix

Use when the user says:

```text
Check Admin, Mod, User logic.
```

Flow:

```text
1. Workflow Guide: discover auth/role structure.
2. Role-Based Skill: build permission matrix.
3. Identify Critical/High permission gaps.
4. User approves fixes.
5. Existing Project Update Skill: patch permissions.
6. Test unauthorized and authorized cases.
```

---

## 6.4. Pattern D — GitHub Update Task

Use when the user says:

```text
Pull latest code and update this feature.
```

Flow:

```text
1. Workflow Guide: git status and safety check.
2. Pull target branch if requested.
3. Existing Project Update Skill: inspect current code.
4. Normalize requirement from user and project.
5. Implement only the needed change.
6. Run project checks.
7. Prepare commit/PR only if requested.
```

---

## 6.5. Pattern E — New Skill Added

Use when the user says:

```text
Add another skill.
```

Flow:

```text
1. Define the new skill’s purpose.
2. Check overlap with existing skills.
3. Create or update the skill file.
4. Register it in this orchestration guide.
5. Add selection rules.
6. Add dependency rules.
7. Add combination patterns if needed.
8. Update version history.
```

---

## 7. Cross-Skill Handoff Format

When one skill produces output that another skill will use, format the handoff clearly.

Use this structure:

```text
## Skill Handoff

Source skill:
Target skill:
Task:
Relevant files:
Requirements:
Findings:
Decisions:
Open questions:
Risks:
Recommended next action:
```

Example:

```text
Source skill: Role-Based System Criteria Comparison
Target skill: Existing Project Update Skill

Task:
Fix Admin permission gaps.

Findings:
- Admin can currently lock Super Admin.
- Backend lacks service-layer role guard.
- UI hides the action, but backend still allows direct API call.

Recommended next action:
Patch backend authorization and add negative test case.
```

---

## 8. Shared Terminology

All skills should use the same terminology.

## 8.1. Status Labels

Use these for criteria comparison:

```text
Met
Partially Met
Not Met
Unclear / Need More Evidence
```

## 8.2. Priority Labels

Use these for fixes:

```text
Critical
High
Medium
Low
```

## 8.3. Risk Labels

Use these for implementation risk:

```text
Low Risk
Medium Risk
High Risk
Blocking Risk
```

## 8.4. Task Types

Use these where possible:

```text
Feature
Bug Fix
UI Update
Backend Update
API Integration
Database Change
Security Fix
Performance Improvement
Documentation
Testing
Refactor
Criteria Comparison
Code Review
```

---

## 9. Conflict Rules Between Skills

If two skills suggest different actions, use this priority order:

```text
1. User’s current explicit instruction
2. Project evidence and existing architecture
3. Safety/security requirements
4. Rubric/client/team requirements
5. Existing workflow guide
6. Specific skill instruction
7. General best practice
```

Important:

```text
A specific skill cannot override repository safety rules.
A coding skill cannot ignore a critical security gap found by an audit skill.
A comparison skill cannot claim implementation is complete without project evidence.
A delivery skill cannot claim tests passed unless tests were actually run.
```

---

## 10. Updating Skills When a New Skill Is Added

When adding a new skill, update this guide.

Required update checklist:

```text
- Add the new skill name and file path.
- Add its purpose.
- Add when to use it.
- Add what output it produces.
- Add which skills it depends on.
- Add which skills should use its output.
- Add conflicts or overlap with existing skills.
- Add at least one combination pattern if needed.
- Update the version history.
```

Template:

```text
## New Skill Registration

Skill name:
File path:
Purpose:
Use when:
Do not use when:
Inputs:
Outputs:
Depends on:
Feeds into:
Overlap with existing skills:
Conflict rule:
Example workflow:
```

---

## 11. Updating Existing Skills

When editing an existing skill, check whether the change affects other skills.

Ask:

```text
- Does this change modify the skill’s purpose?
- Does this change modify the inputs or outputs?
- Does this change overlap with another skill?
- Does another skill depend on the old behavior?
- Should the skill selection rules be updated?
- Should the handoff format be updated?
- Should shared terminology be updated?
```

If yes, update this orchestration guide.

---

## 12. Skill Compatibility Matrix

Maintain this matrix as skills evolve.

| Skill | Main Role | Depends On | Feeds Into | Should Not Replace |
|---|---|---|---|---|
| WORKFLOW_GUIDE_PROFESSIONAL.md | Foundation workflow | None | All coding and analysis skills | Specific task skills |
| skill2.md | Existing project update | Workflow Guide | Testing, commit, PR, handoff | Role-based audit |
| skill3_role_based_system_criteria_comparison.md | Role/permission comparison | Workflow Guide | Existing project update | General workflow |
| External Criteria Audit Skill | Broad criteria/gap analysis | Workflow Guide | Role-based skill, implementation skill | Code implementation skill |
| This Orchestration Guide | Skill coordination | All skill metadata | All skills | Individual task execution |

Update the matrix when new skills are created.

---

## 13. Skill Quality Checklist

Every skill should pass this checklist.

```text
- Has a clear purpose.
- Has clear when-to-use rules.
- Has clear do-not-use rules if needed.
- Does not hard-code one project unless intentionally project-specific.
- Uses project evidence where relevant.
- Uses user intent where relevant.
- Defines inputs and outputs.
- Defines boundaries.
- Connects to other skills.
- Does not duplicate another skill unnecessarily.
- Has a handoff format if another skill may use its output.
- Uses shared status/priority terminology.
- Follows repository safety rules if it involves code.
```

---

## 14. Anti-Patterns

Avoid these mistakes.

## 14.1. Isolated Skill Problem

Bad:

```text
Each skill gives separate instructions and does not know how to connect to the others.
```

Fix:

```text
Register each skill here and define how it depends on or feeds into other skills.
```

---

## 14.2. Duplicate Skill Problem

Bad:

```text
Two skills both tell the AI Agent to compare roles, but with different formats.
```

Fix:

```text
Choose one as the source of truth.
Make the other skill reference it or narrow its scope.
```

---

## 14.3. Workflow Bypass Problem

Bad:

```text
A coding skill tells the AI Agent to edit code immediately without git status, project discovery, or scope definition.
```

Fix:

```text
All coding skills must follow WORKFLOW_GUIDE_PROFESSIONAL.md first.
```

---

## 14.4. Hard-Coded Project Problem

Bad:

```text
A general skill assumes a specific repo path, localhost port, stack, account, branch, or checklist.
```

Fix:

```text
Replace hard-coded values with user/project-discovered values.
```

---

## 14.5. Fake Completion Problem

Bad:

```text
A delivery skill says everything is done even though tests were not run.
```

Fix:

```text
Require honest verification status: run, failed, skipped, or not available.
```

---

## 15. Standard Skill Header Template

Every new skill should start with this structure:

```markdown
# Skill Name

## 1. Purpose

## 2. When to Use

## 3. When Not to Use

## 4. Required Inputs

## 5. Expected Outputs

## 6. Relationship With Other Skills

## 7. Workflow

## 8. Safety Rules

## 9. Output Format

## 10. Handoff Format
```

---

## 16. Standard Skill Relationship Section

Each skill should contain a section like this:

```markdown
## Relationship With Other Skills

Foundation:
- Follow WORKFLOW_GUIDE_PROFESSIONAL.md for repository safety, project discovery, scope, testing, and handoff.

Can receive input from:
- ...

Can produce output for:
- ...

Should be used before:
- ...

Should be used after:
- ...

Should not replace:
- ...
```

---

## 17. Version History

Maintain version history whenever this guide or any skill changes.

Format:

```text
Version:
Date:
Changed files:
Reason:
Impact on other skills:
Follow-up needed:
```

Example:

```text
Version: 1.1
Date: YYYY-MM-DD
Changed files:
- skill3_role_based_system_criteria_comparison.md
- SKILL_ECOSYSTEM_ORCHESTRATION_GUIDE.md

Reason:
Added role-based permission matrix and cross-skill handoff rules.

Impact:
Existing project update skill should use role-based findings before permission fixes.

Follow-up:
Update any future security skill to reuse the same priority labels.
```

---

## 18. Ready-to-Use Meta Prompt

Use this prompt when asking an AI Agent to manage or update the skill ecosystem.

```text
You are managing a set of AI Agent skills for software project work.

Your task is not only to create or edit one skill, but also to make sure all skills support each other effectively.

Follow these rules:
1. Identify the purpose of the new or updated skill.
2. Check whether an existing skill already covers the same purpose.
3. Avoid duplication unless the new skill has a clearly different scope.
4. Make the skill project-agnostic unless the user explicitly wants a project-specific skill.
5. Define how the skill connects to the existing workflow guide.
6. Define which skills should be used before it and after it.
7. Define what output it gives to other skills.
8. Update the skill registry.
9. Update the skill selection rules.
10. Update compatibility and conflict rules if needed.
11. Add or update handoff format if the skill produces output for another skill.
12. Keep shared labels consistent: Met / Partially Met / Not Met / Unclear and Critical / High / Medium / Low.

Current skills:
[PASTE CURRENT SKILL LIST HERE]

New or updated skill request:
[PASTE REQUEST HERE]
```

---

## 19. Final Rule

The skill ecosystem is effective only when every skill answers these questions:

```text
1. What problem does this skill solve?
2. When should it be used?
3. What should be used before it?
4. What should be used after it?
5. What output does it produce?
6. Which skill consumes that output?
7. What safety rules must it follow?
8. What should it not do?
```

If a skill cannot answer these questions, it is not ready for important projects.
