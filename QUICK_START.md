# AI Study Hub v2 - Quick Start

Setup guide moved to [`setup tutorial/README.md`](setup%20tutorial/README.md).

Start here:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

If Docker is not running and you only need local `.env` + app user-secrets:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1 -SkipDocker -SkipBuild
```

Set your own AI provider key locally, never commit it:

```powershell
dotnet user-secrets set "Groq:ApiKey" "<your-groq-api-key>" --project AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
```
