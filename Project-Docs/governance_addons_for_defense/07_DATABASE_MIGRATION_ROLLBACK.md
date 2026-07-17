# Database Migration and Rollback Playbook

## 1. Purpose

This file defines safe handling for database schema/data changes in a local academic project.

Use when:

```text
- Adding a table
- Adding/removing/changing a column
- Adding indexes
- Changing enum/status values
- Changing relationships
- Adding seed data
- Changing migration files
```

## 2. Main Rule

Database changes are high-risk because they can break the app even if code compiles.

Before changing schema, confirm:

```text
- Why the change is needed
- Which feature requires it
- Which existing code uses the affected table/field
- Whether data migration is needed
- How to rollback locally
```

## 3. Migration Proposal Template

```text
Migration Name:
Feature/Requirement:
Reason:
Affected tables:
Affected columns:
New constraints:
Indexes:
Seed data:
Backward compatibility:
Risk:
Rollback plan:
Test plan:
```

## 4. Migration Safety Checklist

| ID | Check | Expected | Status |
|---|---|---|---|
| DB-01 | Requirement needs schema change | Not just convenience | Not Checked |
| DB-02 | Existing schema inspected | No duplicate field/table | Not Checked |
| DB-03 | Existing code impact checked | Affected queries known | Not Checked |
| DB-04 | Null/default value considered | Existing rows safe | Not Checked |
| DB-05 | Foreign keys considered | Relationships valid | Not Checked |
| DB-06 | Index needed for search/filter | Performance considered | Not Checked |
| DB-07 | Migration can apply locally | No error | Not Checked |
| DB-08 | App builds after migration | Build pass | Not Checked |
| DB-09 | Feature works after migration | Manual/API test pass | Not Checked |
| DB-10 | Rollback/reset plan exists | Demo recovery possible | Not Checked |

## 5. Destructive Change Warning

These changes require extra caution:

```text
- Dropping a table
- Dropping a column
- Renaming a column
- Changing column type
- Deleting seed data
- Hard-deleting user/content records
- Changing enum/status values used by code
```

For local academic projects, destructive changes are acceptable only if:

```text
- Team understands data loss
- Test DB can be reset
- Migration is documented
- Demo data can be recreated
```

## 6. Local Rollback Options

Depending on project stack:

```text
Option A: Revert migration file and database update
Option B: Apply down migration
Option C: Reset local database and reapply migrations
Option D: Restore from local backup/dump
Option E: Recreate test data from seed script
```

Fill project-specific command:

```text
Apply migration:
Rollback migration:
Reset local DB:
Seed test data:
Verify schema:
```

## 7. Data Backup Before Risky Change

Before risky local DB change:

```text
- [ ] Export important test data if needed.
- [ ] Record current branch/commit.
- [ ] Record migration list.
- [ ] Confirm reset/reseed command.
```

## 8. Migration Evidence Format

```text
Migration:
Command run:
Result:
Affected schema:
Build result:
Test result:
Manual verification:
Rollback plan:
```
