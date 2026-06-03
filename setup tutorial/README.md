# AI Study Hub v2 - Setup Tutorial

Huong dan setup local sau khi clone/pull repo. Muc tieu: moi may tu tao secret local rieng, khong commit `.env`, khong commit API key.

> Chay cac lenh tu thu muc root cua repo, noi co `setup.ps1`, `AI_Study_Hub_v2/`, `infra/`.

---

## 1. Can cai truoc

| Tool | Yeu cau | Kiem tra |
|---|---|---|
| .NET SDK | 8.0.x tro len | `dotnet --version` |
| Docker Desktop | Dang chay neu muon start Supabase stack | `docker info` |
| PowerShell | Windows PowerShell 5.1 hoac PowerShell 7+ | `$PSVersionTable.PSVersion` |
| Port local | 5432, 8000, 8443, 5240 nen dang free | `Get-NetTCPConnection -LocalPort 5432,8000,8443,5240 -ErrorAction SilentlyContinue` |

Neu PowerShell chan script, dung dang nay:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

---

## 2. Setup nhanh

### Lan dau setup day du

Bat Docker Desktop truoc, sau do chay:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

Script se:

1. Tao/load `infra/supabase/.env` local.
2. Start Supabase local stack bang Docker Compose.
3. Set app config vao `dotnet user-secrets`.
4. Chay `dotnet build` de verify.

### Chi tao `.env` + user-secrets, khong start Docker

Dung khi chi muon chuan bi config local hoac Docker chua bat:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1 -SkipUp -SkipBuild
```

### Khi da co stack chay roi

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1 -SkipUp
```

### Reset toan bo secret local

Chi dung khi muon reset local. Lenh nay tao lai `.env` va admin password:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1 -Force
```

Neu reset ca DB thi dung them `docker compose ... down -v`; thao tac nay mat data local.

---

## 3. `.env`, `.env.example`, user-secrets khac nhau the nao?

| File/config | Dung de lam gi | Co commit khong? |
|---|---|---|
| `infra/supabase/.env` | Secret local cho Supabase Docker stack: Postgres password, JWT secret, anon/service role key, dashboard password | Khong. File nay da gitignore. |
| `infra/supabase/.env.example` | File mau/reference cua Supabase | Co, nhung khong dung lam runtime secret. |
| `dotnet user-secrets` | Secret local cho app .NET doc tu may cua tung nguoi | Khong nam trong repo. |
| API key AI, vi du `Groq:ApiKey` | Key cua tung may/tung dev de goi provider AI | Khong commit. Set thu cong bang user-secrets/env. |

`setup.ps1` hien tai ghi `infra/supabase/.env`, khong ghi de `.env.example`.

---

## 4. User-secrets duoc `setup.ps1` set tu dong

Sau khi chay setup, script set cac key nay cho project `AI_Study_Hub_v2`:

```text
ConnectionStrings:Postgres
Supabase:JwtSecret
Supabase:AnonKey
Supabase:ServiceRoleKey
Seed:DefaultAdmin:Password
```

Kiem tra ten key, khong in value secret:

```powershell
dotnet user-secrets list --project AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
```

> Luu y: output cua lenh tren co the hien value secret. Khong chup/man hinh/paste output len public chat.

---

## 5. AI provider API key

`setup.ps1` khong tu set API key AI nhu Groq/OpenAI. Moi may nen tu set key rieng de tranh lo key va tranh rate limit dung chung.

Set Groq key local:

```powershell
dotnet user-secrets set "Groq:ApiKey" "<groq-api-key-cua-ban>" --project AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
```

Neu chua co key that, app van co the dung mot so flow/local fallback tuy cau hinh, nhung live provider smoke se khong dai dien Groq that.

Khong ghi API key vao:

- `appsettings.json`
- `appsettings.Development.json`
- `.env.example`
- README/docs public
- chat public cua team

---

## 6. Chay app local

Sau khi Supabase stack da chay:

```powershell
cd AI_Study_Hub_v2
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240
```

Mo:

```text
http://localhost:5240/login
```

Default admin:

```text
Email: admin@aistudyhub.local
Password: password duoc setup.ps1 in ra va luu trong user-secrets
```

Supabase Studio:

```text
URL: http://localhost:8000
User: supabase
Password: DASHBOARD_PASSWORD trong infra/supabase/.env
```

Khong commit/paste password nay.

---

## 7. Chay tests

```powershell
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
```

Lan verify gan nhat trong handoff: 141 tests pass, 0 warnings.

---

## 8. Stop / cleanup

Stop app:

```text
Ctrl+C trong terminal dang chay dotnet run
```

Stop Supabase nhung giu data:

```powershell
docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase stop
```

Down containers nhung giu volume:

```powershell
docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase down
```

Reset DB/storage local, mat data:

```powershell
docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase down -v
```

---

## 9. Troubleshooting

### Docker daemon not reachable

Mo Docker Desktop va doi Docker san sang. Neu chi muon tao `.env` + user-secrets, dung:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1 -SkipUp -SkipBuild
```

### Port bi chiem

```powershell
Get-NetTCPConnection -LocalPort 5432,8000,8443,5240 -ErrorAction SilentlyContinue | Select-Object LocalPort,State,OwningProcess
Get-Process -Id <PID>
```

### App bao thieu `Supabase:JwtSecret`

Chay lai setup user-secrets:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1 -SkipUp -SkipBuild
```

### Login admin 401 / invalid_credentials

Thu reset local DB + secret neu dang lam local dev va chap nhan mat data:

```powershell
docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase down -v
powershell -ExecutionPolicy Bypass -File .\setup.ps1 -Force
```

---

## 10. Khi share repo/project

Nen de GitHub repo private neu chua muon lo source code. Tuy nhien repo private khong thay the secret hygiene: secret van khong duoc commit.

Khong commit/share cac thu sau:

```text
infra/supabase/.env
infra/supabase/volumes/db/data/
infra/supabase/volumes/storage/
AI_Study_Hub_v2/bin/
AI_Study_Hub_v2/obj/
*.user
```

`.env` da duoc ignore tai `infra/supabase/.gitignore`.

Neu can share zip cho team, nen exclude `.env`, volumes, `bin`, `obj`. Nguoi nhan chay lai `setup.ps1` de tao secret local moi.
