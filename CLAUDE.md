# AI Study Hub v2 — Hermes Agent Context

## Stack
- .NET 8 / ASP.NET Core / Blazor Server (Interactive Server)
- MudBlazor 9.4, scoped `.razor.css` + `wwwroot/app.css`
- EF Core 8 + Npgsql + pgvector (Supabase PostgreSQL)
- Supabase GoTrue JWT auth
- RAG: PdfPig text extraction → chunking → pgvector search → Groq chat completion
- Tests: NUnit + FluentAssertions + Moq

## Key paths
- Solution: `AI_Study_Hub_v2/AI_Study_Hub_v2.sln`
- Pages: `AI_Study_Hub_v2/Components/Pages/`
- Controllers: `AI_Study_Hub_v2/Controllers/`
- Services: `AI_Study_Hub_v2/Services/`
- RAG: `AI_Study_Hub_v2/Services/Rag/`
- Tests: `AI_Study_Hub_v2.Tests/`
- Session logs: `D:\FPT\summer2026\SWP391\previous_session\`

## Quick commands
```powershell
rtk dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo
rtk dotnet test "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo --no-build
rtk git status --short --branch
```

## UI theme
Dark premium purple/cyan, glass cards, MudBlazor components.
