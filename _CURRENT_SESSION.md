## [2026-06-16 12:00] Layout fix: DocumentLibrary center-stage full width

### What
- Fixed empty left/right margin space on DocumentLibrary page (free space at corners issue)
- Root cause: `MainLayout.razor.css:70-74` — `.content` class constrains page to `min(1180px, calc(100vw - 2rem))` with `margin: 0 auto`, centering content with empty margins on large viewports
- Fix: Extended `IsFullBleed` in `MainLayout.razor:23-27` to include `/documents` route (previously only `/ai/chat` was full-bleed)

### Files changed
- `Components/Layout/MainLayout.razor:23-27` — `IsFullBleed` now matches both `ai/chat` and `documents` paths

### Build
- `dotnet build` — 0 errors, 0 warnings
