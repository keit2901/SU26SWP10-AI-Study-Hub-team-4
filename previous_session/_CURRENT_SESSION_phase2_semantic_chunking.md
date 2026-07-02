# _CURRENT_SESSION - phase2_semantic_chunking

**Started:** 2026-07-02T00:35Z
**Agent:** Codex (GPT-5)
**Goal:** Implement RBL Phase 2 semantic chunking with config-based rollback to fixed-size chunking.
**Status:** IN_PROGRESS

---

## 0. Context loaded
- [x] AGENTS.md (read 2026-07-02T00:35Z)
- [x] previous_session/rule.md (read 2026-07-02T00:35Z)
- [x] docs/RBL_PHASE2_PLAN.md (read 2026-07-02T00:35Z)
- [x] existing RAG pipeline files and tests (read 2026-07-02T00:35Z)

## 1. Verified state at start
- Branch: `feature/Phase_2_Semantic_Chunking`
- Existing implementation used a single fixed-size `ChunkingService`
- `RagOptions` did not yet expose semantic chunking config
- DI registered only one chunking implementation

## 2. Plan
1. Add semantic chunking configuration and rollback path.
2. Implement semantic chunking service plus preserve fixed-size service.
3. Update contracts, controller scoring payload, and tests.
4. Verify as far as current environment allows.

## 3. Progress log (append-only, newest last)

### 2026-07-02T00:35Z - Phase 2 implementation started
- Read `docs/RBL_PHASE2_PLAN.md` and mapped the required changes onto the current RAG pipeline.
- Confirmed the smallest viable integration path is: keep `IChunkingService`, add strategy config, retain fixed-size fallback, and rewrite the main chunker around semantic boundaries.

### 2026-07-02T00:48Z - Semantic chunking integrated
- Added `ChunkingStrategy`, `MinChunkChars`, and `MaxSectionChars` to `RagOptions`.
- Rewrote `Services/Rag/ChunkingService.cs` into a semantic chunker with block parsing, sentence splitting, merge rules, heading preservation, and paragraph/section overlap handling.
- Added `Services/Rag/FixedSizeChunkingService.cs` as rollback implementation.
- Updated DI in `Program.cs` to choose semantic vs fixed chunking via config.
- Extended `DocumentChunkDraft` with optional `SectionTitle`.
- Updated `appsettings.json` and `appsettings.Development.json` to default to `ChunkingStrategy = semantic`.

### 2026-07-02T00:55Z - Contracts and tests updated
- Extended `RagScoringInfoResponse` and `RagController` scoring payload to surface the active chunking strategy plus semantic limits.
- Updated `RagContractTests`, `RagControllerTests`, and rewrote `ChunkingServiceTests` to cover semantic headings, paragraph overlap, list merging, and fixed-size rollback behavior.
- Attempted targeted test execution for `ChunkingServiceTests`, but `dotnet test --no-restore` still failed at NuGet access in this environment (`NU1301`), so compile/runtime verification remains partial.

### 2026-07-02T01:05Z - Semantic chunking refactored into plan-aligned components
- Added `BlockParser`, `SentenceSplitter`, `ChunkMerger`, and shared semantic chunking models under `Services/Rag/`.
- Simplified `ChunkingService` into an orchestrator that composes those three parts.
- Added dedicated unit tests for parser, splitter, and merger behavior so Phase 2 rules can be debugged in isolation.
- Updated ingestion/chunking tests to construct the new semantic pipeline explicitly.

### 2026-07-02T01:20Z - Benchmark and re-ingestion flow completed
- Added `ChunkingBenchmarkService` plus benchmark dataset/models to compare `fixed` vs `semantic` using `recall@k`, `MRR`, chunk count, and small-chunk noise.
- Added `POST /api/benchmark/chunking-compare` to expose the chunking benchmark from the existing benchmark controller.
- Updated admin `reingest-all` to report the active chunking strategy and include both `Ready` and `Failed` documents in the sweep.
- Added unit tests for the chunking benchmark service and admin re-ingestion controller behavior.

### 2026-07-02T01:32Z - Fixed local compile issue from test run log
- Investigated attached `dotnet test --filter Chunking` output from user machine.
- Fixed `ChunkingBenchmarkServiceTests.cs` to use `Microsoft.Extensions.Options.Options.Create(...)` explicitly instead of resolving `Options.Create(...)` against the app `Options` namespace.
- Re-ran the same `dotnet test` command in this environment: it no longer surfaced that compile error, but restore is still blocked here by `NU1301` network access to NuGet.

## 4. Files changed this session
| Path | Change |
|---|---|
| `AI_Study_Hub_v2/Options/RagOptions.cs` | added semantic chunking config |
| `AI_Study_Hub_v2/Services/Rag/ChunkingService.cs` | rewrote fixed-size chunker into semantic chunker |
| `AI_Study_Hub_v2/Services/Rag/BlockParser.cs` | added structural block parsing |
| `AI_Study_Hub_v2/Services/Rag/SentenceSplitter.cs` | added sentence and list splitting |
| `AI_Study_Hub_v2/Services/Rag/ChunkMerger.cs` | added semantic merge and overlap rules |
| `AI_Study_Hub_v2/Services/Rag/SemanticChunkingModels.cs` | added shared semantic chunking records |
| `AI_Study_Hub_v2/Services/Rag/FixedSizeChunkingService.cs` | added rollback implementation |
| `AI_Study_Hub_v2/Services/Rag/Benchmarking/ChunkingBenchmarkModels.cs` | added benchmark result models for fixed vs semantic compare |
| `AI_Study_Hub_v2/Services/Rag/Benchmarking/ChunkingBenchmarkDataset.cs` | added 3-scenario benchmark dataset |
| `AI_Study_Hub_v2/Services/Rag/Benchmarking/ChunkingBenchmarkService.cs` | added chunking benchmark runner |
| `AI_Study_Hub_v2/Services/Rag/RagContracts.cs` | added optional `SectionTitle` on chunk draft |
| `AI_Study_Hub_v2/Program.cs` | config-based chunking DI |
| `AI_Study_Hub_v2/Controllers/BenchmarkController.cs` | added chunking benchmark endpoint |
| `AI_Study_Hub_v2/Controllers/AdminDocumentsController.cs` | re-ingest now returns strategy and retries failed docs too |
| `AI_Study_Hub_v2/Controllers/RagController.cs` | scoring endpoint now exposes chunking strategy info |
| `AI_Study_Hub_v2/Dtos/RagScoringDtos.cs` | added semantic scoring metadata fields |
| `AI_Study_Hub_v2/appsettings.json` | semantic chunking defaults |
| `AI_Study_Hub_v2/appsettings.Development.json` | semantic chunking defaults |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/ChunkingServiceTests.cs` | semantic + fixed rollback tests |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/BlockParserTests.cs` | parser unit tests |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/SentenceSplitterTests.cs` | splitter unit tests |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/ChunkMergerTests.cs` | merger unit tests |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/DocumentIngestionServiceTests.cs` | updated semantic chunking construction |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/ChunkingBenchmarkServiceTests.cs` | benchmark service tests |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Controllers/AdminDocumentsControllerTests.cs` | re-ingest controller tests |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/RagContractTests.cs` | updated defaults expectations |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Controllers/RagControllerTests.cs` | updated scoring expectations |

## 5. Commands run (only commands with side-effect)
- `dotnet test .\AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj --nologo --no-restore --filter ChunkingServiceTests` -> FAIL (`NU1301` access to `api.nuget.org`)
- `dotnet test .\AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj --filter Chunking` -> restore blocked here by `NU1301`, after fixing the previous test compile issue

## 6. Decisions locked
- Keep `IChunkingService` unchanged and switch implementations through config instead of widening the ingestion contract.
- Preserve fixed-size chunking as a first-class rollback path rather than deleting it.

## 7. Open questions / risks
- Environment still blocks NuGet restore, so the semantic chunking changes are code-reviewed but not fully compiled in-session.
- `DocumentChunk` entity does not yet persist `SectionTitle`; Phase 2 plan only required draft-level metadata, so persistence was intentionally deferred.
- The chunking benchmark currently uses a built-in synthetic dataset; benchmarking against manually uploaded PDFs remains a manual verification step.

## 8. Next step (if pause/crash now)
Run a targeted build/test in an environment with NuGet access, starting with `dotnet test .\AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj --filter Chunking`, then call `POST /api/benchmark/chunking-compare` and `POST /api/admin/documents/reingest-all`.

## 9. Quick Facts (snapshot)
Git: `feature/Phase_2_Semantic_Chunking`
Chunking default: `semantic`
Rollback path: `fixed`
Verification: partial only due `NU1301` restore block

### 2026-07-02T10:45Z - Attached failure log resolved
- Investigated attached pasted console log showing 2 failing tests and a failed local app run.
- Fixed `AdminDocumentsControllerTests` to use the shared InMemory test context that ignores `DocumentChunk.Embedding` (`Pgvector.Vector`), matching other tests that cannot map pgvector on EF InMemory.
- Updated `DocumentIngestionServiceTests.IngestAsync_OwnedDocument_StoresChunksAndMarksReady` to assert the semantic chunking behavior now in production: two very small page paragraphs are merged into one chunk.
- Re-ran both targeted failing tests successfully.
- Re-ran the full test suite successfully with local cache: `Passed 232, Skipped 2, Failed 0`.
- App run failure in the pasted log was not a semantic chunking code failure; Kestrel failed to bind local port `5240` with `SocketException (10013)`, so benchmark/reingest API calls could not connect until the app is started cleanly on a free port.

### 2026-07-02T11:25Z - Final semantic cleanup for RBL Phase 2
- Reduced semantic chunk noise by changing heading handling: headings are now preserved as `SectionTitle` metadata for the next semantic chunk instead of being emitted as standalone retrievable chunks.
- Added a guard in `ChunkMerger` to prevent MR1 small-chunk merging across section boundaries.
- Updated semantic chunking tests to match the new expected behavior and added coverage that short paragraphs are not merged across sections.
- Added `docs/RBL_PHASE2_TEST_GUIDE.md` with end-to-end setup, login, benchmark, re-ingest, and acceptance checklist instructions for Phase 2.
- Verification in this Codex environment is partially blocked by two external constraints:
  - the running local app locks default build outputs during compile
  - alternate-output restore still fails here due `NU1301` NuGet network restriction
- The user-side local app/API verification remains the source of truth for final benchmark and re-ingest behavior after restarting the app on the new code.

### 2026-07-02T11:40Z - Final failing test expectation corrected
- User-side full test run surfaced one remaining failure in `ChunkMergerTests`.
- Root cause: the test expected the second section chunk to exclude previous-section text, but the RBL plan explicitly requires section-to-section overlap to carry the last paragraph of the previous section.
- Updated the test to assert the correct behavior: preserve `SECTION 2` as the active section title while prefixing the second chunk with the last paragraph from `SECTION 1`.
