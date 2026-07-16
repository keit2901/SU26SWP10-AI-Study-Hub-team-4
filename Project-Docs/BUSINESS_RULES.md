# AI Study Hub — Business Rules (Quy tắc Nghiệp vụ)

> **Version:** 2026-06-26
> **Branch:** `sprint2/upload-improve-and-ai-chat`
> **Dành cho:** Developer, Tester, Product Owner — hiểu chính xác hệ thống hoạt động như thế nào trong từng tình huống.

---

## Mục lục

1. [Auth & User](#1-auth--user)
2. [Document](#2-document)
3. [Folder](#3-folder)
4. [RAG & Embedding](#4-rag--embedding)
5. [AI Chat](#5-ai-chat)
6. [Quiz](#6-quiz)
7. [Community Reports](#7-community-reports)
8. [Admin](#8-admin)

---

## 1. Auth & User

### 1.1 Đăng ký (Register)

| Rule | Chi tiết |
|------|---------|
| **R1.1** | Email không được trùng với user đã tồn tại trong Supabase |
| **R1.2** | Mật khẩu tối thiểu 6 ký tự (theo Supabase GoTrue policy) |
| **R1.3** | Phải vượt qua reCAPTCHA (trừ khi `Recaptcha:AllowDevelopmentFallback=true` ở môi trường dev) |
| **R1.4** | Sau khi Supabase tạo user thành công, hệ thống tự động tạo `public.users` row với role mặc định là "Student" |
| **R1.5** | Username không được trùng trong bảng `public.users` (unique constraint) |
| **R1.6** | Nếu Supabase tạo user thành công nhưng insert `public.users` thất bại → rollback Supabase user (best-effort cleanup) |

### 1.2 Đăng nhập (Login)

| Rule | Chi tiết |
|------|---------|
| **R1.7** | Email + password gửi lên Supabase GoTrue `/token?grant_type=password` |
| **R1.8** | Nếu tài khoản chưa có trong `public.users` → tự động tạo (xử lý user được tạo từ Supabase dashboard) |
| **R1.9** | Nếu `public.users.IsActive = false` → từ chối đăng nhập (403, `user_inactive`) |
| **R1.10** | Access token JWT có TTL configurable (`Supabase:AccessTokenSeconds`, mặc định 3600s) |
| **R1.11** | Refresh token có TTL do Supabase quy định |
| **R1.12** | Phải vượt qua reCAPTCHA (trừ dev fallback) |

### 1.3 Token & Session

| Rule | Chi tiết |
|------|---------|
| **R1.13** | Mọi API endpoint (trừ login/register/refresh/public) yêu cầu JWT Bearer token |
| **R1.14** | JWT được validate bởi Supabase (Issuer, Audience, signing key) |
| **R1.15** | `supabaseUserId` được extract từ JWT claim `sub` → dùng để tra `public.users` |
| **R1.16** | Refresh token có thể dùng để lấy access token mới (POST `/api/auth/refresh`) |
| **R1.17** | Logout sẽ revoke token tại Supabase (global signout) |

### 1.4 Profile

| Rule | Chi tiết |
|------|---------|
| **R1.18** | `GET /api/auth/me` trả về thông tin user hiện tại (username, fullName, email, role, totalTokensUsed) |
| **R1.19** | `totalTokensUsed` được cập nhật sau mỗi lần gọi AI (chat + quiz) |
| **R1.20** | Chỉ user `IsActive=true` mới có thể dùng các chức năng khác |

---

## 2. Document

### 2.1 Upload

| Rule | Chi tiết |
|------|---------|
| **R2.1** | **Dung lượng tối đa:** 50MB (`DocumentService.MaxFileSizeBytes`) |
| **R2.2** | **Định dạng cho phép:** PDF (.pdf), DOCX (.docx), PPTX (.pptx), DOC (.doc), PPT (.ppt) |
| **R2.3** | **Mã môn học** bắt buộc, format: 3 chữ in hoa + 3 số (regex `^[A-Z]{3}[0-9]{3}$`), ví dụ: SWP391, PRN232 |
| **R2.4** | **Học kỳ** bắt buộc, format: SP/SU/FA/WI + 2 số (regex `^(SP\|SU\|FA\|WI)[0-9]{2}$`), ví dụ: SU26, FA25 |
| **R2.5** | FolderId là optional (null = tài liệu không thuộc thư mục nào) |
| **R2.6** | User phải active (`IsActive=true`) |
| **R2.7** | File rỗng (≤0 byte) → từ chối (400, `empty_file`) |
| **R2.8** | MIME type không đúng với đuôi file → từ chối (400, `invalid_mime_type`) |
| **R2.9** | MIME type không nằm trong whitelist → từ chối (400, `unsupported_mime_type`) |

### 2.2 Ingest (Xử lý tài liệu)

| Rule | Chi tiết |
|------|---------|
| **R2.10** | Ingest tự động chạy sau upload thành công (fire-and-forget, không blocking response) |
| **R2.11** | Có thể trigger re-ingest thủ công: `POST /api/documents/{id}/ingest` |
| **R2.12** | **Quy trình ingest:** Download file từ Supabase → Extract text (PdfPig/OpenXML) → Mô tả ảnh (Groq Vision) → Chunk → Embedding → Lưu vào DB |
| **R2.13** | Nếu ingest thất bại → `Document.Status = Failed`, `ErrorMessage` lưu lý do (tối đa 1000 ký tự) |
| **R2.14** | Nếu ingest thành công → `Document.Status = Ready` |
| **R2.15** | Re-ingest sẽ **xóa toàn bộ chunk cũ** và tạo chunk mới (replace, không merge) |
| **R2.16** | Transaction: nếu 1 chunk lỗi → toàn bộ document rollback, trạng thái = Failed |
| **R2.17** | Chỉ user sở hữu document mới có quyền re-ingest |

### 2.3 Image Description trong Ingest

| Rule | Chi tiết |
|------|---------|
| **R2.18** | **Giới hạn ảnh:** tối đa `GroqOptions.MaxImagesPerDocument` (mặc định 50) ảnh mỗi tài liệu |
| **R2.19** | **Kích thước ảnh tối đa:** `GroqOptions.MaxImageSizeMb` (mặc định 3MB) |
| **R2.20** | Ảnh được gửi theo batch 5 ảnh/lần → mô tả qua Groq Vision (Llama 4 Scout) |
| **R2.21** | Nếu số ảnh vượt budget → skip các ảnh vượt ngưỡng (tùy chọn `SkipImagesWhenLimitExceeded`) |
| **R2.22** | Mô tả ảnh được append vào text của trang chứa ảnh đó |

### 2.4 Chunking

| Rule | Chi tiết |
|------|---------|
| **R2.23** | **Kích thước chunk:** `RagOptions.ChunkSizeChars` (mặc định 1000 ký tự) |
| **R2.24** | **Overlap:** `RagOptions.ChunkOverlapChars` (mặc định 200 ký tự) |
| **R2.25** | Chunk boundary được tìm tại ranh giới tự nhiên: `\n\n` > `\n` > `. ` > `? ` > `! ` > `; ` > `, ` > whitespace |
| **R2.26** | Mỗi chunk gán `ChunkIndex` tăng dần (bắt đầu từ 0) trong phạm vi document |
| **R2.27** | Mỗi chunk lưu `PageNumber` (nếu có từ PDF) |
| **R2.28** | `TokenCount` được ước lượng = `ceil(Content.Length / 4)` |
| **R2.29** | **⚠️ Rủi ro:** ChunkSize=1000 có thể vượt 256-token limit của embedding model. Khuyến nghị giảm về 500 khi deploy Ollama |

### 2.5 Liệt kê & Xem

| Rule | Chi tiết |
|------|---------|
| **R2.30** | `GET /api/documents` chỉ trả về document của user hiện tại |
| **R2.31** | Có thể filter theo: `SubjectCode`, `Semester`, `FolderId`, `Q` (tìm theo tên file) |
| **R2.32** | `GET /api/documents/{id}` trả về chi tiết + signed download URL (5 phút TTL) |
| **R2.33** | `GET /api/documents/{id}/file` trả về Microsoft Office Online Viewer URL |
| **R2.34** | `GET /api/documents/{id}/content` trả về toàn bộ chunks đã extract (text content) |
| **R2.35** | Download URL là signed URL từ Supabase Storage, TTL = 5 phút |

### 2.6 Sửa & Xóa

| Rule | Chi tiết |
|------|---------|
| **R2.36** | **Đổi tên:** tên file mới 1-255 ký tự |
| **R2.37** | **Di chuyển:** có thể chuyển document vào folder (FolderId) hoặc ra khỏi folder (FolderId=null) |
| **R2.38** | Nếu folder đích có ≥30 document → từ chối (400, `max_documents_per_folder`) |
| **R2.39** | Folder đích phải thuộc về user |
| **R2.40** | **Xóa cứng (hard delete):** xóa document khỏi DB + xóa file khỏi Supabase Storage + xóa tất cả chunks |
| **R2.41** | Chỉ chủ sở hữu mới được sửa/xóa document |

### 2.7 Document Status Flow

```
Upload → Ready → (User triggers ingest) → Processing → Ready
                                              ↘ Failed (có ErrorMessage)
```

---

## 3. Folder

### 3.1 Tạo & Sửa

| Rule | Chi tiết |
|------|---------|
| **R3.1** | **Tên folder:** bắt buộc, 1-100 ký tự, unique trong phạm vi 1 user |
| **R3.2** | **Mô tả:** optional, tối đa 500 ký tự |
| **R3.3** | **Icon:** optional string (emoji hoặc tên icon), null = mặc định |
| **R3.4** | Khi đổi tên → kiểm tra không trùng với folder khác của cùng user |
| **R3.5** | Chỉ chủ sở hữu mới được sửa folder |

### 3.2 Favorite (Yêu thích)

| Rule | Chi tiết |
|------|---------|
| **R3.6** | Toggle một lần → đảo trạng thái `IsFavorite` |
| **R3.7** | Không giới hạn số lượng folder yêu thích |
| **R3.8** | Folder yêu thích hiển thị trước trong danh sách (sort `IsFavorite` desc → `UpdatedAt` desc) |
| **R3.9** | Chỉ chủ sở hữu mới toggle favorite |

### 3.3 Share (Chia sẻ)

| Rule | Chi tiết |
|------|---------|
| **R3.10** | Toggle một lần → đảo trạng thái `IsShared` |
| **R3.11** | Khi bật share → `SharedAt = DateTimeOffset.UtcNow` |
| **R3.12** | Khi tắt share → `SharedAt = null` |
| **R3.13** | Folder đã share hiển thị ở endpoint public `GET /api/folders/shared` (không cần auth) |
| **R3.14** | Người share vẫn xem được folder của mình trong `GET /api/folders/personal-shared` |
| **R3.15** | Chỉ chủ sở hữu mới toggle share |

### 3.4 Vote (Like/Dislike)

| Rule | Chi tiết |
|------|---------|
| **R3.16** | Mỗi user chỉ vote 1 lần cho mỗi folder (composite PK: `folder_id + user_id`) |
| **R3.17** | Vote mới → tạo hoặc cập nhật `FolderReaction` |
| **R3.18** | `LikeCount` và `DislikeCount` hiển thị trong `FolderDto` |
| **R3.19** | `CurrentUserVote` = `true` (like), `false` (dislike), `null` (chưa vote) |
| **R3.20** | **Không được tự vote folder của mình** (sẽ bị chặn) |

### 3.5 Copy Shared Folder

| Rule | Chi tiết |
|------|---------|
| **R3.21** | User có thể copy folder đã share về thư viện của mình |
| **R3.22** | Copy tạo folder mới + copy tất cả document bên trong |
| **R3.23** | Tên folder copy: `"{tên gốc} (copy)"` hoặc thêm suffix nếu trùng |
| **R3.24** | Không copy reactions, favorite status, share status |
| **R3.25** | Document copy giữ nguyên SubjectCode, Semester |

### 3.6 Xóa

| Rule | Chi tiết |
|------|---------|
| **R3.26** | Xóa folder → xóa toàn bộ document bên trong (cascade delete) |
| **R3.27** | Mỗi document bị xóa → xóa file khỏi Supabase Storage + xóa chunks |
| **R3.28** | Chỉ chủ sở hữu mới được xóa folder |

---

## 4. RAG & Embedding

### 4.1 Embedding

| Rule | Chi tiết |
|------|---------|
| **R4.1** | Hiện tại dùng `FakeEmbeddingService` (FNV-1a hash), KHÔNG phải embedding thật |
| **R4.2** | **Dimension:** 384 (khớp với `vector(384)` trong PostgreSQL) |
| **R4.3** | Fake embedding là deterministic (cùng input → cùng output) và reproducible |
| **R4.4** | Vector được normalize về unit vector trước khi lưu |
| **R4.5** | **⚠️ Kế hoạch:** Thay bằng Ollama `all-minilm:l6-v2` (384-dim, 256-token limit) |

### 4.2 Semantic Search

| Rule | Chi tiết |
|------|---------|
| **R4.6** | Search chỉ trả về chunk của user hiện tại (`UserId == profile.Id`) |
| **R4.7** | Chỉ search trên document có `Status == Ready` |
| **R4.8** | **TopK:** mặc định 5, tối thiểu 1, tối đa `MaxTopK` (mặc định 10) |
| **R4.9** | **Filters optional:** `DocumentId`, `DocumentIds` (nhiều), `FolderId`, `SubjectCode`, `Semester`, `TopicKeyword` |
| **R4.10** | **SubjectCode filter:** so khớp chính xác sau khi normalize về UPPERCASE |
| **R4.11** | **Semester filter:** so khớp chính xác sau khi normalize về UPPERCASE |
| **R4.12** | **TopicKeyword filter:** dùng `ILIKE '%keyword%'` trên chunk content → text-level filter, KHÔNG phải vector filter |
| **R4.13** | **Cosine Distance:** dùng pgvector operator `<=>` qua `CosineDistance()` EF function |
| **R4.14** | **IVFFlat index:** `lists = 100`, cần ít nhất 1000 chunks để index hoạt động hiệu quả |
| **R4.15** | Nếu query rỗng → từ chối |

### 4.3 Search Results

| Rule | Chi tiết |
|------|---------|
| **R4.16** | Mỗi kết quả gồm: `SourceLabel` (S1, S2...), `FileName`, `ChunkIndex`, `PageNumber`, `ContentExcerpt`, `Score` |
| **R4.17** | `ContentExcerpt` bị cắt ở 500 ký tự |
| **R4.18** | `Score` là cosine distance (càng thấp = càng giống) |

---

## 5. AI Chat

### 5.1 Chat Session

| Rule | Chi tiết |
|------|---------|
| **R5.1** | Mỗi phiên chat thuộc về 1 user + có thể gắn với 1 folder (optional) |
| **R5.2** | `TopK` lưu trong session (mặc định 5) |
| **R5.3** | `Model` lưu trong session (null = auto-select) |
| **R5.4** | Session title tự động lấy từ câu hỏi đầu tiên |

### 5.2 RAG Context Building

| Rule | Chi tiết |
|------|---------|
| **R5.5** | Nếu user chọn document/folder → tìm kiếm ngữ cảnh trước khi gọi LLM |
| **R5.6** | Nếu không chọn document → trả lời bằng general knowledge (không RAG) |
| **R5.7** | Sources được map thành `[S1]`, `[S2]`... |
| **R5.8** | **Budget:** `MaxContextChars` (mặc định 6000 ký tự) — mỗi source excerpt được trimmed theo budget còn lại |
| **R5.9** | **Chat history:** 5 exchanges gần nhất (10 messages), assistant answer truncated ở 300 ký tự |
| **R5.10** | Chat history được append vào user prompt dưới dạng "## Previous conversation" |

### 5.3 System Prompt

| Rule | Chi tiết |
|------|---------|
| **R5.11** | Chat có document scope: system prompt yêu cầu chỉ dùng excerpts, cite nguồn `[S1]` |
| **R5.12** | Chat không có document scope: system prompt cho phép dùng general knowledge |
| **R5.13** | System prompt bao gồm current date |
| **R5.14** | Knowledge cutoff: December 2023 |
| **R5.15** | Tone: tutorial, giải thích rõ ràng, dùng ví dụ, format markdown |

### 5.4 Model Routing

| Rule | Chi tiết |
|------|---------|
| **R5.16** | Nếu model bắt đầu bằng `gemini` → Gemini API (GeminiChatCompletionClient) |
| **R5.17** | Ngược lại → Groq API (GroqChatCompletionClient) |
| **R5.18** | Nếu user chọn Gemini nhưng chưa cấu hình API key → tự động fallback về Groq |
| **R5.19** | **Model mặc định Groq:** `llama-3.3-70b-versatile` |
| **R5.20** | **Model mặc định Gemini:** `gemini-2.5-flash` |
| **R5.21** | **Temperature:** 0.2 (cả Groq và Gemini) |
| **R5.22** | **MaxTokens:** 4096 (cả Groq và Gemini) |

### 5.5 Error Handling

| Rule | Chi tiết |
|------|---------|
| **R5.23** | Nếu LLM call thất bại và `UseLocalDemoFallback = true` → trả về fake answer |
| **R5.24** | Nếu LLM call thất bại và `UseLocalDemoFallback = false` → trả về 503 |
| **R5.25** | Gemini client có retry: 3 lần cho 429 + transient, 1 lần cho 5xx, timeout 60s |

### 5.6 Persistence

| Rule | Chi tiết |
|------|---------|
| **R5.26** | Mỗi exchange (hỏi + đáp) được lưu vào `chat_messages` |
| **R5.27** | `ChatMessage.Role` = `"user"` hoặc `"assistant"` |
| **R5.28** | `SequenceNumber` tăng dần trong mỗi session (unique) |
| **R5.29** | `MetadataJson` lưu scope label, refusal reason, duration, sources |

---

## 6. Quiz

### 6.1 Generate

| Rule | Chi tiết |
|------|---------|
| **R6.1** | Quiz yêu cầu có document scope (phải chọn ít nhất 1 document hoặc folder) |
| **R6.2** | **Số câu hỏi mặc định:** 8 (có thể tùy chỉnh qua `Count`) |
| **R6.3** | **TopK cho RAG:** `max(Clamp(Count, 3, 12) * 2, 5, 15)` |
| **R6.4** | **Filter:** SubjectCode, Semester, TopicKeyword đều được truyền vào `RagSearchRequest` |
| **R6.5** | Nếu không đủ ngữ cảnh (0 kết quả RAG) → từ chối |
| **R6.6** | Load TẤT CẢ quiz cũ của user để tránh câu hỏi trùng |
| **R6.7** | LLM được yêu cầu trả về JSON schema: `{ title, questions: [{ text, options[A-D], correctOptionId, explanation, sourceLabel }] }` |
| **R6.8** | Nếu JSON không parse được → retry tối đa 2 lần |
| **R6.9** | Nếu provider lỗi → tự động switch model (Groq ↔ Gemini) cho lần retry |
| **R6.10** | `QuestionsJson`, `AnswersJson`, `SubmittedJson` lưu dưới dạng JSONB |

### 6.2 Difficulty

| Rule | Chi tiết |
|------|---------|
| **R6.11** | 3 mức: `"easy"`, `"medium"` (default), `"hard"` |
| **R6.12** | Difficulty chỉ ảnh hưởng text trong system prompt, không thay đổi logic |
| **R6.13** | **Easy:** câu hỏi nhận biết, đáp án rõ ràng trong văn bản |
| **R6.14** | **Medium:** câu hỏi hiểu và áp dụng, cần suy luận từ văn bản |
| **R6.15** | **Hard:** câu hỏi phân tích, kết hợp nhiều nguồn, đáp án cần tư duy phản biện |

### 6.3 Save & Resume

| Rule | Chi tiết |
|------|---------|
| **R6.16** | `PATCH /api/quiz/{id}/save` lưu tiến trình: status, answers, submitted, score |
| **R6.17** | `Status` có thể là `InProgress` hoặc `Completed` |
| **R6.18** | `Score` được tính dựa trên số câu trả lời đúng |
| **R6.19** | `GET /api/quiz/resume?sessionId=` trả về quiz gần nhất của session (theo `UpdatedAt` desc) |
| **R6.20** | Quiz metadata được đồng bộ vào `chat_messages.metadata_json` qua `UpdateQuizMetadataAsync()` |

---

## 7. Community Reports

### 7.1 Report

| Rule | Chi tiết |
|------|---------|
| **R7.1** | Chỉ được báo cáo folder **đã share** (`IsShared = true`) |
| **R7.2** | **Không được tự báo cáo folder của mình** (`folder.UserId != profile.Id`) |
| **R7.3** | Mỗi user chỉ có 1 báo cáo **Pending** cho mỗi folder |
| **R7.4** | Nếu đã có báo cáo pending → từ chối (409, `duplicate_report`) |
| **R7.5** | Lý do báo cáo không được để trống |
| **R7.6** | Báo cáo mới có `Status = "Pending"` |

### 7.2 Status Flow

```
Pending → Resolved (có Resolution) 
       → Dismissed (có Resolution)
```

| Rule | Chi tiết |
|------|---------|
| **R7.7** | Chỉ Admin mới xem được danh sách báo cáo pending |
| **R7.8** | Chỉ Admin mới resolve/dismiss báo cáo |
| **R7.9** | Không thể resolve báo cáo đã xử lý (status != "Pending") |
| **R7.10** | Status hợp lệ: `"Resolved"` hoặc `"Dismissed"` |

---

## 8. Admin

### 8.1 Phân quyền

| Role | Quyền |
|------|-------|
| **Admin** | Quản lý users, documents, subjects, audit logs, system settings, community reports |
| **Student** | Chức năng cơ bản: upload, chat, quiz, folder, community |

| Rule | Chi tiết |
|------|---------|
| **R8.1** | Role được kiểm tra qua JWT claim `role` hoặc `roles` |
| **R8.2** | Admin endpoints yêu cầu `[Authorize(Roles = "Admin")]` |
| **R8.3** | Có 1 admin mặc định được seed khi app start (idempotent — không tạo lại nếu đã tồn tại) |

### 8.2 User Management

| Rule | Chi tiết |
|------|---------|
| **R8.4** | Admin có thể xem danh sách tất cả users |
| **R8.5** | Admin có thể vô hiệu hóa user (`IsActive = false`) |
| **R8.6** | Admin có thể mời user mới (gửi invite email qua Supabase) |

### 8.3 Document Moderation

| Rule | Chi tiết |
|------|---------|
| **R8.7** | Admin có thể xem tất cả documents (không giới hạn theo owner) |
| **R8.8** | Admin có thể xóa document vi phạm |

### 8.4 Audit Logs

| Rule | Chi tiết |
|------|---------|
| **R8.9** | Hệ thống ghi log các hành động quan trọng (tạo user, upload, xóa document...) |
| **R8.10** | Admin có thể xem audit log tại `/admin/audit-logs` |

---

## Phụ lục A: Error Code Reference

| HTTP Status | Error Code | Ý nghĩa |
|-------------|-----------|---------|
| 400 | `empty_file` | File upload rỗng |
| 400 | `invalid_mime_type` | MIME type không khớp đuôi file |
| 400 | `unsupported_mime_type` | Định dạng file không hỗ trợ |
| 400 | `max_documents_per_folder` | Folder đã đạt giới hạn 30 tài liệu |
| 400 | `folder_not_shared` | Chỉ báo cáo folder đã share |
| 400 | `cannot_report_own_folder` | Không được tự báo cáo folder của mình |
| 400 | `report_already_resolved` | Báo cáo đã được xử lý |
| 400 | `invalid_status` | Status không hợp lệ |
| 403 | `user_inactive` | Tài khoản bị vô hiệu hóa |
| 404 | `user_not_found` | Không tìm thấy profile user |
| 404 | `document_not_found` | Không tìm thấy tài liệu |
| 404 | `folder_not_found` | Không tìm thấy thư mục |
| 404 | `report_not_found` | Không tìm thấy báo cáo |
| 409 | `duplicate_report` | Đã có báo cáo pending cho folder này |
| 503 | `ai_service_unavailable` | LLM API không khả dụng |

## Phụ lục B: Các hằng số quan trọng

| Hằng số | Giá trị | Nơi định nghĩa |
|---------|--------|---------------|
| MaxFileSizeBytes | 50MB | `DocumentService.cs` |
| MaxDocumentsPerFolder | 30 | `DocumentService.cs` |
| SignedUrlTtlSeconds | 300s (5 phút) | `DocumentService.cs` |
| ChunkSizeChars | 1000 | `appsettings.json → RagOptions` |
| ChunkOverlapChars | 200 | `appsettings.json → RagOptions` |
| DefaultTopK | 5 | `appsettings.json → RagOptions` |
| MaxTopK | 10 | `appsettings.json → RagOptions` |
| EmbeddingDimensions | 384 | `appsettings.json → RagOptions` |
| MaxContextChars | 6000 | `appsettings.json → RagOptions` |
| MaxImagesPerDocument | 50 | `appsettings.json → GroqOptions` |
| MaxImageSizeMb | 3 | `appsettings.json → GroqOptions` |
| MaxHistoryExchanges | 5 | `SemanticKernelRagChatService.cs` |
| AssistantMessageTruncation | 300 chars | `SemanticKernelRagChatService.cs` |
| LLM Temperature | 0.2 | `GroqOptions` / `GeminiOptions` |
| LLM MaxTokens | 4096 | `GroqOptions` / `GeminiOptions` |
| Folder Name Max | 100 chars | `CreateFolderRequest` |
| Folder Description Max | 500 chars | `CreateFolderRequest` |
| Document FileName Max | 255 chars | `RenameDocumentRequest` |
| Report Reason Max | 2000 chars | DB migration |
| Quiz Default Count | 8 | `GenerateQuizRequest` |

---

> **Cập nhật lần cuối:** 2026-06-26. Mọi thay đổi nghiệp vụ phải được cập nhật vào file này.
