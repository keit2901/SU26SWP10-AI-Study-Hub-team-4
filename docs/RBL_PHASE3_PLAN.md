# RBL Phase 3 — RAG Quality & Performance

> **Status:** DRAFT — đợi Phase 1 + Phase 2 hoàn thành  
> **Ngày:** 2026-06-26  
> **Phụ thuộc:** Phase 1 (Real Embedding) + Phase 2 (Semantic Chunking)

---

## Mục tiêu

Sau khi có embedding thật (P1) và chunking ngữ nghĩa (P2), Phase 3 tối ưu **chất lượng tìm kiếm** và **hiệu năng** của toàn bộ pipeline RAG.

---

## 3.1 Embedding Cache

### Vấn đề

Mỗi lần chat/search, system gọi `OllamaEmbeddingService.GenerateEmbeddingAsync()` cho query text. Nếu user hỏi câu tương tự hoặc re-search, Ollama bị gọi lại → lãng phí.

### Giải pháp

**In-memory LRU cache** cho embedding:

```csharp
public sealed class CachingEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _inner;
    private readonly MemoryCache _cache; // LRU, max 1000 entries, TTL 30 phút
}
```

| Config | Giá trị |
|--------|---------|
| Max entries | 1000 |
| TTL | 30 phút |
| Cache key | SHA256(text) |

### Phân công: Phước (@ChickMann), 2h

---

## 3.2 Re-ranking (Cross-encoder)

### Vấn đề

Hiện tại search trả về top-K chunks theo cosine distance. Nhưng cosine distance chỉ đo độ tương tự embedding, không thực sự "hiểu" mối liên hệ giữa query và chunk. Kết quả: chunk có thể chứa keyword nhưng không trả lời đúng câu hỏi.

### Giải pháp

Thêm **re-ranking step** sau vector search:

```
Query → Embedding → Vector Search (top 20) → Re-ranker (top 5) → LLM
                                              ↑
                                   Cross-encoder model
                                   chấm điểm relevance
                                   cho từng cặp (query, chunk)
```

| Model đề xuất | Lý do | Trạng thái |
|--------------|-------|-----------|
| `bge-reranker-v2-m3` (GGUF) | Hỗ trợ đa ngôn ngữ, có sẵn GGUF cho Ollama | ✅ Khả dụng |
| Groq API re-rank endpoint | Không cần self-host, trả phí theo token | ⚠️ Cần budget |

> **⚠️ TRƯỚC KHI CODE:** Chạy spike 2h kiểm tra Ollama re-rank với GGUF model:
> 1. Pull GGUF re-ranker: `ollama pull bge-reranker-v2-m3`
> 2. Set `OLLAMA_NEW_ENGINE=1`
> 3. Test `POST /api/rerank` với 5 cặp (query, document)
> 4. Verify scores có ý nghĩa (khác nhau, không toàn 0)
> 5. Nếu không hoạt động → chuyển sang Groq API hoặc inference server riêng
> **Thời gian spike: 2h. Không bắt đầu code re-rank trước khi spike pass.**

### Files thay đổi

| File | Thay đổi |
|------|---------|
| `Services/Rag/RagSearchService.cs` | Thêm re-rank step giữa `SearchPostgresAsync` và `ToDto` |
| `Options/RagOptions.cs` | `ReRankEnabled`, `ReRankTopN` (mặc định 20 → 5) |
| `Services/Rag/ReRankService.cs` | **Mới** — wrapper gọi Groq/Ollama re-rank API |

### Phân công: Sơn (@ThShadow), 4h

---

## 3.3 Hybrid Search (Keyword + Vector)

### Vấn đề

Pure vector search bỏ lỡ exact keyword matches. Ví dụ: user tìm "SWP391" → vector search có thể trả về chunk về "software project" thay vì đúng môn SWP391.

### Giải pháp

**Hybrid search = vector + keyword** với reciprocal rank fusion (RRF):

```
Score_final = alpha * Score_vector + (1 - alpha) * Score_keyword
```

| Component | Implementation |
|-----------|---------------|
| Vector search | pgvector `CosineDistance` (đã có) |
| Keyword search | PostgreSQL `tsvector` + `ts_rank` (full-text search) |
| Fusion | RRF hoặc weighted sum |

```sql
-- Tạo text search config cho tiếng Việt (dùng unaccent + simple)
-- Yêu cầu: CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE TEXT SEARCH CONFIGURATION vietnamese (COPY = simple);
ALTER TEXT SEARCH CONFIGURATION vietnamese
    ALTER MAPPING FOR hword, hword_part, word
    WITH unaccent, simple;

-- Thêm tsvector column vào document_chunks
ALTER TABLE document_chunks ADD COLUMN search_vector tsvector
GENERATED ALWAYS AS (to_tsvector('vietnamese', content)) STORED;

-- GIN index cho full-text search
CREATE INDEX idx_document_chunks_search ON document_chunks USING GIN (search_vector);

-- Hybrid query
SELECT *, 
  (0.7 * (1.0 - (embedding <=> query_vector) / 2.0) + 
   0.3 * ts_rank(search_vector, plainto_tsquery('vietnamese', query))) AS hybrid_score
FROM document_chunks
WHERE ...
ORDER BY hybrid_score DESC
LIMIT 5;
```

### Files thay đổi

| File | Thay đổi |
|------|---------|
| Migration | Thêm `search_vector tsvector` column + GIN index |
| `Services/Rag/RagSearchService.cs` | Thêm `SearchHybridAsync()` |
| `Options/RagOptions.cs` | `HybridSearchEnabled`, `VectorWeight` (mặc định 0.7) |
| `Dtos/RagDtos.cs` | `RagSearchRequest` thêm `SearchMode` (vector/hybrid/keyword) |

### Phân công: Bảo (@TranGiaBao2005), 5h

---

## 3.4 Benchmark Automation

### Vấn đề

Benchmark hiện tại thủ công — chạy `POST /api/benchmark/run` rồi xem kết quả. Không có baseline history, không có alert khi quality giảm.

### Giải pháp

| Việc | Mô tả |
|------|-------|
| Benchmark dataset | Tạo 10 câu hỏi + 5 PDF mẫu → expected relevant chunks |
| Automated run | Scheduled job (mỗi tuần) chạy benchmark, lưu kết quả vào DB |
| Dashboard | Trang admin `/admin/benchmarks` hiển thị chart recall@5, MRR, latency theo thời gian |
| Alert | Nếu recall@5 giảm >10% so với baseline → gửi warning |

### Files thay đổi

| File | Thay đổi |
|------|---------|
| `Services/Rag/Benchmarking/BenchmarkRunner.cs` | Tự động lưu kết quả vào DB |
| `Data/Entities/BenchmarkResult.cs` | **Mới** — entity lưu kết quả benchmark |
| `Components/Admin/Benchmarks/` | **Mới** — Blazor page hiển thị chart |
| Migration | Thêm bảng `benchmark_results` |

### Phân công: Phước (@ChickMann), 4h

---

## 3.5 Observability

### Vấn đề

Không có metrics về embedding latency, failure rate, model version → không debug được khi có vấn đề.

### Giải pháp

| Metric | Cách đo | Cảnh báo |
|--------|---------|---------|
| Embedding latency | `Stopwatch` trong `OllamaEmbeddingService`, log p50/p95/p99 | > 5s |
| Embedding failure rate | Counter mỗi lần retry thất bại | > 5% |
| Search latency | `Stopwatch` trong `RagSearchService` | > 2s |
| Chunk count per doc | Log sau mỗi lần ingest | < 3 hoặc > 100 |
| Model version | Log `OllamaOptions.Model` khi startup | Thay đổi version |

Triển khai qua `ILogger` + structured logging (đã có sẵn). Không cần thêm package.

### Phân công: Sơn (@ThShadow), 1h

---

## Tổng kết Phase 3

| Component | Mức độ ưu tiên | Effort | Người | Impact |
|-----------|---------------|--------|-------|--------|
| **Embedding Cache** | 🔴 Cao | 2h | Phước | Giảm 80% Ollama calls |
| **Re-ranking** | 🔴 Cao | 4h | Sơn | Cải thiện search quality rõ rệt |
| **Hybrid Search** | 🟡 Trung bình | 5h | Bảo | Cải thiện exact-match queries |
| **Benchmark Auto** | 🟡 Trung bình | 4h | Phước | Phát hiện regression sớm |
| **Observability** | 🟢 Thấp | 1h | Sơn | Debug dễ hơn |

### Thứ tự triển khai

```
Phước: Embedding Cache → Benchmark Auto
         (2h)               (4h)
Sơn:   Re-ranking → Observability
         (4h)          (1h)
Bảo:   Hybrid Search
         (5h)
```

### Phân công tổng

| Người | GitHub | Việc | Thời gian |
|-------|--------|------|----------|
| **Sơn** | `@ThShadow` | Re-ranking + Observability | 5h |
| **Bảo** | `@TranGiaBao2005` | Hybrid Search | 5h |
| **Phước** | `@ChickMann` | Embedding Cache + Benchmark Automation | 6h |

**Tổng thời gian:** ~18h (2-3 ngày, 3 người song song)

---

## Rollback

Tất cả feature đều toggle qua config:

```json
"Rag": {
    "EmbeddingCacheEnabled": true,
    "ReRankEnabled": true,
    "HybridSearchEnabled": true,
    "SearchMode": "hybrid"  // "vector" để quay lại cũ
}
```

Tắt 1 dòng config → quay về behavior cũ. Không cần rollback code.
