# Sprint 2 RAG Demo Script

Audience: Sprint 2 demo for AI Study Hub RAG flow. This script targets the merged `sprint2/integration` branch with ingestion, embedding/search, chat API, and chat UI integrated.

## Demo Goal

Show the end-to-end student flow:

```text
Login -> Upload PDF -> Extract text -> Chunk -> Embed -> Save document_chunks
-> Ask AI -> Retrieve top-K chunks -> Generate answer -> Display answer + source citations
```

## Demo Data

Create or use a small text-based PDF containing these facts:

```text
AI Study Hub is a platform for SWP391 students.
The system uses Retrieval-Augmented Generation to answer questions from uploaded documents.
The demo semester is SU26.
Students can verify answers by checking source citations from their uploaded files.
```

Recommended file name: `swp391-rag-demo-su26.pdf`.

Use metadata:

- Subject: `SWP391`
- Semester: `SU26`
- Folder: optional, for example `Sprint 2 Demo`

## Presenter Setup

1. Start Supabase and confirm services are healthy.
2. Start the app:

```powershell
cd D:\FPT\summer2026\SWP391_parallel\s2_integration\AI_Study_Hub_v2
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240
```

3. Open:

- App login: `http://localhost:5240/login`
- Documents: `http://localhost:5240/documents`
- Upload: `http://localhost:5240/documents/upload`
- AI Chat, after UI merge: `http://localhost:5240/ai/chat`

4. Keep a DB query tab ready for chunk/embedding evidence.

## Demo Flow

### 1. Login

Narration:

> I am logging into AI Study Hub as a student/admin. Sprint 2 keeps the existing Supabase GoTrue authentication from Sprint 1, so all document and RAG queries are scoped to the authenticated user.

Action:

- Login with seeded admin or a demo student.
- Confirm the navigation shows document workspace links.

### 2. Upload Document

Narration:

> I will upload a small text-based PDF. The document metadata uses subject `SWP391` and semester `SU26`, which we can later use as filters for retrieval.

Action:

- Go to `Documents -> Upload document`.
- Select `swp391-rag-demo-su26.pdf`.
- Enter `SWP391` and `SU26`.
- Submit.

Expected:

- Upload succeeds.
- Document appears in the list.
- Detail page/download still works.

### 3. Show Ingestion Evidence

Narration:

> After upload, Sprint 2 ingestion extracts text, creates overlapping chunks, generates 384-dimensional embeddings, and stores them in `public.document_chunks`.

Action:

Run a DB check:

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

Fallback if ingestion is asynchronous:

> The ingestion worker is still processing, so I will refresh status once. If it remains pending, this is an integration blocker rather than a successful smoke.

### 4. Show RAG Search API

Narration:

> Before generation, the retrieval endpoint can return the chunks that will ground the answer. This lets us debug relevance and citations separately from the LLM.

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

- Returned chunks are from the uploaded PDF.
- Each result includes file name, chunk index, page number when available, excerpt, and score.
- User ownership filtering prevents cross-user leakage.

### 5. Ask Answerable Question

Narration:

> Now I will ask a question that is answerable from the uploaded document. The response should be grounded and include source citations.

Action:

- Open `/ai/chat`.
- Ask: `What is AI Study Hub used for?`

Expected answer:

- Mentions it is a platform for SWP391 students.
- Shows at least one source citation from `swp391-rag-demo-su26.pdf`.
- Source includes file name and excerpt; page number is shown if available.

### 6. Ask Metadata Question

Narration:

> This confirms the system can answer a specific fact from the document, not just a generic product question.

Action:

- Ask: `Which semester is mentioned in the document?`

Expected answer:

- Answers `SU26`.
- Includes citation from the uploaded PDF.

### 7. Ask Unrelated Question

Narration:

> RAG should avoid hallucination. If the source material does not mention a topic, the assistant should refuse or say it cannot answer from the available documents.

Action:

- Ask: `Does the document mention quantum physics?`

Expected answer:

- Refuses or states the uploaded document does not mention quantum physics.
- Does not invent facts.
- Does not cite unrelated text as proof unless framed as lack of evidence.

### 8. Cleanup

Narration:

> Finally I will delete the demo document. Sprint 2 should either cascade-delete chunks or make them unavailable to retrieval.

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

## Demo Backup Plan

If Groq is unavailable or rate-limited:

- Show successful `/api/rag/search` results as retrieval evidence.
- Show the chat endpoint returns a controlled error or fallback message.
- State clearly that LLM generation requires a valid `Groq:ApiKey` configured in user-secrets and external API availability.

If `/ai/chat` UI is not merged:

- Demo `POST /api/ai/chat/ask` from PowerShell/Postman.
- Show the JSON response has `answer`, `sources`, optional `refusalReason`, and `durationMs`.

If ingestion is not merged:

- Do not claim end-to-end RAG is complete.
- Show baseline document upload only, then state the missing integration blocker: ingestion must create chunks and embeddings after upload or via a manual ingest endpoint.

## Closing Line

> Sprint 2 turns uploaded study files into searchable, cited context for AI answers. The key acceptance evidence is: chunks exist, embeddings are 384-dimensional, retrieval returns owner-only sources, the answer cites those sources, and unrelated questions do not hallucinate.
