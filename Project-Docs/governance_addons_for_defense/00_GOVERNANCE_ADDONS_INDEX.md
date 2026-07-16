# Governance Add-ons for Local Project Defense

## Purpose

This folder adds the missing governance documents for a project that is mainly used for local development and academic defense, not commercial production.

These files are intentionally separated by topic so the team can use only what is needed.

## How to Use With Existing Documents

Use these files together with the current project documents:

| Existing Document | Role |
|---|---|
| `WORKFLOW_GUIDE_PROFESSIONAL.md` | Main AI agent workflow |
| `SKILL_ECOSYSTEM_ORCHESTRATION_GUIDE.md` | How skills support each other |
| `BUSINESS_RULES.md` | Source of truth for business behavior |
| `COMPREHENSIVE_TEST_GUIDE.md` | Source of truth for test execution |
| `skill2.md` | Existing project update workflow |
| `skill3_role_based_system_criteria_comparison.md` | Role/system comparison workflow |

## Added Files

| File | Topic | Use When |
|---|---|---|
| `01_SECURITY_DEFENSE_CHECKLIST.md` | Security and permission defense | Before role/security demo |
| `02_DEMO_READINESS_QUALITY_GATE.md` | Demo/submission readiness | Before presenting to committee |
| `03_ARCHITECTURE_DECISION_RECORDS.md` | Architecture decision records | When explaining why design choices were made |
| `04_LOCAL_RUNBOOK_TROUBLESHOOTING.md` | Local runbook and troubleshooting | When setup/demo fails |
| `05_LIGHTWEIGHT_CI_CD_POLICY.md` | Git/branch/PR/check policy | Before commit/push/PR |
| `06_API_CONTRACT_COMPATIBILITY.md` | API request/response contract | When UI and backend must match |
| `07_DATABASE_MIGRATION_ROLLBACK.md` | DB migration and rollback safety | Before schema/data changes |
| `08_AI_RAG_EVALUATION_SAFETY.md` | AI/RAG quality and safety | Before demoing AI/RAG features |

## Recommended Defense Flow

```text
1. Read BUSINESS_RULES.md to know expected behavior.
2. Read WORKFLOW_GUIDE_PROFESSIONAL.md before asking AI to edit code.
3. Use skill3_role_based_system_criteria_comparison.md for Admin/User/role logic.
4. Use 01_SECURITY_DEFENSE_CHECKLIST.md for auth/role/data safety.
5. Use COMPREHENSIVE_TEST_GUIDE.md for actual test execution.
6. Use 02_DEMO_READINESS_QUALITY_GATE.md before presentation.
7. Use 04_LOCAL_RUNBOOK_TROUBLESHOOTING.md if local services fail.
8. Use 03_ARCHITECTURE_DECISION_RECORDS.md to answer committee design questions.
```

## Principle

For a local academic project:

```text
Do not over-engineer like a production company system.
But document enough to prove:
- the system is understandable,
- the core logic is correct,
- security basics are handled,
- tests/evidence exist,
- the team can recover during demo.
```
