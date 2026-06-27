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

| Người | GitHub | P1 | P2 | P3 |
|-------|--------|-----|-----|-----|
| **Sơn** | `@ThShadow` | OllamaEmbeddingService + migration | SentenceSplitter | Embedding Cache + Re-ranking |
| **Bảo** | `@TranGiaBao2005` | Health check + transaction refactor | BlockParser | Hybrid Search + Benchmark |
| **Phước** | `@ChickMann` | Test + benchmark baseline | ChunkMerger + integrate | Observability |

---

## Quick links

- [P1: Real Embedding](RBL_PHASE1_PLAN.md) — **ĐÃ CHỐT**, sẵn sàng triển khai
- [P2: Semantic Chunking](RBL_PHASE2_PLAN.md) — **DRAFT**, đợi review
- [P3: Quality & Performance](RBL_PHASE3_PLAN.md) — **DRAFT**, đợi review
