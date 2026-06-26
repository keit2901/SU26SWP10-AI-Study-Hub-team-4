# Thu hoạch sau tranh luận với Challenger — RBL Phase 1 (Real Embedding)

**Ngày:** 2026-06-26
**Người thực hiện:** AI Agent (orchestrator + @challenger)
**Bối cảnh:** Kế hoạch triển khai embedding thật (Ollama + all-minilm:l6-v2) thay cho fake embedding hiện tại

---

## Tóm tắt

Kế hoạch RBL Phase 1 **khả thi về mặt kỹ thuật** nhưng **chưa đầy đủ**. Challenger tìm ra 8 vấn đề mà bản review ban đầu bỏ sót, trong đó có 4 rủi ro nghiêm trọng (Tiger) phải xử lý trước khi deploy.

---

## 4 rủi ro Tiger — phải xử lý trước khi code

### T1. Không gian vector hỗn tạp (CRITICAL)

**Vấn đề:** Nếu deploy real embedding mà không có chiến lược migration, chunk cũ (fake) và chunk mới (real) nằm chung DB. Khi tìm kiếm, `CosDistance` giữa fake và real vector ra kết quả vô nghĩa → kết quả tìm kiếm ngẫu nhiên, người dùng không hiểu tại sao tài liệu A hiện ra mà tài liệu B biến mất.

**Giải pháp đề xuất:**
- Option A: Thêm cột `embedding_model VARCHAR` vào bảng `document_chunks`, filter search chỉ trên chunk cùng model
- Option B: Xóa toàn bộ chunk hiện có, yêu cầu người dùng upload lại tài liệu (mất dữ liệu)
- Option C: Viết batch job re-ingest toàn bộ tài liệu với real embedding
- Option D: Giữ lại fake embedding làm fallback, thêm flag chọn model ở tầng service

**Khuyến nghị:** Option A + C (thêm cột model + batch re-ingest) — an toàn nhất, không mất dữ liệu, có khả năng rollback.

### T2. Một chunk lỗi → mất toàn bộ tài liệu

**Vấn đề:** Code hiện tại dùng transaction wrap toàn bộ quá trình ingestion. Nếu Ollama fail ở chunk 47/100 (timeout, 503, rate limit), transaction rollback → 46 chunk trước đó mất trắng, tài liệu bị đánh dấu Failed.

**Giải pháp đề xuất:**
- Lưu từng chunk độc lập (không dùng transaction wrap toàn bộ)
- Hoặc: thêm retry policy (3 lần, exponential backoff) trước khi đánh dấu chunk lỗi
- Chỉ đánh dấu tài liệu `Failed` nếu TẤT CẢ chunk đều lỗi
- Chunk lỗi đơn lẻ → gán vector null, skip khi search, ghi log

### T3. App không fail-fast khi Ollama chưa sẵn sàng

**Vấn đề:** Không có health-check lúc startup. Nếu container Ollama chưa chạy, người dùng đầu tiên upload tài liệu hoặc chat AI sẽ gặp lỗi mơ hồ ("Internal Server Error"). App fail muộn thay vì fail sớm.

**Giải pháp đề xuất:**
- Thêm `IHostedService` hoặc `IStartupFilter` gọi `GET /api/tags` đến Ollama lúc khởi động
- Nếu Ollama không reachable → log warning rõ ràng, KHÔNG crash app (để các chức năng khác vẫn hoạt động)
- Trả về thông báo thân thiện cho user: "Dịch vụ embedding đang bảo trì, vui lòng thử lại sau"

### T4. ChunkSize=700 vẫn có thể vượt 256 token

**Vấn đề:** Hàm `FindChunkBoundary()` tìm ranh giới câu gần nhất, có thể overshoot 150+ ký tự. PDF nhiều code có ~3 ký tự/token → chunk 850 ký tự = 283 token, vượt limit của `all-minilm`. Model sẽ cắt âm thầm → mất ngữ nghĩa ở cuối chunk.

**Giải pháp đề xuất:**
- Hạ `ChunkSizeChars` xuống 500 (an toàn tuyệt đối cho mọi loại nội dung)
- Hoặc: dùng tokenizer thật (tiktoken) để đếm token chính xác thay vì ước lượng `length/4`
- Test với PDF thực tế có code, bảng biểu, tiếng Việt (dấu câu chiếm nhiều token hơn tiếng Anh)

---

## 3 rủi ro Elephant — cần lưu ý dài hạn

### E1. Chưa có benchmark real vs fake embedding

Chưa có dữ liệu định lượng chứng minh real embedding tốt hơn fake embedding bao nhiêu. Cần benchmark:
- Độ chính xác tìm kiếm (recall@5, recall@10)
- Thời gian phản hồi (latency p50, p95, p99)
- RAM/CPU usage khi chạy Ollama

Nếu real embedding chỉ cải thiện 5-10% nhưng tốn thêm 2GB RAM, cần cân nhắc lại.

### E2. Áp lực RAM — Supabase + Ollama trên cùng máy

Supabase stack (Postgres + Kong + Auth + Storage + Realtime) đã ngốn ~2-3GB. Thêm Ollama + `all-minilm` (~500MB khi load model) có thể gây OOM trên máy 8GB RAM. Nếu OOM giết Postgres → hỏng dữ liệu.

**Giải pháp:** Giới hạn RAM cho container Ollama (`mem_limit: 1g`), test trước trên máy dev.

### E3. Model pull lần đầu — người dùng tưởng app treo

Lần đầu chạy, Ollama phải pull `all-minilm` (~45MB). Request embedding đầu tiên sẽ treo vài phút không có feedback gì → user tưởng app crash.

**Giải pháp:** Pre-pull model trong docker-compose (dùng `entrypoint` script pull model trước khi start service).

---

## 8 hành động cụ thể trước khi code

1. [ ] **Kiểm tra DB:** `SELECT COUNT(*) FROM document_chunks WHERE embedding IS NOT NULL` — nếu >0 thì phải có migration plan
2. [ ] **Quyết định migration strategy** cho chunk cũ (Option A/B/C/D ở trên)
3. [ ] **Hạ ChunkSizeChars** từ 1000 → 500 trong `appsettings.json`
4. [ ] **Thêm service Ollama** vào `docker-compose.yml`:
    ```yaml
    ollama:
      image: ollama/ollama:0.30.9
      volumes:
        - ollama_data:/root/.ollama
      mem_limit: 1g
      healthcheck:
        test: ["CMD", "curl", "-f", "http://localhost:11434/api/tags"]
        interval: 10s
        timeout: 5s
        retries: 5
    ```
5. [ ] **Pre-pull model** trong startup script của container
6. [ ] **Viết `OllamaEmbeddingService`** với retry (3 lần, exponential backoff) + timeout 30s
7. [ ] **Thêm health-check lúc app startup** (`IHostedService`) gọi `/api/tags`
8. [ ] **Viết integration test** xác minh 384-dim, non-zero, khác nhau giữa các input
9. [ ] **Benchmark** real vs fake embedding trước khi quyết định deploy chính thức

---

## Kết luận

Kế hoạch không sai, chỉ **thiếu**. Đừng bỏ qua phần migration — đó là thứ sẽ gây bug khó debug nhất sau này. Làm đúng 8 bước trên, RBL Phase 1 sẽ an toàn và có khả năng rollback.

**Khuyến nghị thứ tự triển khai:**
1. Push 5 commits hiện tại lên origin (giảm rủi ro mất code)
2. Làm 8 bước chuẩn bị ở trên
3. Code `OllamaEmbeddingService`
4. Deploy + test nội bộ
5. Benchmark → quyết định có deploy rộng không
