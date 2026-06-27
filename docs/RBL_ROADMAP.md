# RBL Roadmap — RAG Better Learning

> **3 phase cải thiện chất lượng RAG cho AI Study Hub**

---

| Phase | Tên | Mục tiêu | Effort | File |
|-------|-----|----------|--------|------|
| **P1** | Real Embedding | Thay FakeEmbeddingService bằng Ollama `all-minilm:l6-v2` | 7-9h | [RBL_PHASE1_PLAN.md](RBL_PHASE1_PLAN.md) |
| **P2** | Semantic Chunking | Thay chunking cố định bằng chunking theo ngữ nghĩa (sentence/paragraph/section) | ~9h | [RBL_PHASE2_PLAN.md](RBL_PHASE2_PLAN.md) |
| **P3** | Quality & Performance | Embedding cache, re-ranking, hybrid search, benchmark auto, observability | ~18h | [RBL_PHASE3_PLAN.md](RBL_PHASE3_PLAN.md) |

---

## Dependency

```
P1 (Real Embedding) ──→ P2 (Semantic Chunking) ──→ P3 (Quality & Performance)
     7-9h                      ~9h                        ~18h
```

P2 phụ thuộc P1. P3 phụ thuộc P1 + P2.

---

## Tổng effort: 34-36h (~5 ngày, 3 người)

---

## Team

| Người | GitHub | P1 (7-9h) | P2 (~9h) | P3 (~18h) | Tổng effort |
|-------|--------|-----------|----------|----------|-------------|
| **Sơn** | `@ThShadow` | OllamaEmbeddingService + migration + DI swap | SentenceSplitter | Re-ranking + Observability | ~11h |
| **Bảo** | `@TranGiaBao2005` | Health check + transaction refactor (Design B) | BlockParser | Hybrid Search | ~11h |
| **Phước** | `@ChickMann` | Unit test + benchmark baseline | ChunkMerger + integrate into ChunkingService | Embedding Cache + Benchmark Automation | ~11h |

### Phân công chi tiết theo từng phase

#### P1 — Real Embedding
| Người | Việc | Effort |
|-------|------|--------|
| Sơn | `OllamaEmbeddingService.cs` + `OllamaOptions.cs` + migration `embedding_model` + `Program.cs` DI swap + `RagSearchService` filter | 3h |
| Bảo | `OllamaHealthCheck.cs` + error handling/friendly messages + refactor `DocumentIngestionService` (Design B) + `docker-compose.yml` | 3h |
| Phước | Unit test mock + integration test `[Ignore]` + benchmark baseline + `appsettings.json` update | 2.5h |

#### P2 — Semantic Chunking
| Người | Việc | Effort |
|-------|------|--------|
| Sơn | `SentenceSplitter` — tách câu, xử lý tiếng Việt (abbreviations, dấu câu) | 3h |
| Bảo | `BlockParser` — phát hiện heading, paragraph, list, table từ PDF text | 3h |
| Phước | `ChunkMerger` + tích hợp vào `SemanticChunkingService` mới + test + benchmark so sánh fixed vs semantic | 3h |

#### P3 — Quality & Performance
| Người | Việc | Effort |
|-------|------|--------|
| Sơn | Re-ranking (cross-encoder, `ReRankService`) + Observability (metrics, structured logging) | 5h |
| Bảo | Hybrid Search (keyword+vector fusion, migration `search_vector`, RRF scoring) | 5h |
| Phước | Embedding Cache (`CachingEmbeddingService`, LRU) + Benchmark Automation (DB entity, scheduled job, admin dashboard) | 6h |

---

## Quick links

- [P1: Real Embedding](RBL_PHASE1_PLAN.md) — **ĐÃ CHỐT**, sẵn sàng triển khai
- [P2: Semantic Chunking](RBL_PHASE2_PLAN.md) — **DRAFT**, đợi review
- [P3: Quality & Performance](RBL_PHASE3_PLAN.md) — **DRAFT**, đợi review
