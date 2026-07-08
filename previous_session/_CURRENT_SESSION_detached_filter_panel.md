### 2026-07-06T15:36:00+07:00 - Detached filter/sort panels from table layout
- Updated shared filter-menu presentation across the dashboard-style tables so popup panels no longer depend on the width or clipping of each table header cell
- Switched filter/sort menus from header-bound absolute positioning to viewport-fixed floating panels in:
  - `AI_Study_Hub_v2/Components/Pages/DocumentLibrary.razor`
  - `AI_Study_Hub_v2/Components/Pages/Dashboard/DocumentDashboard.razor`
  - `AI_Study_Hub_v2/Components/Pages/Dashboard/AnalyticsDashboard.razor`
  - `AI_Study_Hub_v2/Components/Pages/Dashboard/FolderTable.razor`
  - `AI_Study_Hub_v2/Components/Pages/Dashboard/SemestersDashboard.razor`
  - `AI_Study_Hub_v2/Components/Pages/Dashboard/SubjectsDashboard.razor`
- Kept existing filter/sort behavior and reset logic unchanged; only the floating panel placement and sizing were redesigned

### 2026-07-06T15:36:00+07:00 - Compile verification after detached filter panel update
- Ran `dotnet msbuild "AI_Study_Hub_v2\AI_Study_Hub_v2.csproj" /t:Compile /p:BuildProjectReferences=false /nologo`
- Result:
  - compile succeeded
  - remaining warnings are pre-existing project warnings plus the existing MudBlazor analyzer warning on `MudChart`
