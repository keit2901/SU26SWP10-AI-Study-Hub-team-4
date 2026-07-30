# AI/RAG Evaluation and Safety Guide

## 1. Purpose

This file defines how to evaluate AI/RAG features before demo or defense.

Use when the project has:

```text
- Document upload
- Embedding
- Chunking
- Retrieval
- RAG chat
- Quiz generation
- AI model routing
- Benchmarking
```

The goal is not to prove the AI is perfect.  
The goal is to prove that the system behaves reasonably, uses document context correctly, handles failure safely, and can be explained.

## 2. RAG Quality Dimensions

Evaluate these areas:

| Area | Question |
|---|---|
| Retrieval | Are the right chunks/sources retrieved? |
| Grounding | Does the answer use the provided document context? |
| Citation | Are sources shown when required? |
| Refusal / Not Found | Does it admit when answer is not in document? |
| Chunk Quality | Are chunks readable and meaningful? |
| Stability | Does it handle model/service failure? |
| Latency | Is response time acceptable for demo? |
| Regression | Do previous working questions still work? |

## 3. Minimum RAG Test Dataset

Create a small dataset before defense.

| ID | Document | Question | Expected Behavior | Result |
|---|---|---|---|---|
| RAG-Q01 |  | Question directly answered by document | Answer with source | Not Checked |
| RAG-Q02 |  | Question requiring multiple sections | Combine relevant sources | Not Checked |
| RAG-Q03 |  | Question not in document | Say not found / not enough info | Not Checked |
| RAG-Q04 |  | Ambiguous question | Ask clarification or answer carefully | Not Checked |
| RAG-Q05 |  | Vietnamese question if supported | Meaningful answer | Not Checked |

## 4. Chunking Evaluation Checklist

| ID | Check | Expected | Status |
|---|---|---|---|
| RAG-CHUNK-01 | Chunks are not too short/noisy | Meaningful context units | Not Checked |
| RAG-CHUNK-02 | Chunks preserve section meaning | Heading/section relationship kept | Not Checked |
| RAG-CHUNK-03 | Vietnamese sentence boundaries handled if needed | No broken sentences | Not Checked |
| RAG-CHUNK-04 | Re-ingest replaces or updates chunks correctly | No stale duplicate chunks | Not Checked |
| RAG-CHUNK-05 | Failed ingest has clear status/message | Not stuck forever | Not Checked |

## 5. Retrieval Evaluation Checklist

| ID | Check | Expected | Status |
|---|---|---|---|
| RAG-RET-01 | Relevant document selected | Search uses selected scope | Not Checked |
| RAG-RET-02 | Top results are relevant | Correct chunks near top | Not Checked |
| RAG-RET-03 | Irrelevant question handled | Not hallucinated as document fact | Not Checked |
| RAG-RET-04 | Embedding model mismatch prevented if applicable | Same model/filter used | Not Checked |
| RAG-RET-05 | Search mode fallback works if implemented | Hybrid/vector/keyword behaves | Not Checked |

## 6. Answer Quality Checklist

| ID | Check | Expected | Status |
|---|---|---|---|
| RAG-ANS-01 | Answer is grounded in retrieved content | No unsupported claim | Not Checked |
| RAG-ANS-02 | Source citation shown if required | `[S1]`, `[S2]`, etc. | Not Checked |
| RAG-ANS-03 | Not-found answer is honest | Does not invent document content | Not Checked |
| RAG-ANS-04 | Tone is suitable for learning | Clear explanation/example | Not Checked |
| RAG-ANS-05 | Answer length is reasonable | Not too short/too verbose | Not Checked |

## 7. AI Failure Handling

Test these cases:

| ID | Failure | Expected |
|---|---|---|
| RAG-FAIL-01 | Embedding service offline | Upload/ingest fails gracefully or shows clear status |
| RAG-FAIL-02 | LLM API key missing | Clear error or configured local fallback |
| RAG-FAIL-03 | Timeout | User receives understandable error |
| RAG-FAIL-04 | Empty/scanned document | Clear failed/no text status |
| RAG-FAIL-05 | Re-ingest after failure | Can recover to Ready |

## 8. Demo Script for AI/RAG

```text
Demo Name:
Document:
Precondition:
Question 1:
Expected:
Question 2:
Expected:
Question 3 not in document:
Expected:
Evidence:
Fallback if AI service fails:
```

## 9. RAG Evaluation Result Format

```text
Test ID:
Document:
Question:
Retrieved sources:
Answer:
Expected behavior:
Result: PASS / FAIL / PARTIAL
Issue:
Recommended fix:
Evidence:
```

## 10. Final RAG Readiness

```text
READY:
- Main document Q&A works.
- Not-found case is handled.
- At least one failure case is documented.
- Team can explain chunking/retrieval/model flow.

READY WITH NOTES:
- Main flow works, but benchmark/manual quality is limited.
- Known limitation is documented.

NOT READY:
- RAG answers mostly irrelevant.
- AI invents answers from document scope.
- Upload/ingest cannot produce searchable chunks.
- Required service failure has no explanation/fallback.
```
