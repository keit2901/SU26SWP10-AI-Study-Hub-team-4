# RBL Phase 2 Test Guide

## Muc tieu

Checklist nay dung de nghiem thu Phase 2 Semantic Chunking theo `docs/RBL_PHASE2_PLAN.md`.

Phase 2 duoc xem la dat khi:

- Semantic chunking da duoc bat qua config.
- Fixed-size chunking van con rollback path.
- Unit test chunking pass.
- Benchmark API chay duoc.
- Re-ingest API chay duoc.
- Tai lieu da duoc re-ingest bang chunking strategy moi.

---

## 1. Chuan bi moi truong

Chay tu repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

Neu da setup roi thi chi can dam bao:

- Supabase local dang chay
- Ollama local dang chay
- app dang chay o `http://localhost:5240`

Neu can chay app:

```powershell
cd .\AI_Study_Hub_v2
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240
```

Neu app dang mo tu lan build cu, hay stop app roi chay lai truoc khi test benchmark/re-ingest.

---

## 2. Unit test can pass

Chay full test suite:

```powershell
dotnet test .\AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj
```

Hoac toi thieu chay nhom test Phase 2:

```powershell
dotnet test .\AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj --filter "ChunkingServiceTests|BlockParserTests|SentenceSplitterTests|ChunkMergerTests|ChunkingBenchmarkServiceTests|DocumentIngestionServiceTests|AdminDocumentsControllerTests"
```

Expected:

- Khong co test fail.
- `ChunkingServiceTests`, `BlockParserTests`, `SentenceSplitterTests`, `ChunkMergerTests` pass.
- `ChunkingBenchmarkServiceTests` pass.
- `DocumentIngestionServiceTests` pass.
- `AdminDocumentsControllerTests` pass.

---

## 3. Dang nhap de lay token local

Tai local dev, default admin la:

- Email: `admin@aistudyhub.local`
- Password: lay tu `dotnet user-secrets list --project .\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj`

Login va lay token:

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

Expected:

- Tra ve user `admin@aistudyhub.local`
- `role = Admin`

---

## 4. Benchmark semantic vs fixed

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5240/api/benchmark/chunking-compare `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body '{"topK":5}'
```

Can kiem tra:

- API tra ve `fixed` va `semantic`
- Co du `chunkCount`, `averageChunkChars`, `tinyChunkCount`, `recallAtK`, `meanReciprocalRank`
- Semantic khong tao heading chunk noise rieng nua
- Semantic phai cho ket qua retrieval hop ly tren dataset benchmark

Goi y doc ket qua:

- `tinyChunkCount` cang thap cang tot
- `recallAtK` cang cao cang tot
- `meanReciprocalRank` cang cao cang tot

---

## 5. Re-ingest tat ca tai lieu hien co

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5240/api/admin/documents/reingest-all `
  -Headers @{ Authorization = "Bearer $token" }
```

Expected:

- Co response JSON
- `chunkingStrategy = semantic`
- `failed = 0` la tot nhat
- Tai lieu `Ready` va `Failed` deu duoc dua vao re-ingest sweep

---

## 6. Acceptance checklist theo RBL plan

Danh dau hoan thanh khi cac muc sau dat:

- `BlockParser` detect duoc heading / paragraph / list
- `SentenceSplitter` tach cau dung cho English va Vietnamese abbreviations co ban
- `ChunkMerger` merge theo semantic rules, tranh chunk qua nho, co overlap hop ly
- `ChunkingService` dung semantic pipeline end-to-end
- `FixedSizeChunkingService` van rollback duoc qua config
- `RagOptions` va `appsettings` co `ChunkingStrategy`, `MinChunkChars`, `MaxSectionChars`
- Benchmark compare fixed vs semantic chay duoc
- Re-ingest all chay duoc
- Test suite pass

---

## 7. Luu y khi nghiem thu

- Neu benchmark/re-ingest tra `401`, login lai va lay token moi. Token dev chi song trong thoi gian ngan.
- Neu build/test bao file bi lock, app dang chay binary cu. Stop app, test xong roi chay lai.
- Neu restore bao `NU1301`, do moi truong khong truy cap duoc NuGet. Khi do can chay tren may da restore/cache san hoac co network hop le.
- Khong paste token va secret vao chat, ticket, hay log chia se.
