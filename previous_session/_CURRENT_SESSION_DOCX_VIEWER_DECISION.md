# Session Log — 2026-06-18 — DOCX Viewer Decision & Suspension

## Decision
**Approach 3 — Option A (Gotenberg Docker)** selected for DOCX/PPTX viewing:
- Use `gotenberg/gotenberg:8` Docker container (MIT license, LibreOffice-powered)
- Convert DOCX/PPTX → PDF on view via HTTP API
- Unified PDF viewer for all document formats
- Implementation deferred to future sprint

## Suspension
- **OfficeViewerDialog.razor** and related backend (`GetFileUrlAsync`, `GET /api/documents/{id}/file`) code preserved but UI hidden
- "View Original" action card in `DocumentDetail.razor` (lines 220-233) commented out with `@* SUSPENDED *@` marker
- All C# code-behind methods (`OpenOfficeViewerAsync`, `IsOfficeViewerSupported`, `OfficeViewerFormatLabel`) kept intact

## Work done
1. **Researched 3 approaches** for DOCX/PPTX viewing:
   - **A1** (same as PDF text chunks) — text only, no visual rendering
   - **A2** (docxjs) — high-fidelity but DOCX-only, no PPTX support
   - **A3** (convert to PDF) — unified solution covering both formats
2. **Deep-dived A3 sub-options**:
   - Gotenberg (Docker) — recommended: free, simple HTTP API, both formats
   - LibreOffice direct — thread-safety issues, heavy
   - Syncfusion/Aspose — expensive or legally restricted for students
   - GemBox — free tier too limited (20 paragraphs / 5 slides)
3. **Suspended current Office Viewer** — commented out Razor block in DocumentDetail.razor

## Build status
- `dotnet build` — verified (see below)

## Files changed
- `Components/Pages/DocumentDetail.razor` — commented out Office Viewer action card (lines 220-233)

## Key state
- Working directory: `D:\Project\AI_Study_Hub\SU26SWP10-AI-Study-Hub-team-4\SU26SWP10-AI-Study-Hub-team-4`
- Active branch: `sprint2/integration`
