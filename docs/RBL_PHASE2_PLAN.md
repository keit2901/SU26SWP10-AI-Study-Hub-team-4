# RBL Phase 2 — Semantic Chunking

> **Status:** DRAFT — đợi Phase 1 hoàn thành, teammates review  
> **Ngày:** 2026-06-26  
> **Phụ thuộc:** Phase 1 (Real Embedding) phải xong trước

---

## Mục tiêu

Thay chunking theo ký tự cố định (500 ký tự sliding window) bằng **chunking theo ngữ nghĩa** (sentence, paragraph, section). Cải thiện chất lượng RAG search — mỗi chunk là một đơn vị ý nghĩa hoàn chỉnh, không bị cắt giữa câu.

---

## Vấn đề hiện tại

`ChunkingService.cs` cắt văn bản dựa trên `ChunkSizeChars` (đã giảm về 500) + `ChunkOverlapChars` (200). `FindChunkBoundary()` cố gắng tìm ranh giới tự nhiên (`\n\n`, `. `, `? `...) nhưng:

| Vấn đề | Hậu quả |
|--------|---------|
| Chunk cắt ngang câu | Embedding của chunk chứa nửa câu → kém ngữ nghĩa |
| Chunk quá ngắn/nhiễu | Header, footer, page number thành chunk riêng → noise trong search |
| Overlap cố định 200 | Không phân biệt được overlap cần thiết (giữa section) vs thừa (giữa paragraph) |
| Không tận dụng cấu trúc tài liệu | PDF có heading, section, list không được phát hiện |

---

## Thiết kế

### Cấu trúc chunk mới

```
Document
├── Section "Chương 1: Giới thiệu"
│   ├── Paragraph "AI là một lĩnh vực..."
│   │   ├── Sentence "AI (Artificial Intelligence) là..."
│   │   └── Sentence "Nó bao gồm nhiều nhánh con..."
│   └── Paragraph "Trong giáo dục..."
└── Section "Chương 2: Machine Learning"
    └── ...
```

### Chiến lược chunking 3 tầng

| Tầng | Điều kiện | Kích thước | Khi nào dùng |
|------|----------|-----------|-------------|
| **Sentence** | Kết thúc bởi `.`, `?`, `!`, `\n` | 50-300 ký tự | Mặc định — tách câu đơn |
| **Paragraph** | 2+ sentence liên tiếp, tổng ≤ 500 ký tự | 100-500 ký tự | Gộp câu ngắn thành đoạn |
| **Section** | Heading + các paragraph con, tổng ≤ 1000 ký tự | 200-1000 ký tự | Khi phát hiện heading (chữ in hoa, đánh số, bold) |

### Merge rules

| Rule | Mô tả |
|------|-------|
| **MR1** | Nếu chunk < 100 ký tự → merge với chunk tiếp theo (tránh chunk quá nhỏ) |
| **MR2** | Nếu merge vượt 800 ký tự → tách tại ranh giới sentence gần nhất |
| **MR3** | Heading luôn được giữ làm section title, không merge với nội dung |
| **MR4** | List items (bullet points) được merge thành 1 chunk nếu tổng ≤ 500 ký tự |

### Overlap

| Loại chunk | Overlap |
|-----------|---------|
| Sentence → Paragraph | Không overlap |
| Paragraph → Paragraph | 1 sentence cuối của paragraph trước làm overlap |
| Section → Section | 1 paragraph cuối của section trước làm overlap |

---

## Files cần thay đổi

| File | Thay đổi |
|------|---------|
| `Services/Rag/ChunkingService.cs` | **Viết lại hoàn toàn** — impl mới dùng semantic boundary detection |
| `Services/Rag/RagContracts.cs` | Giữ nguyên interface `IChunkingService`, thêm `DocumentChunkDraft` fields (SectionTitle?) |
| `appsettings.json` → `Rag` | Thêm `ChunkingStrategy: "semantic"`, `MinChunkChars`, `MaxSectionChars` |
| `Options/RagOptions.cs` | Thêm properties cho semantic chunking |

### Cấu trúc code mới

```csharp
public sealed class SemanticChunkingService : IChunkingService
{
    public IReadOnlyList<DocumentChunkDraft> Chunk(Guid documentId, IReadOnlyList<ExtractedPage> pages)
    {
        // 1. Parse pages → list of TextBlock (heading / paragraph / list / table)
        var blocks = _blockParser.Parse(pages);
        
        // 2. Split blocks → sentences
        var sentences = _sentenceSplitter.Split(blocks);
        
        // 3. Merge sentences → chunks theo rules MR1-MR4
        var chunks = _chunkMerger.Merge(sentences);
        
        // 4. Assign PageNumber, ChunkIndex
        return AssignMetadata(chunks, documentId);
    }
}
```

---

## Phân công

| Người | GitHub | Việc | Effort | Phụ thuộc |
|-------|--------|------|--------|-----------|
| **Sơn** | `@ThShadow` | `SentenceSplitter` — tách sentence, xử lý tiếng Việt | 3h | Không |
| **Bảo** | `@TranGiaBao2005` | `BlockParser` — phát hiện heading, paragraph, list | 3h | Không |
| **Phước** | `@ChickMann` | `ChunkMerger` + tích hợp + test + benchmark | 3h | ⚠️ Chờ Sơn + Bảo xong |

> **⚠️ Lưu ý:** Phước bị block bởi Sơn và Bảo. Trong lúc chờ, Phước có thể chuẩn bị test dataset + viết test framework.

---

## Test

| Test | Mô tả |
|------|-------|
| Unit: SentenceSplitter | Input tiếng Việt có abbreviations → output đúng ranh giới câu |
| Unit: BlockParser | Input PDF page text → detect heading vs paragraph |
| Unit: ChunkMerger | Input 20 short sentences → merge thành 5-8 chunks |
| Integration | Upload 3 PDF (tiếng Việt, tiếng Anh, code) → verify chunk count + content |
| Benchmark | So sánh recall@5 giữa fixed-size và semantic chunking |

---

## Timeline

| Phase | Thời gian | Ghi chú |
|-------|----------|---------|
| Thiết kế SentenceSplitter + BlockParser | 1h | Cả team |
| Code SentenceSplitter (Sơn) + BlockParser (Bảo) — song song | 3h | Phước chuẩn bị test dataset |
| Code ChunkMerger + integrate (Phước) | 2h | Cần Sơn + Bảo xong |
| Test + benchmark | 2h | Cả team |
| **Tổng** | **~12h** (2-3 ngày) | Phước bị block 3h đầu |

---

## Rollback

Giữ lại `ChunkingService` cũ, đặt tên `FixedSizeChunkingService`. Đăng ký qua config:

```json
"Rag": {
    "ChunkingStrategy": "semantic"  // "fixed" để quay lại cũ
}
```

`Program.cs`:
```csharp
if (ragOptions.ChunkingStrategy == "semantic")
    services.AddScoped<IChunkingService, SemanticChunkingService>();
else
    services.AddScoped<IChunkingService, FixedSizeChunkingService>();
```
