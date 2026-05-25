#requires -Version 5.1
<#
.SYNOPSIS
  Bootstrap script cho AI_Study_Hub_v2 --  generate Supabase secrets, up stack,
  cau hinh dotnet user-secrets, build verify. Chay 1 lan sau khi clone/extract.

.PARAMETER Force
  Regenerate `.env` + admin password ngay ca khi da ton tai. Pha state cu -- 
  chi dung khi muon reset hoan toan.

.PARAMETER SkipUp
  Khong chay `docker compose up`. Chi sinh `.env` + set user-secrets.

.PARAMETER SkipBuild
  Khong chay `dotnet build` cuoi cung.

.EXAMPLE
  .\setup.ps1                # lan dau setup
  .\setup.ps1 -Force          # reset toan bo secrets
  .\setup.ps1 -SkipUp         # da co stack chay roi, chi can set user-secrets
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$SkipUp,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$infraDir = Join-Path $repoRoot 'infra\supabase'
$appDir   = Join-Path $repoRoot 'AI_Study_Hub_v2'
$csproj   = Join-Path $appDir 'AI_Study_Hub_v2.csproj'
$envFile  = Join-Path $infraDir '.env'

function Write-Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok  ($m) { Write-Host "OK   $m" -ForegroundColor Green }
function Write-Hint($m) { Write-Host "     $m" -ForegroundColor DarkGray }
function Write-Warn2($m){ Write-Host "WARN $m" -ForegroundColor Yellow }
function Fail($m)       { Write-Host "FAIL $m" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------------------
# 1. Prerequisites
# ---------------------------------------------------------------------------
Write-Step 'Checking prerequisites...'
foreach ($tool in @('docker','dotnet')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Fail "$tool not in PATH. Install: Docker Desktop + .NET 8 SDK."
    }
}
& docker info *> $null
if ($LASTEXITCODE -ne 0) { Fail 'Docker daemon not reachable. Start Docker Desktop.' }
$dotnetVer = (& dotnet --version) 2>&1
Write-Ok "docker OK, dotnet $dotnetVer"

if (-not (Test-Path -LiteralPath $infraDir)) { Fail "Missing folder: $infraDir" }
if (-not (Test-Path -LiteralPath $appDir))   { Fail "Missing folder: $appDir" }
if (-not (Test-Path -LiteralPath $csproj))   { Fail "Missing csproj: $csproj" }

# ---------------------------------------------------------------------------
# 2. Helpers --  random hex + HS256 JWT signing
# ---------------------------------------------------------------------------
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
    $exp = $iat + (5 * 365 * 24 * 60 * 60)  # 5 years
    # Order matters for stable JWT bytes; use ordered hashtable + manual JSON to avoid culture issues.
    $payload = '{"role":"' + $role + '","iss":"supabase-aistudyhub","iat":' + $iat + ',"exp":' + $exp + '}'
    return New-HmacJwt -payloadJson $payload -secret $secret
}

# ---------------------------------------------------------------------------
# 3. Generate or load .env
# ---------------------------------------------------------------------------
$secrets = @{}

if ((Test-Path -LiteralPath $envFile) -and -not $Force) {
    Write-Step "Existing .env found --  loading secrets (use -Force to regenerate)."
    Get-Content -LiteralPath $envFile | ForEach-Object {
        if ($_ -match '^\s*([A-Z0-9_]+)\s*=\s*(.*)$') {
            $secrets[$Matches[1]] = $Matches[2]
        }
    }
    foreach ($k in 'POSTGRES_PASSWORD','JWT_SECRET','ANON_KEY','SERVICE_ROLE_KEY','DASHBOARD_PASSWORD') {
        if (-not $secrets.ContainsKey($k) -or [string]::IsNullOrWhiteSpace($secrets[$k])) {
            Fail "Required key '$k' missing in $envFile. Re-run with -Force to regenerate."
        }
    }
    Write-Ok ".env loaded from disk."
} else {
    Write-Step 'Generating fresh Supabase secrets...'
    $jwt = New-RandomHex 32   # 64 hex = 32 bytes, easily >= 32 chars

    $secrets['POSTGRES_PASSWORD']           = New-RandomHex 16
    $secrets['JWT_SECRET']                  = $jwt
    $secrets['ANON_KEY']                    = New-SupabaseApiKey -role 'anon'         -secret $jwt
    $secrets['SERVICE_ROLE_KEY']            = New-SupabaseApiKey -role 'service_role' -secret $jwt
    $secrets['DASHBOARD_PASSWORD']          = New-RandomHex 12
    $secrets['SECRET_KEY_BASE']             = New-RandomHex 32
    $secrets['VAULT_ENC_KEY']               = New-RandomHex 16
    $secrets['PG_META_CRYPTO_KEY']          = New-RandomHex 16
    $secrets['LOGFLARE_PUBLIC_ACCESS_TOKEN']  = New-RandomHex 32
    $secrets['LOGFLARE_PRIVATE_ACCESS_TOKEN'] = New-RandomHex 32

    # Embedded template --  derived from working Phase 1 .env. Placeholders {{KEY}} replaced below.
    $template = @'
############
# Compose layering
############
COMPOSE_FILE=docker-compose.yml

############
# Secrets --  generated by setup.ps1 (NOT for production; rotate before going live).
############
POSTGRES_PASSWORD={{POSTGRES_PASSWORD}}

JWT_SECRET={{JWT_SECRET}}
ANON_KEY={{ANON_KEY}}
SERVICE_ROLE_KEY={{SERVICE_ROLE_KEY}}

# Asymmetric / opaque keys --  empty for legacy HS256 mode.
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
# Supavisor (Phase 2 --  disabled now)
############
POOLER_PROXY_PORT_TRANSACTION=6543
POOLER_DEFAULT_POOL_SIZE=20
POOLER_MAX_CLIENT_CONN=100
POOLER_TENANT_ID=aistudyhub-dev
POOLER_DB_POOL_SIZE=5


############
# Studio
############
STUDIO_DEFAULT_ORGANIZATION=AI Study Hub
STUDIO_DEFAULT_PROJECT=aistudyhub-v2
OPENAI_API_KEY=


############
# Auth (GoTrue) --  Phase 1: email signup ON, autoconfirm ON, no SMTP
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
# Storage --  Phase 2 only (stubs)
############
GLOBAL_S3_BUCKET=stub
REGION=stub
MINIO_ROOT_USER=supa-storage
MINIO_ROOT_PASSWORD=secret1234
STORAGE_TENANT_ID=stub


############
# Functions --  Phase 2 only
############
FUNCTIONS_VERIFY_JWT=false


############
# PostgREST
############
PGRST_DB_SCHEMAS=public,storage,graphql_public
PGRST_DB_MAX_ROWS=1000
PGRST_DB_EXTRA_SEARCH_PATH=public


############
# Analytics
############
DOCKER_SOCKET_LOCATION=/var/run/docker.sock
GOOGLE_PROJECT_ID=GOOGLE_PROJECT_ID
GOOGLE_PROJECT_NUMBER=GOOGLE_PROJECT_NUMBER


############
# API gateway (Kong)
############
KONG_HTTP_PORT=8000
KONG_HTTPS_PORT=8443
ANON_KEY_ASYMMETRIC=
SERVICE_ROLE_KEY_ASYMMETRIC=


############
# imgproxy (Phase 2)
############
IMGPROXY_AUTO_WEBP=true


############
# TLS Proxy --  not used Phase 1
############
PROXY_DOMAIN=your-domain.example.com
[email protected]
'@

    foreach ($k in $secrets.Keys) {
        $template = $template.Replace("{{$k}}", $secrets[$k])
    }

    # Write UTF-8 WITHOUT BOM and force LF line endings.
    # PowerShell 5.1's Set-Content -Encoding utf8 emits a BOM and CRLF, both of
    # which break older docker compose / .env parsers on some machines.
    $normalized = ($template -replace "`r`n", "`n")
    if (-not $normalized.EndsWith("`n")) { $normalized += "`n" }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($envFile, $normalized, $utf8NoBom)
    Write-Ok "Wrote $envFile (UTF-8 no BOM, LF)"
}

# ---------------------------------------------------------------------------
# 4. docker compose up
# ---------------------------------------------------------------------------
if (-not $SkipUp) {
    Write-Step 'Starting Supabase Local stack (docker compose up -d)...'
    & docker compose -f (Join-Path $infraDir 'docker-compose.yml') --project-directory $infraDir up -d
    if ($LASTEXITCODE -ne 0) { Fail 'docker compose up failed.' }

    Write-Step 'Waiting for supabase-db to become healthy (max 120s)...'
    $deadline = (Get-Date).AddSeconds(120)
    $status = 'starting'
    do {
        Start-Sleep -Seconds 3
        $status = (& docker inspect supabase-db --format '{{.State.Health.Status}}' 2>$null)
        if (-not $status) { $status = 'starting' }
        Write-Hint "db health = $status"
    } while ($status -ne 'healthy' -and (Get-Date) -lt $deadline)

    if ($status -ne 'healthy') {
        Write-Warn2 "supabase-db not healthy within timeout. Continuing --  check 'docker compose ps'."
    } else {
        Write-Ok 'supabase-db healthy.'
    }
} else {
    Write-Step 'Skipping docker compose up (-SkipUp).'
}

# ---------------------------------------------------------------------------
# 5. dotnet user-secrets
# ---------------------------------------------------------------------------
Write-Step 'Configuring dotnet user-secrets...'

# init is idempotent --  sets <UserSecretsId> in csproj if missing
& dotnet user-secrets init --project $csproj *> $null

$existingSecrets = (& dotnet user-secrets list --project $csproj) 2>&1
$adminPwdLine = $existingSecrets | Select-String -Pattern '^Seed:DefaultAdmin:Password\s*=\s*(.+)$'
if ($adminPwdLine -and -not $Force) {
    $adminPwd = $adminPwdLine.Matches[0].Groups[1].Value.Trim()
    $adminPwdSource = 'reused from existing user-secrets'
} else {
    $adminPwd = New-RandomHex 10
    $adminPwdSource = 'newly generated'
}

$secretMap = [ordered]@{
    'ConnectionStrings:Postgres' = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=$($secrets['POSTGRES_PASSWORD'])"
    'Supabase:JwtSecret'         = $secrets['JWT_SECRET']
    'Supabase:AnonKey'           = $secrets['ANON_KEY']
    'Supabase:ServiceRoleKey'    = $secrets['SERVICE_ROLE_KEY']
    'Seed:DefaultAdmin:Password' = $adminPwd
}

foreach ($kv in $secretMap.GetEnumerator()) {
    & dotnet user-secrets set $kv.Key $kv.Value --project $csproj *> $null
    if ($LASTEXITCODE -ne 0) { Fail "Failed to set user-secret '$($kv.Key)'." }
}
Write-Ok 'user-secrets set (5 keys).'

# ---------------------------------------------------------------------------
# 6. dotnet build
# ---------------------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Step 'Running dotnet build...'
    & dotnet build $csproj --nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail 'dotnet build failed.' }
    Write-Ok 'Build succeeded.'
}

# ---------------------------------------------------------------------------
# 7. Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=========================================================' -ForegroundColor Green
Write-Host '  SETUP COMPLETE' -ForegroundColor Green
Write-Host '=========================================================' -ForegroundColor Green
Write-Host ''
Write-Host "  Postgres        : localhost:5432  (password in user-secrets)"
Write-Host "  Kong gateway    : http://localhost:8000  (Studio + GoTrue + REST)"
Write-Host "  Studio login    : supabase / $($secrets['DASHBOARD_PASSWORD'])"
Write-Host "  App will run at : http://localhost:5240"
Write-Host ''
Write-Host "  Default admin email: admin@aistudyhub.local" -ForegroundColor Yellow
Write-Host "  Default admin pwd  : $adminPwd  ($adminPwdSource)" -ForegroundColor Yellow
Write-Host '  ^ Copy now --  pwd is also kept in dotnet user-secrets for re-seed.' -ForegroundColor DarkYellow
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host '  cd AI_Study_Hub_v2'
Write-Host '  $env:ASPNETCORE_ENVIRONMENT = "Development"'
Write-Host '  dotnet run --no-launch-profile --urls http://localhost:5240'
Write-Host ''
Write-Host 'Run all tests (offline, no stack required):' -ForegroundColor Cyan
Write-Host '  dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj'
Write-Host ''
