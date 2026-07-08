### 2026-07-06T16:00:00+07:00 - Header-anchored filter popup positioning
- Updated filter/sort popup placement so each popup opens directly below the header button that was clicked instead of staying fixed at the screen edge
- Added a shared browser helper in `AI_Study_Hub_v2/Components/App.razor` to read header button viewport coordinates
- Applied header-anchored floating menu positioning in:
  - `AI_Study_Hub_v2/Components/Pages/Dashboard/AnalyticsDashboard.razor`
  - `AI_Study_Hub_v2/Components/Pages/Dashboard/DocumentDashboard.razor`
  - `AI_Study_Hub_v2/Components/Pages/Dashboard/FolderTable.razor`
  - `AI_Study_Hub_v2/Components/Pages/Dashboard/SemestersDashboard.razor`
  - `AI_Study_Hub_v2/Components/Pages/Dashboard/SubjectsDashboard.razor`
  - `AI_Study_Hub_v2/Components/Pages/DocumentLibrary.razor`
- Kept the popup independent from table clipping while restoring the expected "open right under this header" behavior

### 2026-07-06T16:00:00+07:00 - Compile verification after header anchoring update
- Ran `dotnet msbuild "AI_Study_Hub_v2\AI_Study_Hub_v2.csproj" /t:Compile /p:BuildProjectReferences=false /nologo`
- Result:
  - compile succeeded
  - remaining warnings are pre-existing project warnings plus the existing MudBlazor analyzer warning on `MudChart`
