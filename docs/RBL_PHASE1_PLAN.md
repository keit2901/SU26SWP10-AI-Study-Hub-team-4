# RBL Phase 1 — Real Embedding (Ollama + all-minilm:l6-v2)

> **Status:** ✅ APPROVED — quyết định đã chốt, sẵn sàng triển khai  
> **Ngày:** 2026-06-26  
> **Review:** Đã qua 2 vòng challenger review + thảo luận team.  
> **Team:** Sơn (@ThShadow), Bảo (@TranGiaBao2005), Phước (@ChickMann)

---

## Mục tiêu

Thay `FakeEmbeddingService` (FNV-1a hash) bằng `OllamaEmbeddingService` (embedding thật), giữ nguyên:
- Interface `IEmbeddingService`
- Schema DB `vector(384)` — khớp với `all-minilm:l6-v2`
- Tất cả API contract hiện có

## Hiện trạng

- `FakeEmbeddingService` deterministic, reproducible, không ngữ nghĩa
- `ChunkSizeChars=1000` — vượt 256-token limit của model (phải giảm)
- Chưa biết DB có bao nhiêu chunk hiện tại
- Docker chưa có Ollama container

---

## Bước 1: Chuẩn bị (trước khi code)

| # | Việc | Người | Ghi chú |
|---|------|-------|---------|
| 1.1 | Query DB: `SELECT COUNT(*) FROM document_chunks WHERE embedding IS NOT NULL` | Dev 1 | Quyết định xem có cần migration không |
| 1.2 | Đảm bảo Docker Desktop chạy trên máy dev | Cả team | Ai không có Docker → không test được embedding |
| 1.3 | Hạ `ChunkSizeChars` từ 1000 → **500** trong `appsettings.json` | Dev 1 | 500 an toàn cho 256-token model kể cả tiếng Việt có dấu; `FindChunkBoundary()` overshoot nên cần margin |
| 1.4 | **Tạo dataset benchmark tiếng Việt:** 10 câu hỏi + 3 PDF mẫu tiếng Việt, ghi nhận expected relevant chunks | Phước | Làm baseline recall@5 trước/sau. Không có dataset → không đo được cải thiện |
| 1.5 | **Pin model version:** ghi nhận image tag `ollama/ollama:0.3.14` + model `all-minilm:l6-v2` vào config | Sơn | Tránh auto-update làm thay đổi embedding âm thầm |

---

## Bước 2: Code

### 2.1 Docker Compose — Ollama service (Dev 2)

Tạo file `infra/ollama/docker-compose.yml` (tách riêng, không gắn vào Supabase stack):

```yaml
version: "3.8"
services:
  ollama:
    image: ollama/ollama:0.30.9
    container_name: aistudy-ollama
    ports:
      - "11434:11434"
    volumes:
      - ollama_data:/root/.ollama
    mem_limit: 1g
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:11434/api/tags"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 60s
    entrypoint: ["/bin/sh", "-c"]
    command:
      - |
        ollama serve &
        sleep 5
        echo "Pulling all-minilm:l6-v2 (pinned to 0.3.14 tag for reproducible embeddings)..."
        ollama pull all-minilm:l6-v2
        echo "Model ready."
        wait
    networks:
      - aistudy-net

volumes:
  ollama_data:

networks:
  aistudy-net:
    external: true
    name: supabase_network  # share network với Supabase stack
```

> **Lý do tách riêng:** `docker compose down` trong `infra/supabase` sẽ không giết Ollama → ingestion đang chạy không bị lỗi.

### 2.2 Options & Config (Dev 1)

**File mới:** `Options/OllamaOptions.cs`
```csharp
public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "all-minilm:l6-v2";
    public int TimeoutSeconds { get; set; } = 30;   // per-request timeout
    public int MaxRetries { get; set; } = 3;
}
```

**appsettings.json:**
```json
"Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "all-minilm:l6-v2",
    "TimeoutSeconds": 30,
    "MaxRetries": 3
}
```

**appsettings.Development.json** override nếu cần port khác.

### 2.3 OllamaEmbeddingService (Dev 1)

**File mới:** `Services/Rag/OllamaEmbeddingService.cs`

Behavior:
- `POST {BaseUrl}/api/embeddings` với body `{ "model": "...", "prompt": "<text>" }`
- Parse response `{ "embedding": [0.1, -0.05, ...] }`
- **Validate 384-dim** (length check)
- **Validate non-zero** (ít nhất 1 phần tử ≠ 0 — tránh zero-vector pollute search)
- **Retry:** 3 lần, exponential backoff (1s, 2s, 4s)
- **Timeout:** 30s per-request (tổng worst-case ~30 + 60 + 120 = 210s với 3 retry)
- Dùng `IHttpClientFactory` (scoped, không singleton)

### 2.4 DI Swap (Dev 1)

**Program.cs:**
```csharp
// Từ:
services.AddScoped<IEmbeddingService, FakeEmbeddingService>();

// Thành:
services.AddScoped<IEmbeddingService, OllamaEmbeddingService>();

// Register Ollama HttpClient:
services.AddHttpClient<OllamaEmbeddingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

### 2.5 Transaction Refactoring (Dev 1 + Dev 2)

**Vấn đề:** `DocumentIngestionService.cs:134-167` wrap toàn bộ ingestion trong 1 transaction. Nếu Ollama fail ở chunk 47/100 → 46 chunk mất trắng, document Failed.

**Thiết kế chọn: Design B** — Ghi chunk mới trước, cleanup chunk cũ sau.

```
For each chunk:
  1. GenerateEmbeddingAsync() — retry 3x, nếu fail → skip chunk này, ghi log
  2. SaveChangesAsync() — save từng chunk độc lập

After all chunks processed:
  3. DELETE old chunks WHERE chunk_index NOT IN (new_chunk_indices)
  4. Nếu 0 chunk được tạo → document Status = Failed
  5. Nếu >= 1 chunk → document Status = Ready
```

> **Hỏi teammates:** Design A (xóa cũ trước) hay Design B (ghi mới trước)? Design B ít rủi ro hơn.

### 2.6 Health Check (Dev 2)

**File mới:** `Services/OllamaHealthCheck.cs` — implement `IHostedService`

```csharp
public class OllamaHealthCheck : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        // Gọi GET {BaseUrl}/api/tags
        // Nếu không reachable → _logger.LogWarning("Ollama not available...")
        // KHÔNG throw exception (app vẫn chạy)
    }
}
```

**Friendly error message:** Nếu `OllamaEmbeddingService` throw → map sang `AiChatException` với message tiếng Việt: "Dịch vụ embedding đang bảo trì, vui lòng thử lại sau."

### 2.7 Migration (Dev 1)

**Thêm cột `embedding_model` vào `document_chunks`:**

```sql
ALTER TABLE document_chunks ADD COLUMN embedding_model VARCHAR(50) NULL;
```

**Partial index cho performance:**
```sql
CREATE INDEX ON document_chunks 
USING ivfflat (embedding vector_cosine_ops) 
WHERE embedding_model = 'all-minilm:l6-v2';
```

**Cập nhật `RagSearchService.ApplyFilters()` (dòng 120-163):**
Thêm filter: `c.EmbeddingModel == _currentModel` — chỉ search trên chunk cùng model, tránh cross-model vector contamination.

**Entity update:** `DocumentChunk.cs` thêm property `EmbeddingModel`.

### 2.8 Re-ingest dữ liệu cũ (Dev 1)

Sau khi migration xong, tạo endpoint admin hoặc startup job:
```
POST /api/admin/documents/reingest-all
```
Chạy `DocumentService.IngestAsync()` cho mỗi document có `Status = Ready`. Chunk cũ tự bị xóa bởi logic hiện tại.

---

## Bước 3: Test (Dev 3)

### 3.1 Unit Tests

| Test | Dùng gì |
|------|---------|
| `OllamaEmbeddingService` gọi đúng endpoint | Mock `HttpMessageHandler`, verify request URL + body |
| Parse response đúng | Mock response JSON, verify `float[384]` output |
| Validate 384-dim | Mock response thiếu dimension → throw |
| Validate non-zero | Mock response toàn 0 → throw |
| Retry logic | Mock fail 2 lần, success lần 3 → verify gọi đúng 3 lần |
| Timeout | Mock delay > timeout → throw |
| `RagSearchService` filter `embedding_model` | Verify query có WHERE clause |

### 3.2 Integration Test (`[Ignore]`)

```csharp
[Test, Ignore("Requires running Ollama container")]
public async Task RealOllama_Returns_384Dim_NonZero_DifferentVectors()
{
    var svc = new OllamaEmbeddingService(httpClient, options);
    
    var v1 = await svc.GenerateEmbeddingAsync("Hello world");
    var v2 = await svc.GenerateEmbeddingAsync("Xin chào thế giới");
    var v3 = await svc.GenerateEmbeddingAsync("Hello world");  // same as v1
    
    Assert.That(v1, Has.Length.EqualTo(384));
    Assert.That(v1.Any(x => x != 0), Is.True);          // non-zero
    Assert.That(v1, Is.Not.EqualTo(v2));                  // different inputs → different vectors
    Assert.That(v1, Is.EqualTo(v3).Within(1e-6));        // same input → same vector
}
```

### 3.3 Smoke Test

1. Start Docker: `docker compose -f infra/ollama/docker-compose.yml up -d`
2. Start app
3. Upload 1 PDF
4. Chờ ingest hoàn tất
5. Chat: hỏi câu liên quan đến nội dung PDF
6. Verify: câu trả lời có cite đúng source, nội dung liên quan

### 3.4 Benchmark Baseline (trước/sau)

| Metric | Cách đo |
|--------|---------|
| Latency p50/p95 | `Stopwatch` trong `GenerateEmbeddingAsync` |
| Search recall@5 | 5 câu hỏi test với document đã biết → verify top 5 kết quả |
| RAM | `docker stats` trong lúc ingestion |

---

## Phân công cuối cùng ✅

| Dev | GitHub | Round 1 (cùng làm, 30m) | Round 2 (song song, 3-4h) | Round 3 (tích hợp, 2-3h) |
|-----|--------|--------------------------|---------------------------|---------------------------|
| **Sơn** | `@ThShadow` | Docker Ollama + chốt thiết kế | `OllamaEmbeddingService` + migration + DI swap | Smoke test |
| **Bảo** | `@TranGiaBao2005` | Docker Ollama + chốt thiết kế | `OllamaHealthCheck` + error handling + transaction refactor (Design B) | Integration test |
| **Phước** | `@ChickMann` | Docker Ollama + chốt thiết kế | Unit test mock + benchmark baseline | Benchmark + verify |

---

## 6 Quyết định đã chốt ✅

| # | Câu hỏi | Quyết định | Lý do |
|---|---------|-----------|-------|
| Q1 | Transaction refactoring? | **Design B** — ghi chunk mới trước, cleanup cũ sau | Crash giữa chừng không mất dữ liệu |
| Q2 | Re-ingest auto hay manual? | **Manual** — `POST /api/admin/documents/reingest-all` | Không block app startup, admin chủ động |
| Q3 | Ollama compose tách/gộp? | **Tách riêng** `infra/ollama/docker-compose.yml`, share network `supabase_network` | Down Supabase không giết Ollama |
| Q4 | Có benchmark không? | **Có** — latency + recall@5 trước/sau | Cần evidence định lượng |
| Q5 | Ai chạy integration test? | **Dev chạy manual trước PR**, `[Ignore]` | CI không có Docker |
| Q6 | Production embedding? | **Ollama local** cho Phase 1 | Miễn phí, interface cho phép swap cloud API sau |

---

## Timeline

| Phase | Thời gian |
|-------|----------|
| Chuẩn bị (Docker, query DB, config) | 1h |
| Round 1: Cùng chốt thiết kế | 0.5h |
| Round 2: Code song song | 3-4h |
| Round 3: Tích hợp, test, fix bug | 2-3h |
| **Tổng** | **7-9h** (1-2 ngày) |

---

## Rollback Plan

1. **Switch lại FakeEmbeddingService:** Đổi 1 dòng `Program.cs` → deploy
2. **Dữ liệu an toàn:** `embedding_model` column đảm bảo chunk cũ/không model bị filter out
3. **Re-ingest bằng fake:** Endpoint admin xóa chunk cũ, tạo lại chunk fake

---

## Reference Files

| File | Mục đích |
|------|----------|
| `Services/Rag/RagContracts.cs` | Interface `IEmbeddingService` |
| `Services/Rag/FakeEmbeddingService.cs` | Implementation hiện tại |
| `Services/Rag/DocumentIngestionService.cs:134-167` | Transaction cần refactor |
| `Services/Rag/RagSearchService.cs:120-163` | ApplyFilters cần thêm model filter |
| `Program.cs:119` | DI registration cần swap |
| `appsettings.json` | Thêm section Ollama |

---

> **Next step:** Bắt đầu Round 1 — cả 3 cùng khởi tạo Docker Ollama container.
