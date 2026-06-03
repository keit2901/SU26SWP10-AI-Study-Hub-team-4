# Sprint 2 RAG Demo Script

Audience: Sprint 2 demo for AI Study Hub RAG flow. This script targets the merged `sprint2/integration` branch with ingestion, embedding/search, chat API, and chat UI integrated.

## Demo Goal

Show the end-to-end student flow in 5-7 minutes:

```text
Login -> Upload PDF -> Extract text -> Chunk -> Embed -> Save document_chunks
-> Ask AI -> Retrieve top-K chunks -> Generate answer -> Display answer + source citations
```

## Demo Data

Use a small text-based PDF, not a scanned PDF. Recommended file name: `swp391-rag-demo-su26.pdf`.

PDF content:

```text
AI Study Hub is a platform for SWP391 students.
The system uses Retrieval-Augmented Generation to answer questions from uploaded documents.
The demo semester is SU26.
Students can verify answers by checking source citations from their uploaded files.
```

Metadata:

- Subject: `SWP391`
- Semester: `SU26`
- Folder: optional, for example `Sprint 2 Demo`

## Presenter Setup

1. Start Supabase and confirm `5432` and `8000` are listening.
2. Set `Groq:ApiKey` in user-secrets; do not put the key in appsettings.
3. Start the app:

```powershell
cd D:\FPT\summer2026\SWP391_parallel\s2_integration\AI_Study_Hub_v2
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240
```

4. Open these pages:

- Login: `http://localhost:5240/login`
- Documents: `http://localhost:5240/documents`
- Upload: `http://localhost:5240/documents/upload`
- AI Chat after UI merge: `http://localhost:5240/ai/chat`

5. Keep a DB query tab ready for chunk/embedding evidence.
6. Keep Postman/PowerShell ready for `/api/rag/search` as backup if Groq is down.

## 5-7 Minute Run Of Show

| Time | Segment | Action | Proof Point |
|---|---|---|---|
| 0:00-0:45 | Login/context | Login as demo user/admin | Authenticated workspace, owner-scoped documents. |
| 0:45-1:45 | Upload | Upload `swp391-rag-demo-su26.pdf` with `SWP391` / `SU26` | Document row + private storage object. |
| 1:45-2:45 | Indexing | Show status and DB chunk/embedding query | Chunks exist; embedding dimension is 384. |
| 2:45-3:45 | Retrieval | Call or mention `/api/rag/search` | Top-K source chunks with file/page/chunk metadata. |
| 3:45-5:15 | Chat/citations | Ask two answerable questions in `/ai/chat` | Grounded answer + source citations. |
| 5:15-6:15 | Fallback | Ask unrelated question | No hallucination / no unsupported claim. |
| 6:15-7:00 | Cleanup/close | Delete demo document or state cleanup evidence | Chunks removed or excluded from RAG. |

## Talk Track

### 1. Login And Document Workspace

Vietnamese:

> Đây là AI Study Hub. Người học đăng nhập bằng Supabase Auth và quản lý tài liệu học tập trong workspace riêng. Tất cả tài liệu và truy vấn RAG đều được lọc theo user hiện tại.

Simple English:

> This is AI Study Hub. A student signs in and manages private learning documents. RAG queries are scoped to the current authenticated user.

Action:

- Login.
- Open `Documents`.
- Show folder/filter briefly if useful.

### 2. Upload Demo PDF

Vietnamese:

> Em sẽ upload một file PDF nhỏ cho môn SWP391, kỳ SU26. Sau khi upload, backend lưu file vào Supabase Storage private và pipeline Sprint 2 chuẩn bị dữ liệu cho AI retrieval.

Simple English:

> I upload a small SWP391 PDF for semester SU26. The backend stores it privately and prepares it for AI retrieval.

Action:

- Open `Documents -> Upload document`.
- Select `swp391-rag-demo-su26.pdf`.
- Set subject `SWP391`, semester `SU26`, optional folder `Sprint 2 Demo`.
- Upload and open document list/detail.

Expected:

- Upload succeeds.
- Document appears in list.
- Detail page/download still works.

### 3. Show Indexing Evidence

Vietnamese:

> Sprint 2 biến tài liệu thành các chunk nhỏ, tạo embedding 384 chiều và lưu vào bảng `document_chunks`. Đây là phần nền để AI trả lời dựa trên tài liệu, không chỉ dựa vào trí nhớ model.

Simple English:

> Sprint 2 converts the document into chunks, creates 384-dimensional embeddings, and stores them in `document_chunks` for grounded retrieval.

Action:

Run:

```sql
select d.id, d.file_name, d.status, count(c.id) as chunk_count
from public.documents d
left join public.document_chunks c on c.document_id = d.id
where d.file_name = 'swp391-rag-demo-su26.pdf'
group by d.id, d.file_name, d.status;
```

Then run:

```sql
select c.chunk_index, c.page_number, vector_dims(c.embedding) as dims, left(c.content, 120) as excerpt
from public.document_chunks c
join public.documents d on d.id = c.document_id
where d.file_name = 'swp391-rag-demo-su26.pdf'
order by c.chunk_index;
```

Expected:

- At least one chunk exists.
- `dims` is `384`.
- Excerpt contains text from the demo PDF.

If ingestion is asynchronous:

> The ingestion worker is still processing, so I will refresh status once. If it remains pending, this is an integration blocker rather than a successful smoke result.

### 4. Show Retrieval API

Vietnamese:

> Trước khi gọi LLM, mình có thể kiểm tra endpoint retrieval. Nó trả về các đoạn tài liệu liên quan nhất, kèm file, page/chunk và excerpt để debug citation.

Simple English:

> Before generation, retrieval returns the most relevant source chunks with file and citation metadata.

Action:

Call `POST /api/rag/search` if merged:

```powershell
$body = @{
    query = "What is AI Study Hub used for?"
    subjectCode = "SWP391"
    semester = "SU26"
    topK = 5
} | ConvertTo-Json

Invoke-RestMethod `
    -Method Post `
    -Uri "http://localhost:5240/api/rag/search" `
    -Headers @{ Authorization = "Bearer <access_token>" } `
    -ContentType "application/json" `
    -Body $body
```

Expected talking points:

- Returned chunks come from the uploaded PDF.
- Results include file name, chunk index, page number when available, excerpt, and score.
- User ownership filtering prevents cross-user leakage.

### 5. Ask Answerable Question

Question:

```text
What is AI Study Hub used for?
```

Vietnamese:

> Bây giờ em hỏi câu có thông tin trực tiếp trong tài liệu. Câu trả lời phải dựa trên chunk đã retrieve và hiển thị citation.

Simple English:

> Now I ask a question that is directly answered by the document. The response should cite the retrieved source.

Expected answer:

- Mentions AI Study Hub is a platform for SWP391 students.
- Shows at least one source citation from `swp391-rag-demo-su26.pdf`.
- Source includes file name and excerpt; page number is shown if available.

### 6. Ask Semester Question

Question:

```text
Which semester is mentioned in the document?
```

Vietnamese:

> Câu này kiểm tra một fact rất cụ thể trong file, để chứng minh AI đang đọc dữ liệu upload chứ không trả lời chung chung.

Simple English:

> This checks a specific fact from the uploaded document.

Expected answer:

- Answers `SU26`.
- Includes citation from the uploaded PDF.

### 7. Ask Unrelated Question

Question:

```text
Does the document mention quantum physics?
```

Vietnamese:

> Nếu tài liệu không có thông tin, assistant phải fallback hoặc từ chối, không được tự bịa câu trả lời.

Simple English:

> If the document does not contain the information, the assistant should refuse or say it cannot answer from the available sources.

Expected answer:

- Refuses or states the uploaded document does not mention quantum physics.
- Does not invent facts.
- Does not cite unrelated text as proof unless framed as lack of evidence.

### 8. Cleanup

Vietnamese:

> Cuối cùng em xoá file demo. RAG không được tiếp tục trả về chunk của tài liệu đã xoá.

Simple English:

> Finally, I delete the demo document. Deleted content should no longer appear in retrieval results.

Action:

- Delete the document from UI or API.
- Confirm DB cleanup:

```sql
select count(*) as remaining_chunks
from public.document_chunks
where document_id = '<document-id-from-demo>';
```

Expected:

- `remaining_chunks = 0`, or search no longer returns deleted-document chunks if a later soft-delete design is introduced.

## Backup Plan

If Groq is unavailable, rate-limited, or key is missing:

Vietnamese:

> Provider AI bên ngoài đang không khả dụng, nhưng retrieval layer vẫn trả về đúng source chunks. Điều này chứng minh tài liệu đã được index và hệ thống có context có citation; phần generation sẽ chạy lại khi Groq sẵn sàng.

Simple English:

> The external AI provider is unavailable, but retrieval still returns the correct source chunks. Generation can resume when Groq is available.

Action:

- Show successful `/api/rag/search` response.
- Show chat endpoint returns a controlled provider error or fallback, not an unhandled crash.
- State that `Groq:ApiKey` must be configured through user-secrets.

If `/ai/chat` UI is not merged:

- Demo `POST /api/ai/chat/ask` from PowerShell/Postman.
- Show the JSON response has `answer`, `sources`, optional `refusalReason`, and `durationMs`.

If ingestion is not merged:

- Do not claim end-to-end RAG is complete.
- Show baseline document upload only.
- State the blocker clearly: ingestion must create chunks and embeddings after upload or via a manual ingest endpoint.

## Mentor Q&A

Q: Does the AI use other users' documents?

A:

> No. Retrieval must filter by the authenticated Supabase user mapped to `public.users`, then join documents to chunks. QA smoke includes an owner-isolation check.

Q: What happens if the PDF is scanned?

A:

> Sprint 2 demo uses text-based PDFs. If extraction returns empty text, the document should be marked failed or excluded from RAG. OCR is out of scope for Sprint 2.

Q: Why are citations basic?

A:

> Sprint 2 focuses on the end-to-end RAG path. It stores file/page/chunk metadata now; exact PDF highlights and stronger citation precision can be improved later.

Q: How do you control hallucination?

A:

> The system retrieves source chunks first, prompts the LLM to answer only from those chunks, returns a fallback when sources are missing, and shows citations in the UI.

## Closing Line

Vietnamese:

> Sprint 2 hoàn thành mục tiêu RAG core: upload tài liệu, tạo chunks và embeddings, retrieve context, sinh câu trả lời có citation, và fallback khi không có bằng chứng trong tài liệu.

Simple English:

> Sprint 2 completes the core RAG flow: upload, chunk, embed, retrieve, generate with citations, and fallback when the source does not contain the answer.
