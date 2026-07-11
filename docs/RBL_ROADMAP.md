# RBL Roadmap — RAG Better Learning

> **3 phase cải thiện chất lượng RAG cho AI Study Hub**

---

| Phase | Tên | Mục tiêu | Effort | File |
|-------|-----|----------|--------|------|
| **P1** | Real Embedding | Thay FakeEmbeddingService bằng Ollama `all-minilm:l6-v2` | ~10h | [RBL_PHASE1_PLAN.md](RBL_PHASE1_PLAN.md) |
| **P2** | Semantic Chunking | Thay chunking cố định bằng chunking theo ngữ nghĩa (sentence/paragraph/section) | ~12h | [RBL_PHASE2_PLAN.md](RBL_PHASE2_PLAN.md) |
| **P3** | Quality & Performance | Embedding cache, re-ranking, hybrid search, benchmark auto, observability | ~18h | [RBL_PHASE3_PLAN.md](RBL_PHASE3_PLAN.md) |

---

## Dependency

```
P1 (Real Embedding) ──→ P2 (Semantic Chunking) ──→ P3 (Quality & Performance)
     7-9h                      ~9h                        ~18h
```

P2 phụ thuộc P1. P3 phụ thuộc P1 + P2.

---

## Tổng effort: ~40h (~6 ngày, 2 người)

---

## Team

| Người | GitHub | P1 (~10h) | P2 (~12h) | P3 (~18h) | Tổng effort |
|-------|--------|-----------|----------|----------|-------------|
| **Bảo** | `@TranGiaBao2005` | Service + migration + DI + health check + docker | Đợi replan 2 người | Đợi replan 2 người | ~5.5h (P1) |
| **Phước** | `@ChickMann` | Transaction refactor + test + benchmark + dataset | Đợi replan 2 người | Đợi replan 2 người | ~4.5h (P1) |

> **P2 và P3 cần replan cho 2 người** — hiện tại chỉ P1 đã cập nhật. P2/P3 giữ nguyên nội dung, chưa phân công lại.

### Phân công chi tiết P1

| Người | Việc | Effort |
|-------|------|--------|
| Bảo | `OllamaEmbeddingService.cs` + `OllamaOptions.cs` + migration `embedding_model` + `Program.cs` DI swap + `RagSearchService` filter + `OllamaHealthCheck.cs` + `docker-compose.yml` + model pinning | 5.5h |
| Phước | Transaction refactor (Design B) trong `DocumentIngestionService` + error handling/friendly messages + unit test mock + integration test + benchmark baseline + Vietnamese dataset + `appsettings.json` update | 4.5h |

---

## Quick links

- [P1: Real Embedding](RBL_PHASE1_PLAN.md) — **ĐÃ CHỐT**, sẵn sàng triển khai
- [P2: Semantic Chunking](RBL_PHASE2_PLAN.md) — **DRAFT**, đợi review
- [P3: Quality & Performance](RBL_PHASE3_PLAN.md) — **DRAFT**, đợi review
