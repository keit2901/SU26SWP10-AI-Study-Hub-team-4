# Session Log — DB Audit & ERD Diagrams (2026-07-19)

## Summary
- Full audit of all 22 entity tables for unused fields/tables
- Cleaned up: removed QuizAttempt (dead table), Quiz.ErrorCode, Plan.FeatureFlagsJson
- Created 2 ERD diagrams in draw.io format

## Cleanup Performed
### Deleted
- **QuizAttempt** (entity, config, DbSet) — all 7 fields unused, table `quiz_attempts` entirely dead
- **Quiz.ErrorCode** — field `error_code` declared in entity/config/migration but never read/written
- **Plan.FeatureFlagsJson** — field `feature_flags_json` declared but never populated/consumed

### Verified with build
- `dotnet build` → 0 errors, all warnings pre-existing

## Remaining tables after cleanup: **21 tables**

## ERD Diagrams Created
1. **ERD_Modern.drawio** (29KB) — Crow's foot table notation
   - Color-coded by domain (Auth purple, Content sky, Chat emerald, etc.)
   - 21 entity boxes with columns displayed
   - 27 relationship edges
   
2. **ERD_Chen.drawio** (27KB) — Chen ER notation (original user style)
   - Rectangle entities, diamond relationships, attribute text
   - 21 entities, 17 relationships, cardinality labels
   - Monochrome style matching user's original hand-drawn ERD

## Files changed/created
- Deleted: `Data/Entities/QuizAttempt.cs`, `Data/Configurations/QuizAttemptConfiguration.cs`
- Edited: `Data/Entities/Quiz.cs` (removed ErrorCode, Attempts nav prop)
- Edited: `Data/Entities/Plan.cs` (removed FeatureFlagsJson)
- Edited: `Data/Configurations/QuizConfiguration.cs` (removed ErrorCode config)
- Edited: `Data/Configurations/PlanConfiguration.cs` (removed FeatureFlagsJson config)
- Edited: `Data/AppDbContext.cs` (removed QuizAttempt DbSet)
- Created: `ERD_Modern.drawio`
- Created: `ERD_Chen.drawio`
