# Lightweight CI/CD and Git Policy

## 1. Purpose

This file defines a simple Git, branch, check, and PR policy for a local academic/team project.

It is not enterprise CI/CD.  
It is meant to reduce merge conflicts, broken builds, and unclear commits.

## 2. Branch Rules

Recommended branch types:

```text
feature/<short-name>
fix/<short-name>
docs/<short-name>
test/<short-name>
refactor/<short-name>
chore/<short-name>
```

Examples:

```text
feature/admin-user-filter
fix/inactive-user-login
docs/demo-readiness-guide
test/rag-quality-cases
```

## 3. Before Starting Work

```text
- [ ] Check current branch.
- [ ] Pull latest target branch if required.
- [ ] Confirm there are no unknown local changes.
- [ ] Create or switch to correct working branch.
- [ ] Read relevant docs/requirements.
```

## 4. Before Commit

```text
- [ ] Only relevant files changed.
- [ ] No secrets committed.
- [ ] No unrelated formatting changes.
- [ ] Build/test run or reason documented.
- [ ] Manual test steps documented if needed.
- [ ] Business rules still respected.
- [ ] API contract still matches UI if relevant.
```

## 5. Commit Message

Use project style if one exists.  
If not, use:

```text
feat: add ...
fix: prevent ...
docs: update ...
test: add ...
refactor: simplify ...
chore: update ...
```

Examples:

```text
fix: prevent inactive users from logging in
feat: add admin report status filter
docs: add demo readiness quality gate
test: add upload failure cases
```

## 6. Push Rules

Do not push if:

```text
- Build is broken and not documented.
- Critical test fails.
- Secret file is included.
- Commit includes unrelated large refactor.
- Branch is not intended for this task.
```

## 7. Pull Request Checklist

```text
## Summary
- ...

## Changes
- ...

## Testing
- ...

## Evidence
- ...

## Risk
- ...

## Checklist
- [ ] Build passes or failure explained
- [ ] Critical tests pass or failure explained
- [ ] No unrelated files changed
- [ ] No secrets committed
- [ ] Business rules respected
- [ ] UI/API contract checked if relevant
```

## 8. Lightweight CI Expectations

If CI exists, it should ideally check:

```text
- Build
- Unit tests
- Lint/type check if project has it
- Secret scanning if available
```

If CI does not exist, local manual checks are acceptable for academic defense, but the result must be recorded.

## 9. Failed CI / Failed Local Test Rule

If a check fails:

```text
1. Do not hide it.
2. Record the command.
3. Record the failure.
4. Decide whether it is caused by your change.
5. Fix if in scope.
6. If not in scope, document as known issue.
```

## 10. Merge Rule

Before merge:

```text
- [ ] Reviewer or team lead has checked the change if required.
- [ ] Demo-critical flow still works.
- [ ] Branch is up to date enough to avoid obvious conflicts.
- [ ] PR description is complete.
```
