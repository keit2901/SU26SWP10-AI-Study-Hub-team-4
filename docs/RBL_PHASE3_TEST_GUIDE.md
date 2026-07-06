# RBL Phase 3 Test Guide

Ngay cap nhat: 2026-07-02

## Pham vi

Phase 3 hoan tat cac hang muc sau:

- Embedding cache cho query embedding
- Re-rank sau vector search
- Hybrid search `vector` / `keyword` / `hybrid`
- Benchmark history luu vao DB + endpoint xem lich su
- Trang admin `/admin/benchmarks`
- Benchmark automation background job + endpoint chay tay
- Observability cho embedding/search latency

## 1. Chay test code

Tu root repo:

```powershell
dotnet test .\AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj
```

Ket qua mong doi:

- `total: 241`
- `failed: 0`
- `skipped: 2`

Hai test skip la test phu thuoc provider/ollama thuc te.

## 2. Khoi dong app

Neu da co local secrets va database:

```powershell
dotnet run --project .\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
```

Luu y:

- App startup se tu chay migration, bao gom bang `benchmark_results`
- App se dung config `Rag` moi trong `appsettings*.json`

## 3. Login lay token

```powershell
$login = Invoke-RestMethod -Method Post `
  -Uri http://localhost:5240/api/auth/login `
  -ContentType "application/json" `
  -Body '{"email":"admin@aistudyhub.local","password":"admin"}'

$token = $login.accessToken
```

Kiem tra token:

```powershell
Invoke-RestMethod -Method Get `
  -Uri http://localhost:5240/api/auth/me `
  -Headers @{ Authorization = "Bearer $token" }
```

## 4. Test hybrid search

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5240/api/rag/search `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body '{"query":"SWP391 plan","topK":5,"searchMode":"hybrid"}'
```

Thu them:

- `searchMode: "vector"`
- `searchMode: "keyword"`

Muc tieu:

- endpoint tra 200
- ket qua co `sourceLabel`, `documentId`, `score`
- `keyword` va `hybrid` co the cho thu tu ket qua khac voi `vector`

## 5. Test chunking benchmark

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5240/api/benchmark/chunking-compare `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body '{"topK":5}'
```

Muc tieu:

- endpoint tra 200
- co block `fixed` va `semantic`
- co `recallAtK` va `meanReciprocalRank`

## 6. Test benchmark history

Chay benchmark tay:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5240/api/benchmark/run `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body '{}'
```

Lay lich su benchmark:

```powershell
Invoke-RestMethod -Method Get `
  -Uri http://localhost:5240/api/benchmark/history?take=10 `
  -Headers @{ Authorization = "Bearer $token" }
```

Muc tieu:

- sau khi chay `run`, endpoint `history` co them 1 ban ghi moi
- ban ghi co `overallScore`, `citationAccuracy`, `p50LatencyMs`, `isAutomated`, `alertTriggered`

## 7. Test benchmark automation chay tay

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5240/api/benchmark/automation/run-now `
  -Headers @{ Authorization = "Bearer $token" }
```

Muc tieu:

- endpoint tra 200
- `history` co them ban ghi moi voi `isAutomated = true`

Background automation mac dinh dang tat:

```json
"Rag": {
  "BenchmarkAutomationEnabled": false
}
```

Neu muon test background job:

1. Bat `BenchmarkAutomationEnabled = true`
2. Giam `BenchmarkAutomationIntervalHours`
3. restart app

## 8. Test admin UI

Mo:

- `http://localhost:5240/admin/benchmarks`

Kiem tra:

- menu trai co muc `Benchmark History`
- page load duoc danh sach benchmark tu API
- button `Run Manual` va `Run Automated` chay duoc
- chart va bang du lieu cap nhat sau khi run

## 9. Test reingest sau khi doi chien luoc chunking

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5240/api/admin/documents/reingest-all `
  -Headers @{ Authorization = "Bearer $token" }
```

Muc tieu:

- endpoint tra 200
- co `succeeded > 0`
- `chunkingStrategy` khop config hien tai

## 10. Config rollback

Co the tat rieng tung tinh nang bang config:

```json
"Rag": {
  "EmbeddingCacheEnabled": false,
  "ReRankEnabled": false,
  "HybridSearchEnabled": false,
  "SearchMode": "vector"
}
```

## 11. Dau hieu thanh cong

Phase 3 duoc xem la passed khi:

- test suite qua `241/241` test chay, `2` skip
- `api/rag/search` hoat dong voi 3 mode
- `api/benchmark/history` tra du lieu
- `/admin/benchmarks` mo duoc va hien thi history
- benchmark manual/automated deu luu vao DB
