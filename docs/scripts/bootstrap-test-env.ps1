<#
.SYNOPSIS
  AI Study Hub v2 — One-Command Test Environment Bootstrap
.DESCRIPTION
  Starts all required services (Supabase, Ollama), verifies model availability,
  builds the solution, applies migrations, and runs unit tests.
  IDEMPOTENT: Safe to re-run at any time. Each step detects current state.
.EXAMPLE
  .\docs\scripts\bootstrap-test-env.ps1
  .\docs\scripts\bootstrap-test-env.ps1 -SkipTests  # Build only, skip unit tests
.NOTES
  Version: 1.0.0
  Updated: 2026-07-02
#>
param(
    [switch]$SkipTests,
    [int]$AppStartupWaitSeconds = 20
)

$ErrorActionPreference = "Stop"
$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
Set-Location $repoRoot

Write-Host @"
========================================
 AI Study Hub v2 — Test Env Bootstrap
========================================
"@ -ForegroundColor Cyan

$errors = @()

# ════════════════════════════════════════
# STEP 0: Verify prerequisites
# ════════════════════════════════════════
Write-Host "`n[0/6] Checking prerequisites..." -ForegroundColor Cyan
try { docker info *>$null; Write-Host "  ✓ Docker running" -ForegroundColor Green }
catch { $errors += "Docker not running. Start Docker Desktop first."; Write-Host "  ✗ Docker not running" -ForegroundColor Red }

try { dotnet --version *>$null; Write-Host "  ✓ .NET SDK $(dotnet --version)" -ForegroundColor Green }
catch { $errors += ".NET SDK not found"; Write-Host "  ✗ .NET SDK not found" -ForegroundColor Red }

if ($errors) { Write-Host ($errors -join "`n") -ForegroundColor Red; exit 1 }

# ════════════════════════════════════════
# STEP 1: Start Supabase
# ════════════════════════════════════════
Write-Host "`n[1/6] Starting Supabase..." -ForegroundColor Cyan
try {
    docker compose -f "infra\supabase\docker-compose.yml" up -d 2>&1 | Out-Null

    # Wait for PostgreSQL to accept connections (max 30s)
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        $pgCheck = docker exec supabase-db pg_isready -U postgres 2>$null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep 1
    }
    if ($ready) {
        Write-Host "  ✓ Supabase ready (Postgres accepting)" -ForegroundColor Green
    } else {
        $errors += "Supabase Postgres did not become ready within 30s"
        Write-Host "  ✗ Supabase timeout" -ForegroundColor Red
    }
} catch {
    $errors += "Failed to start Supabase: $_"
    Write-Host "  ✗ Supabase start failed" -ForegroundColor Red
}

# ════════════════════════════════════════
# STEP 2: Start Ollama
# ════════════════════════════════════════
Write-Host "`n[2/6] Starting Ollama..." -ForegroundColor Cyan
try {
    docker compose -f "infra\ollama\docker-compose.yml" up -d 2>&1 | Out-Null

    # Wait for Ollama to be ready (max 20s)
    $ready = $false
    for ($i = 0; $i -lt 20; $i++) {
        try {
            $null = Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -UseBasicParsing -TimeoutSec 2
            $ready = $true; break
        } catch { Start-Sleep 1 }
    }
    if ($ready) {
        Write-Host "  ✓ Ollama running" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ Ollama not responding — embeddings will use warn-only fallback" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ⚠ Ollama start failed — embeddings will use warn-only fallback" -ForegroundColor Yellow
}

# ════════════════════════════════════════
# STEP 3: Pull embedding model (if needed)
# ════════════════════════════════════════
Write-Host "`n[3/6] Checking Ollama model..." -ForegroundColor Cyan
try {
    $resp = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -Method Get -TimeoutSec 5
    $hasModel = ($resp.models | Where-Object { $_.name -eq "all-minilm:l6-v2" }).Count -gt 0
    if (-not $hasModel) {
        Write-Host "  Pulling all-minilm:l6-v2 (384-dim, ~80MB)..." -ForegroundColor Yellow
        docker exec aistudy-ollama ollama pull all-minilm:l6-v2 2>&1 | Out-Null
        Write-Host "  ✓ Model pulled" -ForegroundColor Green
    } else {
        Write-Host "  ✓ Model already present" -ForegroundColor Green
    }
} catch {
    Write-Host "  ⚠ Cannot check Ollama models — will retry at app startup" -ForegroundColor Yellow
}

# ════════════════════════════════════════
# STEP 4: Build solution
# ════════════════════════════════════════
Write-Host "`n[4/6] Building solution..." -ForegroundColor Cyan
try {
    $buildResult = dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo -v q 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Build successful" -ForegroundColor Green
    } else {
        $errors += "Build failed. Check output above."
        Write-Host "  ✗ Build failed!" -ForegroundColor Red
        Write-Host $buildResult -ForegroundColor Red
    }
} catch {
    $errors += "Build error: $_"
    Write-Host "  ✗ Build error" -ForegroundColor Red
}

# ════════════════════════════════════════
# STEP 5: Apply migrations via app startup
# ════════════════════════════════════════
Write-Host "`n[5/6] Applying database migrations..." -ForegroundColor Cyan
try {
    # Check if app already running on port 5240
    $existing = Get-NetTCPConnection -LocalPort 5240 -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "  ⚠ Port 5240 already in use by PID $($existing.OwningProcess). Stopping..." -ForegroundColor Yellow
        Stop-Process -Id $existing.OwningProcess -Force -ErrorAction SilentlyContinue
        Start-Sleep 2
    }

    # Start app in background with PID tracking
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $proc = Start-Process -FilePath "dotnet" `
        -ArgumentList "run","--project","AI_Study_Hub_v2","--no-launch-profile","--urls","http://localhost:5240" `
        -PassThru -NoNewWindow -RedirectStandardOutput "$env:TEMP\aistudy-bootstrap-stdout.log" `
        -RedirectStandardError "$env:TEMP\aistudy-bootstrap-stderr.log"

    # Wait for app to be ready
    $ready = $false
    for ($i = 0; $i -lt $AppStartupWaitSeconds; $i++) {
        try {
            $null = Invoke-WebRequest -Uri "http://localhost:5240" -UseBasicParsing -TimeoutSec 2
            $ready = $true; break
        } catch {
            # Still starting — check if process crashed
            if ($proc.HasExited) {
                $errorLog = Get-Content "$env:TEMP\aistudy-bootstrap-stderr.log" -Raw -ErrorAction SilentlyContinue
                throw "App process exited prematurely (exit code: $($proc.ExitCode)): $errorLog"
            }
        }
        Start-Sleep 1
    }

    if ($ready) {
        Write-Host "  ✓ App started, port 5240 responding" -ForegroundColor Green
        Write-Host "  ℹ Migrations apply at startup; check log for 'Database migrations applied'" -ForegroundColor Gray

        # Parse logs for migration status
        $stdout = Get-Content "$env:TEMP\aistudy-bootstrap-stdout.log" -Raw -ErrorAction SilentlyContinue
        if ($stdout -match "No migrations were applied|Database migrations applied") {
            Write-Host "  ✓ Migrations verified: $($Matches[0])" -ForegroundColor Green
        } else {
            Write-Host "  ⚠ Migration log not found — check $env:TEMP\aistudy-bootstrap-stdout.log" -ForegroundColor Yellow
        }
    } else {
        $errors += "App did not start within ${AppStartupWaitSeconds}s"
        Write-Host "  ✗ App startup timeout" -ForegroundColor Red
    }

    # Cleanup: stop the bootstrap instance (user can restart later)
    Write-Host "  ℹ Stopping bootstrap app instance..." -ForegroundColor Gray
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        Write-Host "  ✓ Bootstrap app stopped (PID $($proc.Id))" -ForegroundColor Green
    }
} catch {
    $errors += "Migration step failed: $_"
    Write-Host "  ✗ Migration error: $_" -ForegroundColor Red
}

# ════════════════════════════════════════
# STEP 6: Run unit tests
# ════════════════════════════════════════
if (-not $SkipTests) {
    Write-Host "`n[6/6] Running unit tests..." -ForegroundColor Cyan
    try {
        $testResult = dotnet test "AI_Study_Hub_v2\AI_Study_Hub_v2.Tests" --nologo -v q 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✓ All tests passed" -ForegroundColor Green
        } else {
            $errors += "Some tests failed. Check output above."
            Write-Host "  ✗ Test failures detected!" -ForegroundColor Red
            Write-Host $testResult -ForegroundColor Red
        }
    } catch {
        $errors += "Test execution error: $_"
        Write-Host "  ✗ Test error" -ForegroundColor Red
    }
} else {
    Write-Host "`n[6/6] Unit tests — SKIPPED (-SkipTests)" -ForegroundColor Yellow
}

# ════════════════════════════════════════
# Final report
# ════════════════════════════════════════
Write-Host "`n========================================" -ForegroundColor Cyan
if ($errors) {
    Write-Host "❌ Bootstrap completed with $($errors.Count) error(s):" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host "`nCheck logs at: $env:TEMP\aistudy-bootstrap-*.log" -ForegroundColor Yellow
    exit 1
} else {
    Write-Host "✅ All 6 steps complete. Environment ready." -ForegroundColor Green
    Write-Host "`nNext: Start app manually for manual testing:" -ForegroundColor Cyan
    Write-Host '  $env:ASPNETCORE_ENVIRONMENT="Development"' -ForegroundColor White
    Write-Host '  dotnet run --project AI_Study_Hub_v2 --no-launch-profile --urls http://localhost:5240' -ForegroundColor White
    exit 0
}
