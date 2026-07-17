# Skill Organization and Load Order Governance

## 1. Purpose

This skill defines how to organize, name, order, load, and apply multiple skill files in a clean and controlled way.

Use this skill when:

```text
- There are many skill files.
- The AI Agent starts using skills randomly.
- Skill names overlap.
- The team does not know which skill should be used first.
- A new skill is added and needs to be placed correctly.
- The user wants the skill system to be neat, predictable, and not messy.
```

This skill does not replace the Skill Ecosystem Orchestration Guide.

Instead, it focuses specifically on:

```text
- Skill file naming
- Skill numbering
- Folder structure
- Load order
- Skill priority
- Skill activation rules
- Skill index maintenance
- Avoiding random or duplicated skill usage
```

---

## 2. Core Rule

The AI Agent must not use skills randomly.

For every task, the AI Agent must decide:

```text
1. Which skill is the foundation skill?
2. Which skill is the main task skill?
3. Which skill is only supporting?
4. Which skill should not be used?
5. What order should the skills be applied in?
```

The AI Agent must not activate many skills just because they exist.

Use the minimum set of skills needed to complete the task safely and correctly.

---

## 3. Skill Layers

All skills should be organized into layers.

## 3.1. Layer 0 — Index and Governance

These files explain how the whole skill system is organized.

Examples:

```text
00_GOVERNANCE_ADDONS_INDEX.md
SKILL_ECOSYSTEM_ORCHESTRATION_GUIDE.md
10_SKILL_ORGANIZATION_AND_LOAD_ORDER.md
```

Use for:

```text
- Finding the right skill
- Adding new skills
- Resolving overlap
- Maintaining order
```

Do not use these as the main implementation skill.

---

## 3.2. Layer 1 — Foundation Workflow

These files control the general professional workflow.

Examples:

```text
WORKFLOW_GUIDE_PROFESSIONAL.md
skill2.md
```

Use for:

```text
- Starting real coding work
- Pulling latest code
- Reading project structure
- Defining scope
- Testing
- Handoff
```

These should usually be loaded before coding-related skills.

---

## 3.3. Layer 2 — Analysis and Comparison

These files analyze what should be done before implementation.

Examples:

```text
skill3_role_based_system_criteria_comparison.md
01_SECURITY_DEFENSE_CHECKLIST.md
06_API_CONTRACT_COMPATIBILITY.md
08_AI_RAG_EVALUATION_SAFETY.md
09_EXTERNAL_TEST_TOOLS_SKILL.md
```

Use for:

```text
- Role comparison
- Security review
- API/UI mismatch analysis
- AI/RAG evaluation
- External tool test planning
```

These should usually run before implementation if the task is risky or unclear.

---

## 3.4. Layer 3 — Implementation Support

These skills guide actual code updates.

Examples:

```text
skill2.md
WORKFLOW_GUIDE_PROFESSIONAL.md
07_DATABASE_MIGRATION_ROLLBACK.md
```

Use for:

```text
- Updating existing code
- Fixing bugs
- Matching UI with backend
- Database changes
- Migration changes
```

Implementation skills must follow foundation workflow rules.

---

## 3.5. Layer 4 — Verification and Delivery

These files verify readiness and prepare output.

Examples:

```text
02_DEMO_READINESS_QUALITY_GATE.md
04_LOCAL_RUNBOOK_TROUBLESHOOTING.md
05_LIGHTWEIGHT_CI_CD_POLICY.md
09_EXTERNAL_TEST_TOOLS_SKILL.md
```

Use for:

```text
- Final demo readiness
- Commit/PR preparation
- Local troubleshooting
- Evidence export
- Test summary
```

---

## 4. Recommended File Naming Convention

Use numeric prefixes for general governance/add-on files.

Recommended pattern:

```text
00_INDEX_OR_OVERVIEW.md
01_SECURITY_DEFENSE_CHECKLIST.md
02_DEMO_READINESS_QUALITY_GATE.md
03_ARCHITECTURE_DECISION_RECORDS.md
04_LOCAL_RUNBOOK_TROUBLESHOOTING.md
05_LIGHTWEIGHT_CI_CD_POLICY.md
06_API_CONTRACT_COMPATIBILITY.md
07_DATABASE_MIGRATION_ROLLBACK.md
08_AI_RAG_EVALUATION_SAFETY.md
09_EXTERNAL_TEST_TOOLS_SKILL.md
10_SKILL_ORGANIZATION_AND_LOAD_ORDER.md
```

Rules:

```text
- Use 00 for index/overview.
- Use 01-09 for core governance topics.
- Use 10+ for meta-skills and expansion skills.
- Do not reuse the same number for different skills.
- Do not rename files randomly after they are referenced by other files.
- If a file is renamed, update all indexes and references.
```

---

## 5. Recommended Folder Structure

Recommended clean structure:

```text
project-guides/
├── README.md
├── workflow/
│   ├── WORKFLOW_GUIDE_PROFESSIONAL.md
│   └── SKILL_ECOSYSTEM_ORCHESTRATION_GUIDE.md
├── skills/
│   ├── skill2_existing_project_update.md
│   ├── skill3_role_based_system_criteria_comparison.md
│   ├── 09_EXTERNAL_TEST_TOOLS_SKILL.md
│   └── 10_SKILL_ORGANIZATION_AND_LOAD_ORDER.md
├── governance/
│   ├── 01_SECURITY_DEFENSE_CHECKLIST.md
│   ├── 02_DEMO_READINESS_QUALITY_GATE.md
│   ├── 03_ARCHITECTURE_DECISION_RECORDS.md
│   ├── 04_LOCAL_RUNBOOK_TROUBLESHOOTING.md
│   ├── 05_LIGHTWEIGHT_CI_CD_POLICY.md
│   ├── 06_API_CONTRACT_COMPATIBILITY.md
│   ├── 07_DATABASE_MIGRATION_ROLLBACK.md
│   └── 08_AI_RAG_EVALUATION_SAFETY.md
├── project-specific/
│   ├── BUSINESS_RULES.md
│   ├── COMPREHENSIVE_TEST_GUIDE.md
│   ├── ONBOARDING.md
│   └── handoff/
└── evidence/
    ├── test-reports/
    ├── screenshots/
    ├── logs/
    └── demo/
```

If the project already has a folder structure, do not force this exact structure.  
Instead, adapt the idea:

```text
- Workflow files together
- Skill files together
- Governance files together
- Project-specific files together
- Evidence files together
```

---

## 6. Skill Registry Requirement

Maintain a skill registry.

Every skill must be registered with:

```text
- Skill ID
- File name
- Layer
- Purpose
- When to use
- When not to use
- Inputs
- Outputs
- Depends on
- Feeds into
- Priority
```

Template:

| Skill ID | File | Layer | Purpose | Use When | Do Not Use When | Depends On | Feeds Into |
|---|---|---|---|---|---|---|---|
| SK-00 | 00_INDEX.md | Governance | List docs/skills | Finding files | Doing implementation | None | All |
| SK-01 | WORKFLOW_GUIDE_PROFESSIONAL.md | Foundation | Main workflow | Any coding task | Pure writing task | None | All implementation |
| SK-02 | skill2.md | Implementation | Existing project update | Updating current code | New project from scratch | Workflow | Test/handoff |
| SK-03 | skill3_role_based_system_criteria_comparison.md | Analysis | Role/system comparison | Admin/Mod/User logic | Pure UI styling | Workflow | Implementation |
| SK-09 | 09_EXTERNAL_TEST_TOOLS_SKILL.md | Verification | External test tools | Need tool evidence | No test needed | Test guide | Demo readiness |
| SK-10 | 10_SKILL_ORGANIZATION_AND_LOAD_ORDER.md | Governance | Skill ordering | Many skills exist | Single simple task | Index | All skills |

Update this registry whenever a skill is added, removed, renamed, or split.

---

## 7. Skill Load Order

Use this default order.

```text
1. Index / registry
2. Foundation workflow
3. Project-specific context
4. Main task skill
5. Supporting analysis skills
6. Implementation skill
7. Testing / evidence skill
8. Handoff / delivery skill
```

Example for role-based feature fix:

```text
1. 00 index
2. WORKFLOW_GUIDE_PROFESSIONAL.md
3. BUSINESS_RULES.md
4. skill3_role_based_system_criteria_comparison.md
5. skill2.md
6. 09_EXTERNAL_TEST_TOOLS_SKILL.md if testing with tools
7. 02_DEMO_READINESS_QUALITY_GATE.md
```

Example for API/UI mismatch:

```text
1. WORKFLOW_GUIDE_PROFESSIONAL.md
2. 06_API_CONTRACT_COMPATIBILITY.md
3. skill2.md
4. 09_EXTERNAL_TEST_TOOLS_SKILL.md
5. Handoff summary
```

Example for AI/RAG testing:

```text
1. WORKFLOW_GUIDE_PROFESSIONAL.md
2. BUSINESS_RULES.md
3. COMPREHENSIVE_TEST_GUIDE.md
4. 08_AI_RAG_EVALUATION_SAFETY.md
5. 09_EXTERNAL_TEST_TOOLS_SKILL.md
6. 02_DEMO_READINESS_QUALITY_GATE.md
```

---

## 8. Skill Activation Rules

Before using a skill, answer:

```text
1. Does the task require this skill?
2. Is this skill the main skill or only support?
3. Is there a more specific skill for this task?
4. Would using this skill duplicate another skill?
5. What output should this skill produce?
```

Use a skill only if it changes the quality of the output.

Do not use a skill just because the filename sounds related.

---

## 9. Main Skill vs Supporting Skill

Every task should have only one main skill.

Supporting skills may be used only when needed.

Examples:

| Task | Main Skill | Supporting Skill |
|---|---|---|
| Pull latest code and update UI/backend | Existing Project Update | Workflow, API Contract |
| Compare Admin/Moderator/User | Role-Based Comparison | Business Rules, Security Checklist |
| Test using external tools | External Test Tools | Comprehensive Test Guide, Demo Gate |
| Prepare for defense | Demo Readiness | Security, Runbook, Test Evidence |
| Add new skill | Skill Ecosystem Orchestration | Skill Organization |

Rule:

```text
If two skills both look like the main skill, choose the more specific one.
```

---

## 10. Duplicate and Overlap Control

When two skills overlap, use this process:

```text
1. Identify overlapping sections.
2. Decide which skill owns the topic.
3. Make the other skill reference the owner skill.
4. Remove or shorten duplicated instructions if needed.
5. Update the index/registry.
```

Ownership examples:

| Topic | Owner Skill/File |
|---|---|
| General workflow | WORKFLOW_GUIDE_PROFESSIONAL.md |
| Skill relationships | SKILL_ECOSYSTEM_ORCHESTRATION_GUIDE.md |
| File order and naming | 10_SKILL_ORGANIZATION_AND_LOAD_ORDER.md |
| Role comparison | skill3_role_based_system_criteria_comparison.md |
| Existing project update | skill2.md |
| External test tools | 09_EXTERNAL_TEST_TOOLS_SKILL.md |
| Demo readiness | 02_DEMO_READINESS_QUALITY_GATE.md |
| API contract | 06_API_CONTRACT_COMPATIBILITY.md |
| DB migration | 07_DATABASE_MIGRATION_ROLLBACK.md |

---

## 11. Skill Priority Rules

If skill instructions conflict, use this priority order:

```text
1. User's explicit current instruction
2. Project evidence and current code
3. Safety/security requirements
4. Business rules
5. Foundation workflow
6. Main task skill
7. Supporting skill
8. General best practice
```

Important:

```text
A supporting skill cannot override the main task skill.
A task skill cannot override repository safety rules.
A testing skill cannot claim code is correct if business rules fail.
A UI skill cannot ignore backend permission.
A delivery skill cannot claim tests passed without evidence.
```

---

## 12. Skill Update Rules

When a skill is added or modified:

```text
1. Assign a clear number or ID.
2. Put it in the correct folder/category.
3. Register it in the skill registry.
4. Add when-to-use and when-not-to-use.
5. Define dependencies.
6. Define outputs.
7. Define which skills consume the output.
8. Update load order examples if needed.
9. Update the zip/index if distributing as a pack.
```

Do not add random skill files without registering them.

---

## 13. Naming Rules for New Skills

Good names:

```text
09_EXTERNAL_TEST_TOOLS_SKILL.md
10_SKILL_ORGANIZATION_AND_LOAD_ORDER.md
API_CONTRACT_COMPATIBILITY.md
DATABASE_MIGRATION_ROLLBACK.md
```

Bad names:

```text
newskill.md
skill_final.md
skill_final_final.md
random.md
guide2.md
test123.md
```

Filename should clearly answer:

```text
What is this skill about?
```

---

## 14. Skill Pack Release Checklist

Before giving the user a skill pack/zip:

```text
- [ ] Every file has a clear purpose.
- [ ] Every file name is understandable.
- [ ] Index is updated.
- [ ] No duplicate numbering.
- [ ] No random old draft files included.
- [ ] Skill registry is updated.
- [ ] New skill relationships are documented.
- [ ] Load order examples are updated.
- [ ] No project-specific secret or credential included.
- [ ] Zip contains the latest versions only.
```

---

## 15. Clean-up Rules

If files become messy:

```text
1. List all skill files.
2. Identify duplicates.
3. Identify outdated files.
4. Identify files with wrong numbering.
5. Decide keep/merge/delete/archive.
6. Update index.
7. Create a clean zip.
```

Suggested archive folder:

```text
_archive/
```

Do not delete old files if they may contain useful history.  
Archive them instead.

---

## 16. Output Format for Skill Organization Task

When asked to organize skills, output:

```text
## Skill Organization Report

### 1. Files Found
| File | Type | Keep/Merge/Rename/Archive | Reason |

### 2. Recommended Structure
<folder tree>

### 3. Skill Registry
| Skill ID | File | Layer | Purpose | Depends On | Feeds Into |

### 4. Load Order
Default order:
Task-specific examples:

### 5. Conflicts / Duplicates
| Issue | Files | Fix |

### 6. Final Clean Pack
Files included:
Files excluded:
Notes:
```

---

## 17. Ready-to-Use Prompt

Use this prompt when asking an AI Agent to organize a skill set.

```text
You are managing a multi-skill AI Agent workflow system.

Do not use skills randomly.
Do not create messy duplicate skill files.
Do not rename files without updating references.
Do not add a new skill without registering it.

Your task:
1. List all current skill/workflow/governance files.
2. Classify them by layer:
   - Index/Governance
   - Foundation Workflow
   - Analysis/Comparison
   - Implementation
   - Verification/Delivery
   - Project-Specific Context
3. Identify duplicate or overlapping skills.
4. Decide the correct main skill and supporting skills for each common task.
5. Create or update a skill registry.
6. Define load order.
7. Recommend folder structure.
8. Update index files.
9. Produce a clean final pack with only the latest needed files.

Current files:
[PASTE FILE LIST HERE]

Current task:
[PASTE TASK HERE]
```

---

## 18. Final Rule

A skill system is organized only when the AI Agent can answer:

```text
1. Which skill should I use first?
2. Which skill is the main skill?
3. Which skills are supporting only?
4. Which skill should not be used?
5. What output does each skill produce?
6. What file owns each topic?
7. Where should a new skill be registered?
8. What is the correct final handoff?
```

If the AI Agent cannot answer these, the skill system is not organized enough.
