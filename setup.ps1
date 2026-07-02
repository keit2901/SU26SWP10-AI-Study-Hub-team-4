#requires -Version 5.1
<#
.SYNOPSIS
  Bootstrap script cho AI Study Hub v2 — worktree D:\FPT\summer2026\SWP391_github_current.
  Generate Supabase secrets, up stack, configure dotnet user-secrets, build verify.

.PARAMETER Force
  Regenerate .env + admin password ngay ca khi da ton tai.

.PARAMETER SkipDocker
  Khong chay docker compose up. Chi sinh .env + set user-secrets.

.PARAMETER SkipBuild
  Khong chay dotnet build cuoi cung.

.PARAMETER DockerOnly
  Chi up docker stack (Supabase + Ollama), khong tao secrets hay build.

.EXAMPLE
  .\setup.ps1                 # lan dau setup day du
  .\setup.ps1 -Force           # reset toan bo secrets
  .\setup.ps1 -SkipDocker      # da co stack, chi set user-secrets
  .\setup.ps1 -DockerOnly      # chi can khoi dong lai Supabase
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$SkipDocker,
    [switch]$SkipBuild,
    [switch]$DockerOnly
)

$ErrorActionPreference = 'Stop'

# ---- Paths (relative to this script's location) ----
$repoRoot = $PSScriptRoot
$infraDir      = Join-Path $repoRoot 'infra\supabase'
$ollamaDir     = Join-Path $repoRoot 'infra\ollama'
$ollamaCompose = Join-Path $ollamaDir 'docker-compose.yml'
$appDir        = Join-Path $repoRoot 'AI_Study_Hub_v2'
$csproj   = Join-Path $appDir 'AI_Study_Hub_v2.csproj'
$envFile  = Join-Path $infraDir '.env'
$testProj = Join-Path $appDir 'AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj'

# ---- Pretty output ----
function Write-Step($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }
function Write-Ok  ($m) { Write-Host "     OK   $m" -ForegroundColor Green }
function Write-Warn($m) { Write-Host "     WARN $m" -ForegroundColor Yellow }
function Fail($m)       { Write-Host "`nFAIL $m" -ForegroundColor Red; exit 1 }

# ======================================================================
# 1. PREREQUISITES
# ======================================================================
Write-Step '1/6  Checking prerequisites...'

if (-not (Test-Path -LiteralPath $infraDir)) { Fail "Missing: $infraDir" }
if (-not (Test-Path -LiteralPath $ollamaCompose)) { Fail "Missing: $ollamaCompose" }
if (-not (Test-Path -LiteralPath $appDir))   { Fail "Missing: $appDir" }
if (-not (Test-Path -LiteralPath $csproj))   { Fail "Missing: $csproj" }
Write-Ok "Worktree structure verified"

foreach ($tool in @('docker', 'dotnet')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Fail "$tool not in PATH. Install Docker Desktop + .NET 8 SDK."
    }
}

$dockerOk = $false
& docker info *> $null
if ($LASTEXITCODE -eq 0) {
    $dockerOk = $true
    Write-Ok "Docker daemon running"
} else {
    Write-Warn "Docker daemon NOT running. Steps requiring Docker will be skipped."
    Write-Warn "Start Docker Desktop and re-run setup.ps1 -SkipDocker to complete."
}

$dotnetVer = (& dotnet --version) 2>&1
Write-Ok "dotnet SDK $dotnetVer"

# ======================================================================
# 2. GENERATE .ENV (Supabase secrets)
# ======================================================================
if ($DockerOnly) {
    Write-Step '2/6  Docker-only mode -- skipping .env generation'
} else {
    Write-Step '2/6  Supabase secrets (.env)...'

    # ---- Crypto helpers ----
    function New-RandomHex([int]$bytes) {
        $b = New-Object byte[] $bytes
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
        -join ($b | ForEach-Object { $_.ToString('x2') })
    }

    function ConvertTo-Base64Url([byte[]]$bytes) {
        return ([Convert]::ToBase64String($bytes)).TrimEnd('=').Replace('+','-').Replace('/','_')
    }

    function New-HmacJwt([string]$payloadJson, [string]$secret) {
        $headerJson = '{"alg":"HS256","typ":"JWT"}'
        $headerEnc  = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($headerJson))
        $payloadEnc = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($payloadJson))
        $signing    = "$headerEnc.$payloadEnc"
        $hmac       = New-Object System.Security.Cryptography.HMACSHA256
        $hmac.Key   = [System.Text.Encoding]::UTF8.GetBytes($secret)
        $sig        = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($signing))
        return "$signing." + (ConvertTo-Base64Url $sig)
    }

    function New-SupabaseApiKey([string]$role, [string]$secret) {
        $iat = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        $exp = $iat + (5 * 365 * 24 * 60 * 60)
        $payload = '{"role":"' + $role + '","iss":"supabase-aistudyhub","iat":' + $iat + ',"exp":' + $exp + '}'
        return New-HmacJwt -payloadJson $payload -secret $secret
    }

    $secrets = @{}

    if ((Test-Path -LiteralPath $envFile) -and -not $Force) {
        Write-Step '     Existing .env found -- loading (use -Force to regenerate)'
        Get-Content -LiteralPath $envFile | ForEach-Object {
            if ($_ -match '^\s*([A-Z0-9_]+)\s*=\s*(.*)$') {
                $secrets[$Matches[1]] = $Matches[2]
            }
        }
        foreach ($k in 'POSTGRES_PASSWORD','JWT_SECRET','ANON_KEY','SERVICE_ROLE_KEY','DASHBOARD_PASSWORD') {
            if (-not $secrets.ContainsKey($k) -or [string]::IsNullOrWhiteSpace($secrets[$k])) {
                Fail "Required key '$k' missing in $envFile. Re-run with -Force."
            }
        }
        Write-Ok ".env loaded ($($secrets.Count) keys)"
    } else {
        Write-Step '     Generating fresh secrets...'
        $jwt = New-RandomHex 32

        $secrets['POSTGRES_PASSWORD']             = New-RandomHex 16
        $secrets['JWT_SECRET']                    = $jwt
        $secrets['ANON_KEY']                      = New-SupabaseApiKey -role 'anon'         -secret $jwt
        $secrets['SERVICE_ROLE_KEY']              = New-SupabaseApiKey -role 'service_role' -secret $jwt
        $secrets['DASHBOARD_PASSWORD']            = New-RandomHex 12
        $secrets['SECRET_KEY_BASE']               = New-RandomHex 32
        $secrets['VAULT_ENC_KEY']                 = New-RandomHex 16
        $secrets['PG_META_CRYPTO_KEY']            = New-RandomHex 16
        $secrets['LOGFLARE_PUBLIC_ACCESS_TOKEN']  = New-RandomHex 32
        $secrets['LOGFLARE_PRIVATE_ACCESS_TOKEN'] = New-RandomHex 32

        $template = @'
############
# Compose layering
############
COMPOSE_FILE=docker-compose.yml

############
# Secrets -- generated by setup.ps1
############
POSTGRES_PASSWORD={{POSTGRES_PASSWORD}}

JWT_SECRET={{JWT_SECRET}}
ANON_KEY={{ANON_KEY}}
SERVICE_ROLE_KEY={{SERVICE_ROLE_KEY}}

SUPABASE_PUBLISHABLE_KEY=
SUPABASE_SECRET_KEY=
JWT_KEYS=
JWT_JWKS=

DASHBOARD_USERNAME=supabase
DASHBOARD_PASSWORD={{DASHBOARD_PASSWORD}}

SECRET_KEY_BASE={{SECRET_KEY_BASE}}
VAULT_ENC_KEY={{VAULT_ENC_KEY}}
PG_META_CRYPTO_KEY={{PG_META_CRYPTO_KEY}}

LOGFLARE_PUBLIC_ACCESS_TOKEN={{LOGFLARE_PUBLIC_ACCESS_TOKEN}}
LOGFLARE_PRIVATE_ACCESS_TOKEN={{LOGFLARE_PRIVATE_ACCESS_TOKEN}}

S3_PROTOCOL_ACCESS_KEY_ID=625729a08b95bf1b7ff351a663f3a23c
S3_PROTOCOL_ACCESS_KEY_SECRET=850181e4652dd023b7a98c58ae0d2d34bd487ee0cc3254aed6eda37307425907


############
# URLs
############
SUPABASE_PUBLIC_URL=http://localhost:8000
API_EXTERNAL_URL=http://localhost:8000


############
# Database
############
POSTGRES_HOST=db
POSTGRES_DB=postgres
POSTGRES_PORT=5432


############
# Auth (GoTrue)
############
SITE_URL=http://localhost:5240
ADDITIONAL_REDIRECT_URLS=http://localhost:5240/login
JWT_EXPIRY=900

DISABLE_SIGNUP=false

MAILER_URLPATHS_CONFIRMATION="/auth/v1/verify"
MAILER_URLPATHS_INVITE="/auth/v1/verify"
MAILER_URLPATHS_RECOVERY="/auth/v1/verify"
MAILER_URLPATHS_EMAIL_CHANGE="/auth/v1/verify"

ENABLE_EMAIL_SIGNUP=true
ENABLE_EMAIL_AUTOCONFIRM=true
SMTP_ADMIN_EMAIL=admin@aistudyhub.local
SMTP_HOST=supabase-mail
SMTP_PORT=2500
SMTP_USER=fake_mail_user
SMTP_PASS=fake_mail_password
SMTP_SENDER_NAME=fake_sender
ENABLE_ANONYMOUS_USERS=false

ENABLE_PHONE_SIGNUP=false
ENABLE_PHONE_AUTOCONFIRM=false


############
# Storage
############
GLOBAL_S3_BUCKET=stub
REGION=stub
MINIO_ROOT_USER=supa-storage
MINIO_ROOT_PASSWORD=secret1234
STORAGE_TENANT_ID=stub


############
# PostgREST
############
PGRST_DB_SCHEMAS=public,storage,graphql_public
PGRST_DB_MAX_ROWS=1000
PGRST_DB_EXTRA_SEARCH_PATH=public


############
# API gateway (Kong)
############
KONG_HTTP_PORT=8000
KONG_HTTPS_PORT=8443


############
# Studio
############
STUDIO_DEFAULT_ORGANIZATION=AI Study Hub
STUDIO_DEFAULT_PROJECT=aistudyhub-v2
OPENAI_API_KEY=
'@

        foreach ($k in $secrets.Keys) {
            $template = $template.Replace("{{$k}}", $secrets[$k])
        }

        $normalized = ($template -replace "`r`n", "`n")
        if (-not $normalized.EndsWith("`n")) { $normalized += "`n" }
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($envFile, $normalized, $utf8NoBom)
        Write-Ok ".env created (UTF-8 no BOM, LF)"
    }
}

# ======================================================================
# 3. DOCKER COMPOSE UP
# ======================================================================
if ($SkipDocker) {
    Write-Step '3/6  -SkipDocker: skipping docker compose'
} elseif (-not $dockerOk) {
    Write-Step '3/6  Docker unavailable -- skipping'
} else {
    Write-Step '3/6  Starting Supabase stack...'
    & docker compose -f (Join-Path $infraDir 'docker-compose.yml') --project-directory $infraDir up -d
    if ($LASTEXITCODE -ne 0) { Fail 'docker compose up failed' }

    Write-Step '     Waiting for supabase-db (max 120s)...'
    $deadline = (Get-Date).AddSeconds(120)
    $healthy = $false
    do {
        Start-Sleep -Seconds 3
        $status = (& docker inspect supabase-db --format '{{.State.Health.Status}}' 2>$null)
        if ($status -eq 'healthy') { $healthy = $true; break }
        Write-Host "     db status: $status" -ForegroundColor DarkGray
    } while ((Get-Date) -lt $deadline)

    if ($healthy) {
        Write-Ok "supabase-db healthy"
    } else {
        Write-Warn "DB not healthy within 120s. Check 'docker compose ps'."
    }
}

# ======================================================================
# 3.5 OLLAMA DOCKER UP
# ======================================================================
if ($SkipDocker) {
    Write-Step '3.5/6  -SkipDocker: skipping Ollama Docker'
} elseif (-not $dockerOk) {
    Write-Step '3.5/6  Docker unavailable -- skipping Ollama'
} else {
    Write-Step '3.5/6  Starting Ollama embedding service...'

    & docker compose -f $ollamaCompose up -d
    if ($LASTEXITCODE -ne 0) { Fail 'Ollama docker compose up failed' }

    Write-Step '     Waiting for Ollama model all-minilm:l6-v2...'
    $deadline = (Get-Date).AddSeconds(180)
    $ollamaReady = $false

    do {
        Start-Sleep -Seconds 5
        $tags = (& curl.exe -s http://localhost:11434/api/tags) 2>$null

        if ($tags -match 'all-minilm:l6-v2') {
            $ollamaReady = $true
            break
        }

        Write-Host "     waiting for Ollama..." -ForegroundColor DarkGray
    } while ((Get-Date) -lt $deadline)

    if ($ollamaReady) {
        Write-Ok "Ollama ready: all-minilm:l6-v2"
    } else {
        Write-Warn "Ollama not ready within 180s. Check:"
        Write-Host "     docker logs -f aistudy-ollama" -ForegroundColor DarkGray
    }
}

# ======================================================================
# 4. ENSURE user-secrets INIT
# ======================================================================
if ($DockerOnly) {
    Write-Step '4/6  Docker-only mode -- skipping user-secrets'
} else {
    Write-Step '4/6  dotnet user-secrets...'
    & dotnet user-secrets init --project $csproj 2>&1 | Out-Null
    Write-Ok "user-secrets init done"
}

# ======================================================================
# 5. SET user-secrets (API keys, connection string)
# ======================================================================
if ($DockerOnly) {
    Write-Step '5/6  Docker-only mode -- skipping user-secrets values'
} else {
    Write-Step '5/6  Setting required secrets...'

    $existingSecrets = (& dotnet user-secrets list --project $csproj) 2>&1
    $adminPwdLine = $existingSecrets | Select-String -Pattern '^Seed:DefaultAdmin:Password\s*=\s*(.+)$'
    if ($adminPwdLine -and -not $Force) {
        $adminPwd = $adminPwdLine.Matches[0].Groups[1].Value.Trim()
        $adminPwdSource = 'reused'
    } else {
        $adminPwd = New-RandomHex 10
        $adminPwdSource = 'new'
    }

    $secretMap = [ordered]@{
        'ConnectionStrings:Postgres' = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=$($secrets['POSTGRES_PASSWORD'])"
        'Supabase:JwtSecret'         = $secrets['JWT_SECRET']
        'Supabase:AnonKey'           = $secrets['ANON_KEY']
        'Supabase:ServiceRoleKey'    = $secrets['SERVICE_ROLE_KEY']
        'Seed:DefaultAdmin:Password' = $adminPwd
    }

    foreach ($kv in $secretMap.GetEnumerator()) {
        & dotnet user-secrets set $kv.Key $kv.Value --project $csproj 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail "Failed to set '$($kv.Key)'" }
    }
    Write-Ok "5 secrets set (admin pwd: $adminPwdSource)"

    # ---- Check which API keys are still missing ----
    $missingKeys = @()
    $checkKeys = @('Groq:ApiKey', 'Gemini:ApiKey', 'Recaptcha:SecretKey')
    foreach ($k in $checkKeys) {
        $found = $existingSecrets | Select-String -Pattern "^$($k.Replace(':','\:'))\s*=" -SimpleMatch
        if (-not $found) { $missingKeys += $k }
    }
    if ($missingKeys.Count -gt 0) {
        Write-Warn "Missing API keys: $($missingKeys -join ', ')"
        Write-Host "     Set with: dotnet user-secrets set '<Key>' '<Value>' --project `"$csproj`"" -ForegroundColor DarkGray
    }
}

# ======================================================================
# 5.5 APPLY EF MIGRATIONS
# ======================================================================
if ($DockerOnly) {
    Write-Step '5.5/6  Docker-only mode -- skipping EF migrations'
} else {
    Write-Step '5.5/6  Applying EF migrations...'

    & dotnet ef database update --project $csproj --startup-project $csproj
    if ($LASTEXITCODE -ne 0) {
        Fail 'dotnet ef database update failed. Make sure dotnet-ef is installed and Supabase DB is running.'
    }

    Write-Ok "EF migrations applied"
}
# ======================================================================
# 6. BUILD
# ======================================================================
if ($SkipBuild) {
    Write-Step '6/6  -SkipBuild: skipping build'
} elseif ($DockerOnly) {
    Write-Step '6/6  Docker-only mode -- skipping build'
} else {
    Write-Step '6/6  Building...'
    & dotnet build $csproj --nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail 'dotnet build failed' }
    Write-Ok "Build succeeded"
}

# ======================================================================
# SUMMARY
# ======================================================================
Write-Host ''
Write-Host '=========================================================' -ForegroundColor Green
Write-Host '  SETUP COMPLETE' -ForegroundColor Green
Write-Host '=========================================================' -ForegroundColor Green
Write-Host ''
if (-not $DockerOnly) {
    Write-Host "  Postgres        : localhost:5432"
    Write-Host "  Kong gateway    : http://localhost:8000"
    Write-Host "  Studio login    : supabase / $($secrets['DASHBOARD_PASSWORD'])"
    Write-Host "  App URL         : http://localhost:5240"
    Write-Host "  Ollama          : http://localhost:11434 (all-minilm:l6-v2)"
    Write-Host ''
    Write-Host "  Admin email     : admin@aistudyhub.local" -ForegroundColor Yellow
    Write-Host "  Admin password  : $adminPwd  ($adminPwdSource)" -ForegroundColor Yellow
}
Write-Host ''
Write-Host 'Quick start:' -ForegroundColor Cyan
Write-Host '  # Run app'
Write-Host '  $env:ASPNETCORE_ENVIRONMENT = "Development"'
Write-Host "  dotnet run --project `"$csproj`" --no-launch-profile --urls http://localhost:5240"
Write-Host ''
Write-Host '  # Run tests (no Docker needed)'
Write-Host "  dotnet test `"$testProj`""
Write-Host ''
Write-Host '  # Re-run setup with different options'
Write-Host '  .\setup.ps1 -Force          # regenerate all secrets'
Write-Host '  .\setup.ps1 -SkipDocker     # skip docker, set secrets only'
Write-Host '  .\setup.ps1 -DockerOnly     # just start Supabase + Ollama stack'
Write-Host ''
