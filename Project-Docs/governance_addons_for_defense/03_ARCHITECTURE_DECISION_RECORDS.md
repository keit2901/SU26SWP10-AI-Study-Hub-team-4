# Architecture Decision Records

## 1. Purpose

Architecture Decision Records, or ADRs, explain important technical decisions.

For academic defense, ADRs help answer:

```text
- Why did you choose this framework?
- Why did you design the system this way?
- Why did you use this database?
- Why did you separate roles this way?
- Why did you choose this chunking/retrieval method?
- What alternatives did you consider?
- What are the trade-offs?
```

Do not record every small code change.  
Record decisions that affect architecture, data flow, security, AI/RAG, database, or major project direction.

## 2. ADR Template

```text
ADR ID:
Title:
Status: Proposed / Accepted / Deprecated / Superseded
Date:
Owner:

Context:
What problem or requirement caused this decision?

Decision:
What did the team decide?

Options Considered:
1.
2.
3.

Reason:
Why was this option selected?

Consequences:
Positive:
-
Negative / Trade-off:
-

Affected Areas:
- UI:
- Backend:
- Database:
- AI/RAG:
- Testing:
- Documentation:

Risk:
Low / Medium / High

Rollback / Alternative Plan:
How can this decision be changed later if needed?

Evidence:
- File:
- Test:
- Report:
- Screenshot:
```

## 3. ADR Register

| ADR ID | Title | Status | Date | Notes |
|---|---|---|---|---|
| ADR-001 | Project stack selection | Proposed | YYYY-MM-DD | Fill in |
| ADR-002 | Authentication and role model | Proposed | YYYY-MM-DD | Fill in |
| ADR-003 | Document upload and ingestion pipeline | Proposed | YYYY-MM-DD | Fill in |
| ADR-004 | Chunking strategy | Proposed | YYYY-MM-DD | Fill in |
| ADR-005 | Embedding model / vector search approach | Proposed | YYYY-MM-DD | Fill in |
| ADR-006 | Admin/Moderator/User permission separation | Proposed | YYYY-MM-DD | Fill in |
| ADR-007 | Local demo fallback strategy | Proposed | YYYY-MM-DD | Fill in |

---

# ADR-001 — Project Stack Selection

## Status

Proposed / Accepted

## Context

The team needed a stack suitable for UI, backend, database, authentication, testing, and local demo requirements.

## Decision

Use the current project stack and keep future changes consistent with that stack.

## Options Considered

```text
1. Keep current stack
2. Rewrite with another stack
3. Split frontend and backend into separate systems
```

## Reason

Keeping the current stack reduces risk, preserves existing code and tests, and makes the project easier to explain during defense.

## Consequences

Positive:

```text
- Less rewrite risk
- Easier local setup
- Existing tests remain useful
- Architecture is consistent
```

Negative / Trade-off:

```text
- Team must follow current framework conventions
- Some features may require project-specific implementation
```

## Rollback / Alternative Plan

Only consider major stack changes if the current stack blocks a critical requirement.

---

# ADR-002 — Backend Permission Enforcement

## Status

Proposed / Accepted

## Context

The UI may hide restricted actions, but users can still call backend APIs directly.

## Decision

Backend authorization must enforce permissions for protected actions.

## Options Considered

```text
1. UI-only hiding
2. Backend-only enforcement
3. Both UI visibility and backend enforcement
```

## Reason

UI-only hiding is not enough. Backend enforcement prevents direct API misuse and is easier to defend during review.

## Consequences

Positive:

```text
- Safer role separation
- Clearer permission logic
- Better testability
```

Negative / Trade-off:

```text
- More backend checks and tests are needed
```
