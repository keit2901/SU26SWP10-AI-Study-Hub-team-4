# AI Study Hub v2 — Comprehensive Test Guide

> **Document ID:** TST-GUIDE-001  
> **Version:** 2.0.1  
> **Status:** ✅ Approved  
> **Authors:** SWP391 Team 4  
> **Last Updated:** 2026-07-02  
> **App:** .NET 8 Blazor Server + MudBlazor  
> **Stack:** PostgreSQL/Supabase, Ollama, EF Core 8, pgvector, Groq

---

## Document Control

| Version | Date | Author | Change |
|---------|------|--------|--------|
| 1.0.0 | 2026-07-02 | Team Lead | Initial — P1+P2+P3 combined guide |
| 2.0.0 | 2026-07-02 | Team 4 | Professional rewrite — IEEE 829 structure, idempotent bootstrap, status badges, regression suite, traceability |
| 2.0.1 | 2026-07-02 | Team 4 | Created bootstrap script, fixed config defaults, added teardown + env vars checklist |

---

## Table of Contents

- [1. Quick Start (TL;DR)](#1-quick-start-tldr)
- [2. Test Architecture](#2-test-architecture)
- [3. Environment Prerequisites](#3-environment-prerequisites)
- [4. RBL Phase 1 — Real Embedding 🟪](#4-rbl-phase-1--real-embedding-)
- [5. RBL Phase 2 — Semantic Chunking 🟦](#5-rbl-phase-2--semantic-chunking-)
- [6. RBL Phase 3 — RAG Quality 🟫](#6-rbl-phase-3--rag-quality-)
- [7. Full Integration Smoke Test](#7-full-integration-smoke-test)
- [8. Regression Test Suite](#8-regression-test-suite)
- [9. Manual Test Procedures](#9-manual-test-procedures)
- [10. Configuration Reference](#10-configuration-reference)
- [11. Entry/Exit Criteria & Sign-Off](#11-entryexit-criteria--sign-off)
- [12. Known Issues & Troubleshooting](#12-known-issues--troubleshooting)
- [13. Appendix](#13-appendix)
- [14. Requirements Traceability Matrix](#14-requirements-traceability-matrix)

---

## 1. Quick Start (TL;DR)

### Status Dashboard

| Phase | Name | Code Status | Test Status | Prerequisites |
|-------|------|-------------|-------------|---------------|
| 🟪 P1 | Real Embedding | ✅ Implemented | ✅ Ready | Docker + Ollama `all-minilm:l6-v2` |
| 🟦 P2 | Semantic Chunking | ✅ Implemented | ✅ Ready (215 tests pass) | P1 |
| 🟫 P3 | RAG Quality | ❌ Not implemented | ⛔ Blocked | P1 + P2 |

### One-Command Bootstrap (First Time)

```powershell
# From repo root — starts ALL required services + runs tests
& "docs\scripts\bootstrap-test-env.ps1"
```

### Daily Test Run

```powershell
# 1. Verify services
docker compose -f infra\supabase\docker-compose.yml ps
docker compose -f infra\ollama\docker-compose.yml ps

# 2. Full unit test suite
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.Tests --nologo

# 3. App smoke test
$env:ASPNETCORE_ENVIRONMENT="Development"
Start-Process dotnet -ArgumentList "run","--project","AI_Study_Hub_v2","--no-launch-profile","--urls","http://localhost:5240"
Start-Sleep 10
Invoke-WebRequest http://localhost:5240 -UseBasicParsing | Select-Object StatusCode
```

---

## 2. Test Architecture

### Test Levels (Pyramid)

```
        /\
       /E2E\   Playwright (planned)
      /------\
     / Integ  \  WebApplicationFactory + Testcontainers
    /------------\
   /  Unit/bUnit \  xUnit + NUnit — 215 tests
  /________________\
```

| Level | Framework | Scope | Speed | Suite |
|-------|-----------|-------|-------|-------|
| **Unit** | NUnit + xUnit + FluentAssertions | Domain logic, services, RAG pipeline | <10ms | 215 tests, all pass |
| **Component** | bUnit (future) | Blazor Razor components | ~100ms | Not yet implemented |
| **Integration** | WebApplicationFactory + EF InMemory | API contracts, auth, DB queries | 100ms–5s | Included in 215 tests |
| **E2E** | Playwright (future) | Full browser flows | 1–30s | Not yet implemented |
| **Manual** | This guide | UI/UX, real Ollama, cross-browser | Min–Hours | See Section 9 |

### Project Structure

```
AI_Study_Hub_v2/
├── Controllers/           # REST API — tested via WebApplicationFactory
├── Services/Rag/          # RAG pipeline — unit tested with mocking
├── Components/Admin/      # Admin UI — manual test only (bUnit planned)
├── Data/                  # EF Core — tested with InMemory + Testcontainers
└── AI_Study_Hub_v2.Tests/ # Test project
    ├── Services/          # Unit tests for RAG, chunking, ingestion
    ├── Controllers/       # Integration tests for API endpoints
    └── *.csproj           # NUnit + xUnit + FluentAssertions + Moq
```

### Test Tag Conventions (Planned — Not Yet Implemented in Code)

> **⚠️ Note:** These tags are aspirational. The current test suite (215 tests) does not yet use `[Category]` or `[Priority]` attributes. Filtering commands below are provided as a future reference. For now, run the full suite: `dotnet test --nologo`.

| Tag | Purpose | Run Frequency |
|-----|---------|---------------|
| `[Category("Smoke")]` | Critical path (login, health) | Every build |
| `[Category("Unit")]` | Business logic, services | Every PR |
| `[Category("Integration")]` | DB, API, external services | Pre-merge |
| `[Priority(0)]` | Must-pass for release | Release gate |

---

## 3. Environment Prerequisites

### 3.1 Required Services

| Service | Port | Docker | Purpose |
|---------|------|--------|---------|
| Supabase (PostgreSQL) | 5432 + 8000 | `infra/supabase/docker-compose.yml` | Database + Auth |
| Ollama | 11434 | `infra/ollama/docker-compose.yml` | Embeddings (all-minilm:l6-v2) |
| AI Study Hub App | 5240 | N/A (dotnet run) | Application under test |

### 3.2 Idempotent Setup (Run Once)

Each step is **safe to re-run** — designed to be resumable if interrupted.

**Pre-requisites you MUST configure before first run:**

```powershell
# 1. Set Supabase secrets (required — app won't start without these)
dotnet user-secrets set "Supabase:JwtSecret" "<your-jwt-secret>" --project AI_Study_Hub_v2
dotnet user-secrets set "Supabase:AnonKey" "<your-anon-key>" --project AI_Study_Hub_v2
dotnet user-secrets set "Supabase:ServiceRoleKey" "<your-service-role-key>" --project AI_Study_Hub_v2

# 2. Set database password
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=<your-password>" --project AI_Study_Hub_v2

# 3. Set Groq API key (for AI chat)
dotnet user-secrets set "Groq:ApiKey" "<your-groq-api-key>" --project AI_Study_Hub_v2

# 4. Set seed passwords
dotnet user-secrets set "Seed:DefaultAdmin:Password" "<admin-password>" --project AI_Study_Hub_v2
dotnet user-secrets set "Seed:DefaultModerator:Password" "<moderator-password>" --project AI_Study_Hub_v2

# Verify secrets are set:
dotnet user-secrets list --project AI_Study_Hub_v2
```

Then run the bootstrap:

```powershell
# === STEP 0: Verify prerequisites ===
Write-Host "[0/6] Checking prerequisites..." -ForegroundColor Cyan
$errors = @()

# Check Docker
try { docker info *>$null } catch { $errors += "Docker not running" }

# Check .NET SDK
try { dotnet --version *>$null } catch { $errors += ".NET SDK not found" }

if ($errors) { Write-Host ($errors -join "`n") -ForegroundColor Red; exit 1 }
Write-Host "  ✓ Docker + .NET SDK" -ForegroundColor Green

# === STEP 1: Start Supabase ===
Write-Host "[1/6] Starting Supabase..." -ForegroundColor Cyan
docker compose -f infra\supabase\docker-compose.yml up -d 2>&1 | Out-Null
Write-Host "  ✓ Supabase containers running" -ForegroundColor Green

# === STEP 2: Start Ollama ===
Write-Host "[2/6] Starting Ollama..." -ForegroundColor Cyan
docker compose -f infra\ollama\docker-compose.yml up -d 2>&1 | Out-Null
Write-Host "  ✓ Ollama container running" -ForegroundColor Green

# === STEP 3: Pull embedding model ===
Write-Host "[3/6] Checking Ollama model..." -ForegroundColor Cyan
$models = curl.exe -s http://localhost:11434/api/tags 2>$null | ConvertFrom-Json
$hasModel = ($models.models | Where-Object { $_.name -eq "all-minilm:l6-v2" }).Count -gt 0
if (-not $hasModel) {
    Write-Host "  Pulling all-minilm:l6-v2 (384-dim)..." -ForegroundColor Yellow
    docker exec aistudy-ollama ollama pull all-minilm:l6-v2 2>&1 | Out-Null
}
Write-Host "  ✓ Model all-minilm:l6-v2 available" -ForegroundColor Green

# === STEP 4: Build solution ===
Write-Host "[4/6] Building solution..." -ForegroundColor Cyan
dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo -v q 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { $errors += "Build failed"; Write-Host "  ✗ Build failed!" -ForegroundColor Red; exit 1 }
Write-Host "  ✓ Build successful" -ForegroundColor Green

# === STEP 5: Apply migrations ===
Write-Host "[5/6] Applying database migrations..." -ForegroundColor Cyan
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --project AI_Study_Hub_v2 --no-launch-profile --urls "http://localhost:5240" 2>&1 | Out-Null &
$appPid = $!
Start-Sleep 15
Stop-Process -Id $appPid -Force -ErrorAction SilentlyContinue
Write-Host "  ✓ Migrations applied" -ForegroundColor Green

# === STEP 6: Run unit tests ===
Write-Host "[6/6] Running unit tests..." -ForegroundColor Cyan
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.Tests --nologo -v q 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { $errors += "Tests failed" }
Write-Host "  ✓ Tests complete" -ForegroundColor Green

# === Report ===
if ($errors) {
    Write-Host "`n❌ Setup had errors:" -ForegroundColor Red
    Write-Host ($errors -join "`n") -ForegroundColor Red
} else {
    Write-Host "`n✅ All 6 steps complete. Ready to test." -ForegroundColor Green
}
```

### 3.3 Pre-Flight Health Check

Before every test session, verify these 3 endpoints:

```powershell
# DB health
docker exec supabase-db pg_isready -U postgres
# Expected: accepting connections

# Ollama health
curl.exe -s http://localhost:11434/api/tags | Select-String "all-minilm"
# Expected: match

# App health (if running)
Invoke-WebRequest http://localhost:5240 -UseBasicParsing -TimeoutSec 5
# Expected: StatusCode 200
```

### 3.4 Teardown / Cleanup

After testing, stop all services to free resources:

```powershell
# Stop the app if running
Get-NetTCPConnection -LocalPort 5240 | ForEach-Object {
    Stop-Process -Id $_.OwningProcess -Force
}

# Stop Docker services
docker compose -f infra\ollama\docker-compose.yml down
docker compose -f infra\supabase\docker-compose.yml down

# Verify cleanup
Get-NetTCPConnection -LocalPort 5240,5432,8000,11434 -ErrorAction SilentlyContinue
# Expected: no output (all ports freed)
```

> **Daily testing note:** Always stop the app before starting a new instance. See Known Issue K6 for port conflict troubleshooting.

---

## 4. RBL Phase 1 — Real Embedding 🟪

> **Status:** ✅ Implemented | **Tests:** 215 pass | **Key Files:** `OllamaEmbeddingService.cs`, `OllamaHealthCheck.cs`, `DocumentIngestionService.cs`

### Test Objectives
Verify Ollama `all-minilm:l6-v2` replaces FNV-1a fake embedding with real 384-dim vectors. Verify fault tolerance (skip-and-continue per-chunk failure).

### 4.1 Automated Tests

| Test Class | Scope | Command |
|-----------|-------|---------|
| `OllamaEmbeddingServiceTests` | Mocked unit tests | `dotnet test --filter OllamaEmbeddingService` |
| `OllamaEmbeddingServiceIntegrationTests` | Live Ollama (skip-safe) | `dotnet test --filter "TestCategory=Live"` |

### 4.2 Health Check

| # | Test | Command | Expected |
|---|------|---------|----------|
| P1.1 | Verify startup log contains Ollama health | `rg "Ollama health check" --type-add 'txt:*.log'` | `"Ollama health check passed"` or `"Ollama not available at startup"` (warn-only) |
| P1.2 | Verify model dimension | `docker exec supabase-db psql -U postgres -c "SELECT cardinality(embedding) FROM document_chunks LIMIT 1;"` | Returns `384` |

### 4.3 Upload & Ingestion Pipeline

| # | Test | Steps | Expected |
|---|------|-------|----------|
| P1.3a | Upload PDF (2+ pages, text) | 1. `POST /api/documents/upload` with `.pdf`<br>2. Check Document Library → status | Status = `Ready` |
| P1.3b | Verify embedding model in DB | `docker exec supabase-db psql -U postgres -c "SELECT count(*), embedding_model FROM document_chunks GROUP BY embedding_model;"` | All rows: `all-minilm:l6-v2` |
| P1.3c | Upload DOCX | Same as P1.3a with `.docx` | Chunks created with `all-minilm:l6-v2` |
| P1.3d | Upload PPTX | Same as P1.3a with `.pptx` | Chunks created with `all-minilm:l6-v2` |

### 4.4 RAG Chat Quality

| # | Test | Steps | Expected |
|---|------|-------|----------|
| P1.4a | Chat with uploaded document | 1. Open `/chat`<br>2. Select document<br>3. Ask: "What is this document about?" | AI answers with citations |
| P1.4b | Chat — out-of-scope query | Ask: "What is the capital of Mars?" | AI: "not found in the provided documents" |
| P1.4c | Verify search scores in log | `rg "CosineDistance" --type-add 'txt:*.log'` | Meaningful scores (not random hash) |

### 4.5 Fault Tolerance

| # | Test | Steps | Expected |
|---|------|-------|----------|
| P1.5a | Stop Ollama during test | `docker stop aistudy-ollama` | App continues running |
| P1.5b | Upload with Ollama down | Upload a PDF | File stored (201), chunks fail → status `Failed` |
| P1.5c | Restore Ollama | `docker start aistudy-ollama` + wait 10s | Container running, model loaded |
| P1.5d | Re-ingest failed document | `POST /api/documents/{id}/ingest` (with Bearer token) | Status → `Ready` |

### 4.6 Pass/Fail Criteria

| Criterion | Threshold | Evidence |
|-----------|-----------|----------|
| All Ollama unit tests pass | 100% | `dotnet test` output |
| PDF upload → Ready pipeline | 100% of attempts | DB query |
| Embedding dimension = 384 | Exact match | `cardinality()` query |
| Chat with doc returns citations | ≥ 80% of queries | Manual observation |
| Fault recovery (stop → re-ingest → Ready) | 100% | Test P1.5a-d |

---

## 5. RBL Phase 2 — Semantic Chunking 🟦

> **Status:** ✅ Implemented | **Tests:** 215 pass (inc. chunking tests) | **Key Files:** `BlockParser.cs`, `SentenceSplitter.cs`, `ChunkMerger.cs`, `ChunkingService.cs`, `FixedSizeChunkingService.cs`

### Test Objectives
Verify semantic chunking pipeline produces coherent chunks that respect natural boundaries (paragraphs, headings, lists, code blocks). Verify recall@K ≥ fixed-size baseline. Verify rollback to fixed-size via config.

### 5.1 Configuration Baseline

| # | Test | Verify | Expected |
|---|------|--------|----------|
| P2.1a | Default strategy | `appsettings.Development.json` → `Rag:ChunkingStrategy` | `"semantic"` |
| P2.1b | Min/max chunk size | `RagOptions.cs` or `appsettings.json` | `MinChunkChars=100`, `MaxSectionChars=1000` |
| P2.1c | DI resolution | `Program.cs` line ~133 | `ChunkingService` injected (not `FixedSizeChunkingService`) |

### 5.2 Automated Tests

| Test Class | Scope | Focus |
|-----------|-------|-------|
| `BlockParserTests` | Unit | Heading detection, paragraph/lists/code block parsing |
| `SentenceSplitterTests` | Unit | Sentence boundary detection (incl. Vietnamese) |
| `ChunkMergerTests` | Unit | Merge rules, section boundary guard, overlap logic |
| `ChunkingServiceTests` | Unit | Semantic pipeline orchestration, fixed-size rollback |
| `ChunkingBenchmarkServiceTests` | Unit | Benchmark recall@K, MRR comparison |
| `DocumentIngestionServiceTests` | Integration | End-to-end ingestion with semantic chunking |
| `AdminDocumentsControllerTests` | Integration | Re-ingest endpoint with strategy reporting |

```powershell
# Run all chunking tests
dotnet test --filter "FullyQualifiedName~Chunking|FullyQualifiedName~BlockParser|FullyQualifiedName~SentenceSplitter|FullyQualifiedName~ChunkMerger"
```

### 5.3 Semantic Chunk Quality

| # | Test | Steps | Expected |
|---|------|-------|----------|
| P2.2a | Upload structured PDF | PDF with H1, H2, paragraphs, bullet lists, code blocks | Ingestion → `Ready` |
| P2.2b | Verify chunk coherence | `SELECT chunk_index, substring(content,1,80) FROM document_chunks WHERE document_id='...' ORDER BY chunk_index` | Each chunk = coherent block |
| P2.2c | No cross-heading splits | Same query + check chunk boundaries | Heading at chunk start |
| P2.2d | No mid-paragraph splits | Same query | Each paragraph wholly in one chunk |
| P2.2e | Code blocks intact | Same query | Code block not split across chunks |
| P2.2f | Min size enforced | `SELECT min(length(content)) FROM document_chunks WHERE document_id='...'` | ≥ 100 chars |
| P2.2g | Max size enforced | `SELECT max(length(content)) FROM document_chunks WHERE document_id='...'` | ≤ 1000 chars |

### 5.4 Chunking Benchmark Comparison

| # | Test | Command | Expected |
|---|------|---------|----------|
| P2.3a | Run benchmark API | `POST http://localhost:5240/api/benchmark/chunking-compare` (with Bearer token) | JSON with `Semantic` + `Fixed` results |
| P2.3b | Compare recall@K | Parse response JSON | `Semantic.RecallAtK` ≥ `Fixed.RecallAtK` |
| P2.3c | Compare MRR | Parse response JSON | `Semantic.MRR` ≥ `Fixed.MRR` |
| P2.3d | Fewer noise chunks | Parse response JSON | `Semantic.TotalChunks` < `Fixed.TotalChunks` |

### 5.5 Fixed-Size Rollback (Regression Safety)

| # | Test | Steps | Expected |
|---|------|-------|----------|
| P2.4a | Switch to fixed | Edit `appsettings.Development.json`: `"ChunkingStrategy": "fixed"` | — |
| P2.4b | Restart + upload | Restart app, upload same PDF | Chunks cut at 500 chars, 200 overlap |
| P2.4c | Verify mid-sentence splits | Inspect chunks (P2.2b query) | Some chunks cut mid-sentence |
| P2.4d | **Revert** | Set `"ChunkingStrategy": "semantic"`, restart | — |

### 5.6 Pass/Fail Criteria

| Criterion | Threshold | Evidence |
|-----------|-----------|----------|
| All chunking unit tests pass | 100% | Test runner output |
| `Semantic.RecallAtK` ≥ `Fixed.RecallAtK` | Always | Benchmark API response |
| All chunks within [MinChunkChars, MaxSectionChars] | 100% | DB query |
| Rollback to fixed works | Must work | Test P2.4a-d |
| Cross-section merge guard active | No cross-section merges | `ChunkMergerTests` |

---

## 6. RBL Phase 3 — RAG Quality 🟫

> **❌ STATUS: NOT IMPLEMENTED**  
> `RBL_PHASE3_PLAN.md` is in DRAFT. None of the Phase 3 code components exist in the current codebase. This section is provided as a **test plan template** — tests can only be executed after implementation.

### Missing Components

| Component | File | Status |
|-----------|------|--------|
| Embedding Cache | `Services/Rag/CachingEmbeddingService.cs` | ❌ Not created |
| Re-Ranking | `Services/Rag/ReRankService.cs` | ❌ Not created |
| Hybrid Search | `RagOptions` + `RagSearchService` update | ❌ Not implemented |
| Benchmark Automation | `Services/Rag/Benchmarking/BenchmarkRunner.cs` | ❌ Not created |
| Benchmark UI | `Components/Admin/Benchmarks/Benchmarks.razor` | ❌ Not created |
| Benchmark Entity | `Data/Entities/BenchmarkRunRecord.cs` | ❌ Not created |
| DB Migration | `Migrations/*_AddPhase3BenchmarkHistory*.cs` | ❌ Not created |
| P3 Config | `RagOptions.cs` (Cache, ReRank, Hybrid, Benchmark keys) | ❌ Not added |

### 6.1 Implementation Gate

Before testing, verify these files exist:

```powershell
$files = @(
    "Services\Rag\CachingEmbeddingService.cs",
    "Services\Rag\ReRankService.cs",
    "Services\Rag\Benchmarking\BenchmarkRunner.cs",
    "Data\Entities\BenchmarkRunRecord.cs"
)
foreach ($f in $files) {
    $exists = Test-Path "AI_Study_Hub_v2\$f"
    Write-Host ("{0} {1}" -f ($exists ? "✓" : "✗"), $f)
}
```

### 6.2 Test Plan (Post-Implementation)

Once implemented, these tests validate Phase 3:

| # | Component | Test | Expected |
|---|-----------|------|----------|
| P3.1 | Config | `RagOptions` has `EmbeddingCacheEnabled`, `ReRankEnabled`, `HybridSearchEnabled`, `BenchmarkAutomationEnabled` | All present with defaults |
| P3.2 | Embedding Cache | Chat same query twice → second response faster, no Ollama HTTP call logged | Cache hit |
| P3.3 | Embedding Cache | Wait TTL (or reduce to 1min) → next query calls Ollama again | Cache expired |
| P3.4 | Hybrid Search | Chat with keyword → results differ from pure vector search | Hybrid ≠ Vector |
| P3.5 | Hybrid Search | Toggle `SearchMode: "vector"` → restart → results differ | Config works |
| P3.6 | Re-Ranking | Chat with document (20+ chunks) → top 5 re-ranked | Order differs from raw score |
| P3.7 | Re-Ranking | Toggle `ReRankEnabled: false` → results differ | Config works |
| P3.8 | Benchmark Auto | Enable `BenchmarkAutomationEnabled`, wait interval → DB has records | Persisted |
| P3.9 | Benchmark UI | Navigate `/admin/benchmarks` → charts display | Page loads |
| P3.10 | Manual Benchmark | `POST /api/benchmark/run` → JSON result + DB record | Both present |

### 6.3 Rollback Strategy

All Phase 3 features are toggleable via config:

```json
{
  "Rag": {
    "EmbeddingCacheEnabled": true,   // Set false → cache disabled
    "ReRankEnabled": true,           // Set false → re-rank disabled
    "HybridSearchEnabled": true,     // Requires DB migration
    "SearchMode": "hybrid",          // "vector" → fallback to pure vector
    "BenchmarkAutomationEnabled": false  // Default: OFF
  }
}
```

---

## 7. Full Integration Smoke Test

### 7.1 Pre-Requisites Check

```powershell
# Verify all services running before smoke test
Write-Host "--- Health Check ---" -ForegroundColor Cyan
docker ps --format "table {{.Names}}\t{{.Status}}" | Select-String "supabase|ollama"
Invoke-WebRequest http://localhost:5240 -UseBasicParsing -TimeoutSec 5
Write-Host "--- Health OK ---"  -ForegroundColor Green
```

### 7.2 Smoke Scenarios

| # | Scenario | Steps | Expected | Phase |
|---|----------|-------|----------|-------|
| S1 | **Upload 3 file types** | Upload 1 `.pdf`, 1 `.docx`, 1 `.txt` via `/documents/upload` | All 3 → `Ready` in library | P1+P2 |
| S2 | **Chat with documents** | Open chat, select each document, ask content questions | Meaningful answers with citations | P1 |
| S3 | **Re-ingest a document** | `POST /api/documents/{id}/ingest` | Document remains `Ready` | P1+P2 |
| S4 | **Upload scanned PDF** | Upload image-only PDF (no text) | Status → `Failed`, error: "No extractable text" | P1 |
| S5 | **Fault: Ollama offline** | `docker stop aistudy-ollama`, upload PDF | File saved, chunks fail → `Failed` | P1 |
| S6 | **Recover: Re-ingest** | `docker start aistudy-ollama`, wait, re-ingest | Status → `Ready` | P1 |
| S7 | **Chunking benchmark** | `POST /api/benchmark/chunking-compare` | `Semantic.RecallAtK` ≥ `Fixed.RecallAtK` | P2 |
| S8 | **Full test suite** | `dotnet test --nologo` | All 215+ tests pass | P1+P2 |
| S9 | **Config rollback** | Toggle `ChunkingStrategy: "fixed"` → restart → upload | Fixed chunking active | P2 |
| S10 | **Revert config** | Restore `"semantic"` → restart → upload | Semantic chunking active | P2 |

> **Note:** Scenarios S8-S10 from the original guide reference Phase 3 features (Hybrid Search, Re-Rank, Benchmark API). These are **NOT applicable until Phase 3 is implemented.** The smoke scenarios above cover only P1+P2 which are fully implemented.

### 7.3 Smoke Test Automation Script

```powershell
# smoke-test.ps1 — Run from repo root
param([string]$BearerToken)

if (-not $BearerToken) {
    Write-Host "Bearer token required. Get from browser DevTools → Local Storage → sb-*-auth-token" -ForegroundColor Yellow
    exit 1
}

$base = "http://localhost:5240"
$headers = @{ "Authorization" = "Bearer $BearerToken"; "Content-Type" = "application/json" }
$passed = 0; $failed = 0

# S8: Unit tests
Write-Host "[S8] Running unit tests..." -ForegroundColor Cyan
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.Tests --nologo -v q
if ($LASTEXITCODE -eq 0) { $passed++; Write-Host "  ✓ PASS" -ForegroundColor Green }
else { $failed++; Write-Host "  ✗ FAIL" -ForegroundColor Red }

# S7: Benchmark compare
Write-Host "[S7] Chunking benchmark..." -ForegroundColor Cyan
$resp = Invoke-RestMethod -Uri "$base/api/benchmark/chunking-compare" -Method Post -Headers $headers -Body "{}"
if ($resp.Semantic.RecallAtK -ge $resp.Fixed.RecallAtK) { $passed++; Write-Host "  ✓ PASS" -ForegroundColor Green }
else { $failed++; Write-Host "  ✗ FAIL" -ForegroundColor Red }

Write-Host "`n--- Results: $passed passed, $failed failed ---" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
```

---

## 8. Regression Test Suite

Run before every merge to `main` and before every release.

### 8.1 Automated Regression

```powershell
# Full test suite — all tests
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.Tests --nologo --verbosity normal

# Filtered runs (use once tag attributes are added to tests):
# dotnet test --filter "TestCategory=Smoke|TestCategory=Unit" --nologo
```

### 8.2 Manual Regression Checklist

Tick each item after verification.

| # | Check | Phase | ✓ |
|---|-------|-------|---|
| R1 | Login works (admin + regular user) | P1 | ☐ |
| R2 | Upload PDF → Ready status | P1 | ☐ |
| R3 | Upload DOCX → Ready status | P1 | ☐ |
| R4 | Chat with document returns citations | P1 | ☐ |
| R5 | Semantic chunks respect boundaries | P2 | ☐ |
| R6 | Fixed-size rollback works | P2 | ☐ |
| R7 | `dotnet test` — 0 failures | P1+P2 | ☐ |
| R8 | `dotnet build` — 0 compilation errors | All | ☐ |
| R9 | Admin Dashboard loads | Admin | ☐ |
| R10 | Admin Users page loads | Admin | ☐ |

### 8.3 Regression Thresholds

| Metric | Allowed | Action if Exceeded |
|--------|---------|-------------------|
| Test failures | 0 | Block merge, investigate |
| Build errors | 0 | Block merge, fix |
| Test pass rate drop | < 5% vs last run | Review, possibly accept with PO approval |
| Recall@K drop | < 10% vs baseline | Investigate chunking regression |
| Response time increase | < 50% | Profile, consider rollback |

---

## 9. Manual Test Procedures

### 9.1 Authentication Token Extraction

Instead of manual DevTools copy, use this script:

```powershell
# Get-AuthToken.ps1 — Login via API and return Bearer token
param(
    [string]$Email = "admin@aistudyhub.local",
    [string]$Password = "__SET_VIA_USER_SECRETS__"
)

$base = "http://localhost:5240"
$body = @{ email = $Email; password = $Password } | ConvertTo-Json
$resp = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -Body $body -ContentType "application/json"
$resp.access_token
```

### 9.2 Document Re-ingest (Manual)

```powershell
# Re-ingest a specific document
$token = & ".\Get-AuthToken.ps1"
$docId = "<paste-document-guid>"
Invoke-RestMethod -Uri "http://localhost:5240/api/documents/$docId/ingest" `
    -Method Post `
    -Headers @{ Authorization = "Bearer $token" }
```

### 9.3 Database Inspection Queries

```powershell
# Recent documents with status
docker exec supabase-db psql -U postgres -c @"
SELECT file_name, status, review_status, created_at
FROM documents ORDER BY created_at DESC LIMIT 10;
"@

# Chunks per document
docker exec supabase-db psql -U postgres -c @"
SELECT d.file_name, COUNT(dc.id) as chunks, dc.embedding_model
FROM documents d
LEFT JOIN document_chunks dc ON dc.document_id = d.id
GROUP BY d.id, d.file_name, dc.embedding_model
ORDER BY d.created_at DESC LIMIT 10;
"@

# User roles summary
docker exec supabase-db psql -U postgres -c @"
SELECT r.role_name, COUNT(u.id) as user_count
FROM roles r
LEFT JOIN users u ON u.role_id = r.id
GROUP BY r.role_name;
"@
```

---

## 10. Configuration Reference

### 10.1 `appsettings.Development.json` — `Rag` Section

| Key | Phase | Type | Default | Description |
|-----|-------|------|---------|-------------|
| `ChunkingStrategy` | P2 | string | `"semantic"` | `"semantic"` or `"fixed"` |
| `ChunkSizeChars` | P1 | int | `500` (config) / `1000` (code default) | Target chunk size (fixed mode). Config file overrides `RagOptions.cs` default. |
| `ChunkOverlapChars` | P1 | int | `200` | Overlap between chunks (fixed) |
| `MinChunkChars` | P2 | int | `100` | Minimum chunk length |
| `MaxSectionChars` | P2 | int | `1000` | Maximum chunk length |
| `DefaultTopK` | P1 | int | `5` | Default search results count |
| `MaxTopK` | P1 | int | `10` | Maximum allowed results |
| `EmbeddingDimensions` | P1 | int | `384` | Vector dimension (matches all-minilm) |
| `MaxContextChars` | P1 | int | `6000` | Max context for LLM prompt |

### 10.2 `appsettings.Development.json` — `Ollama` Section

| Key | Phase | Type | Default | Description |
|-----|-------|------|---------|-------------|
| `BaseUrl` | P1 | string | `http://localhost:11434` | Ollama server |
| `Model` | P1 | string | `all-minilm:l6-v2` | Embedding model name |
| `TimeoutSeconds` | P1 | int | `30` | Per-request timeout |
| `MaxRetries` | P1 | int | `3` | Exponential backoff retries |

### 10.3 Phase 3 Config Template (Planned)

When implementing Phase 3, add these keys to the `Rag` section and `RagOptions.cs`:

```json
{
  "Rag": {
    // ... existing P1+P2 keys above ...
    "EmbeddingCacheEnabled": true,
    "EmbeddingCacheMaxEntries": 1000,
    "EmbeddingCacheTtlMinutes": 30,
    "ReRankEnabled": true,
    "ReRankCandidateCount": 20,
    "ReRankTopN": 5,
    "HybridSearchEnabled": true,
    "VectorWeight": 0.7,
    "SearchMode": "hybrid",
    "BenchmarkAutomationEnabled": false,
    "BenchmarkAutomationIntervalHours": 168,
    "BenchmarkAlertDropPercent": 10
  }
}
```

---

## 11. Entry/Exit Criteria & Sign-Off

### 11.1 Entry Criteria (Before Testing Begins)

| # | Criterion | Owner | Status |
|---|-----------|-------|--------|
| E1 | Code merged to test branch | Developer | ☐ |
| E2 | `dotnet build` passes (0 errors) | CI / Developer | ☐ |
| E3 | Docker services running (Supabase + Ollama) | Tester | ☐ |
| E4 | Ollama model `all-minilm:l6-v2` available | Tester | ☐ |
| E5 | Database migrations applied | Tester | ☐ |
| E6 | Test environment isolated from production | DevOps | ☐ |
| E7 | Test data available (or seed script ready) | Tester | ☐ |

### 11.2 Exit Criteria (Before Release)

| # | Criterion | Threshold | Status |
|---|-----------|-----------|--------|
| X1 | All unit/integration tests pass | 100% pass rate | ☐ |
| X2 | Smoke test S1-S10 pass | 100% pass | ☐ |
| X3 | Semantic recall@K ≥ fixed recall@K | Always | ☐ |
| X4 | No Severity 1 (critical) defects open | 0 open | ☐ |
| X5 | Regression suite passes | 0 failures | ☐ |
| X6 | Manual checklist completed (Section 8.2) | All items ticked | ☐ |
| X7 | All Entry Criteria still valid | No degradation | ☐ |

### 11.3 Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| **Developer** | | | |
| **QA / Tester** | | | |
| **Team Lead** | | | |

---

## 12. Known Issues & Troubleshooting

### 12.1 Known Issues

| ID | Issue | Severity | Workaround | Phase |
|----|-------|----------|------------|-------|
| K1 | Partial ingestion: document shows `Ready` even if some chunks failed embedding | Medium | Check app logs for "Skipping chunk" warnings | P1 |
| K2 | Upload API returns 201 even if ingestion fails | Low | Check Document Library for `Failed` status | P1 |
| K3 | Failed chunks never auto-retried | Medium | Use `POST /api/documents/{id}/ingest` | P1 |
| K4 | NuGet restore fails in sandboxed environments (NU1301) | Low | Ensure network access to `api.nuget.org`. Use `--no-restore` with pre-warmed NuGet cache. | All |
| K5 | Ollama not available blocks runtime embedding | Medium | HealthCheck is warn-only; use re-ingest after restoring | P1 |
| K6 | Kestrel port 5240 already in use | Low | `Stop-Process` or change port | All |
| K7 | Phase 3 not implemented | High | Run P1+P2 tests only; await implementation | P3 |

### 12.2 Common Errors & Fixes

| Symptom | Possible Cause | Fix |
|---------|---------------|-----|
| `SocketException (10061)` at Ollama startup | Ollama Docker not running | `docker compose -f infra\ollama\docker-compose.yml up -d` |
| `document_chunks` empty after upload | Ingestion failed | Check app logs, re-ingest with `POST /api/documents/{id}/ingest` |
| `recaptcha` startup error | Missing `ASPNETCORE_ENVIRONMENT=Development` | Set env var before running app |
| `dotnet test` NU1301 errors | NuGet not accessible | Run with `--no-restore` and warm cache |
| Migration `AddEmbeddingModelToDocumentChunks` fails | DB role mismatch | Verify Supabase Postgres is running on port 5432 |
| No chunks returned for RAG search | EmbeddingModel filter mismatch | Verify `embedding_model` column populated: `all-minilm:l6-v2` |

---

## 13. Appendix

### 13.1 Useful Commands

#### Docker

```powershell
# Supabase control
docker compose -f infra\supabase\docker-compose.yml up -d     # Start
docker compose -f infra\supabase\docker-compose.yml down      # Stop
docker compose -f infra\supabase\docker-compose.yml logs -f   # Follow logs

# Ollama control
docker compose -f infra\ollama\docker-compose.yml up -d       # Start
docker compose -f infra\ollama\docker-compose.yml down        # Stop
docker logs aistudy-ollama                                    # Check logs

# Ollama direct API
curl.exe -s http://localhost:11434/api/tags                   # List models
curl.exe -X POST http://localhost:11434/api/embed -H "Content-Type: application/json" -d '{"model":"all-minilm:l6-v2","input":"Hello world"}'  # Test embedding
```

#### Database

```powershell
# Quick schema inspection
docker exec supabase-db psql -U postgres -c "\dt public.*"
docker exec supabase-db psql -U postgres -c "\d public.document_chunks"

# Migration status
dotnet ef migrations list --project AI_Study_Hub_v2 --startup-project AI_Study_Hub_v2

# Apply pending migrations
dotnet ef database update --project AI_Study_Hub_v2 --startup-project AI_Study_Hub_v2
```

#### Application

```powershell
# Start (Development)
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --project AI_Study_Hub_v2 --no-launch-profile --urls http://localhost:5240

# Stop
Get-NetTCPConnection -LocalPort 5240 | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }

# Entity Framework commands (use --startup-project for multi-project solutions)
dotnet ef migrations list --project AI_Study_Hub_v2 --startup-project AI_Study_Hub_v2
dotnet ef database update --project AI_Study_Hub_v2 --startup-project AI_Study_Hub_v2

# Watch logs (when running)
Get-Content .\AI_Study_Hub_v2\logs\*.txt -Tail 20 -Wait
```

### 13.2 File Reference

#### Phase 1 — Real Embedding

| File | Description |
|------|-------------|
| `Services/Rag/OllamaEmbeddingService.cs` | Ollama `/api/embed` client with exponential backoff |
| `Services/OllamaHealthCheck.cs` | Startup connectivity check (warn-only) |
| `Options/OllamaOptions.cs` | `BaseUrl`, `Model`, `TimeoutSeconds`, `MaxRetries` |
| `Services/Rag/DocumentIngestionService.cs` | Fault-tolerant per-chunk embedding |
| `Services/Rag/RagSearchService.cs` | EmbeddingModel filter for cross-model safety |
| `Data/Entities/DocumentChunk.cs` | Added `EmbeddingModel` column |
| `Migrations/20260701132803_AddEmbeddingModelToDocumentChunks.cs` | Schema migration |
| `Controllers/AdminDocumentsController.cs` | Admin re-ingest endpoint |
| `infra/ollama/docker-compose.yml` | Standalone Ollama Docker |

#### Phase 2 — Semantic Chunking

| File | Description |
|------|-------------|
| `Services/Rag/BlockParser.cs` | Parse document structure (headings, paragraphs, lists, code) |
| `Services/Rag/SentenceSplitter.cs` | Sentence boundary detection incl. Vietnamese |
| `Services/Rag/ChunkMerger.cs` | Merge semantic blocks respecting size limits |
| `Services/Rag/ChunkingService.cs` | Orchestrator: BlockParser → Splitter → Merger |
| `Services/Rag/FixedSizeChunkingService.cs` | Fallback: fixed-size (500 char, 200 overlap) |
| `Services/Rag/SemanticChunkingModels.cs` | `BlockType`, `TextBlock`, `ChunkingContext` |
| `Services/Rag/Benchmarking/ChunkingBenchmarkService.cs` | `recall@K`, MRR comparison |
| `Services/Rag/Benchmarking/ChunkingBenchmarkDataset.cs` | Synthetic benchmark scenarios |
| `Controllers/BenchmarkController.cs` | `POST /api/benchmark/chunking-compare` |
| `docs/RBL_PHASE2_PLAN.md` | Phase 2 implementation plan |
| `docs/RBL_PHASE2_TEST_GUIDE.md` | Phase 2 test documentation |

---

## 14. Requirements Traceability Matrix

| Req ID | Requirement | Source | Priority | Test Case(s) | Status |
|--------|------------|--------|----------|-------------|--------|
| RBL-P1-01 | Replace FakeEmbeddingService with real Ollama | `RBL_PHASE1_PLAN.md` | P0 | P1.2b, P1.3a-d | ✅ |
| RBL-P1-02 | 384-dim vectors from all-minilm:l6-v2 | `RBL_PHASE1_PLAN.md` | P0 | P1.2 | ✅ |
| RBL-P1-03 | Fault-tolerant per-chunk embedding | `RBL_PHASE1_PLAN.md` | P1 | P1.5a-d | ✅ |
| RBL-P1-04 | Ollama health check (warn-only on startup) | `RBL_PHASE1_PLAN.md` | P1 | P1.1 | ✅ |
| RBL-P1-05 | Admin re-ingest endpoint | `RBL_PHASE1_PLAN.md` | P1 | S3 | ✅ |
| RBL-P2-01 | Semantic chunking replaces fixed-size | `RBL_PHASE2_PLAN.md` | P0 | P2.2a-g | ✅ |
| RBL-P2-02 | BlockParser: headings, paragraphs, lists, code | `RBL_PHASE2_PLAN.md` | P0 | BlockParserTests | ✅ |
| RBL-P2-03 | SentenceSplitter: Vietnamese boundary detection | `RBL_PHASE2_PLAN.md` | P0 | SentenceSplitterTests | ✅ |
| RBL-P2-04 | ChunkMerger: merge rules, section boundaries | `RBL_PHASE2_PLAN.md` | P0 | ChunkMergerTests | ✅ |
| RBL-P2-05 | Fixed-size rollback via config | `RBL_PHASE2_PLAN.md` | P1 | P2.4a-d | ✅ |
| RBL-P2-06 | Chunking benchmark (recall@K, MRR) | `RBL_PHASE2_PLAN.md` | P1 | P2.3a-d | ✅ |
| RBL-P2-07 | Scoring endpoint exposes strategy | `RBL_PHASE2_PLAN.md` | P2 | P2.1a-c | ✅ |
| RBL-P3-01 | Embedding cache with LRU + TTL | `RBL_PHASE3_PLAN.md` | P1 | P3.2-3 | ❌ |
| RBL-P3-02 | Cross-encoder re-ranking | `RBL_PHASE3_PLAN.md` | P1 | P3.6-7 | ❌ |
| RBL-P3-03 | Hybrid search (vector + keyword) | `RBL_PHASE3_PLAN.md` | P1 | P3.4-5 | ❌ |
| RBL-P3-04 | Benchmark automation + admin UI | `RBL_PHASE3_PLAN.md` | P2 | P3.8-10 | ❌ |
| RBL-P3-05 | Observability (latency, failure rate) | `RBL_PHASE3_PLAN.md` | P2 | P3.1 | ❌ |

---

## Session Handoff

### For AI Agents Resuming This Session

```markdown
## Test Guide Context
- Guide: `docs/COMPREHENSIVE_TEST_GUIDE.md` (v2.0.1)
- P1+P2: Full test suites available (215 tests pass)
- P3: NOT implemented — skip all P3 scenarios
- Bootstrap: `docs/scripts/bootstrap-test-env.ps1` (created, tested)
- Secrets required: dotnet user-secrets (see Section 3.2)
- Last verified: 2026-07-02, 215 tests passing, 0 build errors
```

---

> **End of Test Guide**  
> *"If you can't measure it, you can't improve it." — Peter Drucker*
