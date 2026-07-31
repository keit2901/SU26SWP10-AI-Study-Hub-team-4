# AI Study Hub v2

## Final Release Document

| Field | Value |
|---|---|
| Release | AI Study Hub v2 — Final Release |
| Team | SWP391 Team 4 |
| Release date | 1 August 2026 |
| Application type | ASP.NET Core .NET 8 Blazor Interactive Server application |
| UI framework | MudBlazor 9.4 |
| Data platform | PostgreSQL with pgvector, plus Supabase Auth and Storage |
| Local application URL | <http://localhost:5240> |
| Production/demo URL | <https://su26swp10-ai-study-hub-team-4-production.up.railway.app> |
| Repository | <https://github.com/keit2901/SU26SWP10-AI-Study-Hub-team-4> |

*Hanoi, August 2026*

AI Study Hub v2 provides students with a private academic document library, document-grounded RAG chat with source citations, quiz practice, folder organization, community sharing, per-file moderation, subscription plans, and administrative governance.

> **Release scope note:** This document describes the implementation currently present in this repository. It does not describe untracked or future features. No public demonstration video is tracked in this release package.

## Table of Contents

- [I. Deliverable Package](#i-deliverable-package)
- [II. Installation Guides](#ii-installation-guides)
  - [1. Prerequisites](#1-prerequisites)
  - [2. Configure local Supabase](#2-configure-local-supabase)
  - [3. Configure application secrets](#3-configure-application-secrets)
  - [4. Optional Ollama embeddings](#4-optional-ollama-embeddings)
  - [5. Build and test](#5-build-and-test)
  - [6. Run the application](#6-run-the-application)
  - [7. Stop and clean up](#7-stop-and-clean-up)
- [III. User Manual](#iii-user-manual)
  - [1. Overview](#1-overview)
  - [2. Registration, login, and profile](#2-registration-login-and-profile)
  - [3. Upload, ingestion, and document library](#3-upload-ingestion-and-document-library)
  - [4. RAG chat and source citations](#4-rag-chat-and-source-citations)
  - [5. Quiz generation, resume, and grading](#5-quiz-generation-resume-and-grading)
  - [6. Folder management and community sharing](#6-folder-management-and-community-sharing)
  - [7. Per-file share review](#7-per-file-share-review)
  - [8. Moderator per-file review](#8-moderator-per-file-review)
  - [9. Administrator operations](#9-administrator-operations)
  - [10. Pricing and PayOS payments](#10-pricing-and-payos-payments)
  - [11. Roles, permissions, and status flows](#11-roles-permissions-and-status-flows)
- [IV. Testing and Release Evidence](#iv-testing-and-release-evidence)
- [V. Troubleshooting and Security](#v-troubleshooting-and-security)
- [VI. Release Notes](#vi-release-notes)

## I. Deliverable Package

The following package is included in the repository. Links point to tracked source, configuration, documentation, migration, and test paths.

| No. | Deliverable | Notes |
|---:|---|---|
| 1 | [`AI_Study_Hub_v2/AI_Study_Hub_v2.sln`](AI_Study_Hub_v2/AI_Study_Hub_v2.sln) | Final .NET solution containing the application and test project. |
| 2 | [`AI_Study_Hub_v2/`](AI_Study_Hub_v2/) | ASP.NET Core .NET 8 application source, Blazor pages, services, controllers, data entities, and configuration. |
| 3 | [`AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/`](AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/) | NUnit, FluentAssertions, Moq, EF Core, PostgreSQL, and integration-oriented test sources. |
| 4 | [`AI_Study_Hub_v2/Migrations/`](AI_Study_Hub_v2/Migrations/) | Entity Framework Core database migrations, including [`20260801120000_AddPerFileModeration`](AI_Study_Hub_v2/Migrations/20260801120000_AddPerFileModeration.cs). |
| 5 | [`infra/supabase/`](infra/supabase/) | Local development Supabase Compose stack for PostgreSQL/pgvector, Auth, API gateway, Studio, and related services. |
| 6 | [`infra/ollama/`](infra/ollama/) | Optional Ollama Compose stack for the `all-minilm:l6-v2` embedding model. |
| 7 | [`Project-Docs/`](Project-Docs/) | Project workflow, testing, architecture, governance, and operational documentation. |
| 8 | [`setup tutorial/README.md`](setup%20tutorial/README.md) | Supplementary setup notes. The commands in this release document are the authoritative executable path because `setup.ps1` is not present in this tracked worktree. |
| 9 | [`docs/test-share-moderation/`](docs/test-share-moderation/) | Tracked test documents for share-review and moderation scenarios. |
| 10 | [`LICENSE`](LICENSE) | Project license. |

Other release references:

- **Tagged source code:** No dedicated final-release Git tag is currently tracked. Use the repository history at <https://github.com/keit2901/SU26SWP10-AI-Study-Hub-team-4> until the team creates one.
- **Production/demo:** <https://su26swp10-ai-study-hub-team-4-production.up.railway.app>.
- **Demonstration video:** No public demonstration video is tracked in this repository.

## II. Installation Guides

### 1. Prerequisites

Install the following before starting local development:

| Tool | Requirement | Check |
|---|---|---|
| .NET SDK | .NET 8 SDK | `dotnet --version` |
| Docker Desktop | Running for the local Supabase stack | `docker info` |
| PowerShell | Windows PowerShell 5.1 or PowerShell 7+ | `$PSVersionTable.PSVersion` |
| Git | Required to clone the repository | `git --version` |

The default local stack uses host ports `5432` for PostgreSQL, `8000` for Supabase/Kong/Studio, and `5240` for the application. The optional Ollama stack uses port `11434`.

Run all commands below from the repository root.

### 2. Configure local Supabase

The local Compose stack is for development. Do not deploy its local `.env` file to production.

1. Create the ignored local environment file from the tracked template:

   ```powershell
   Copy-Item .\infra\supabase\.env.example .\infra\supabase\.env
   ```

2. Fill the local Supabase values required by the template. Keep PostgreSQL credentials, JWT values, anonymous keys, service-role keys, and dashboard credentials private.

3. Start and inspect the stack:

   ```powershell
   docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase pull
   docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase up -d
   docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase ps
   ```

4. Optional health checks:

   ```powershell
   curl.exe http://localhost:8000/auth/v1/health
   docker exec -it supabase-db psql -U postgres -c "CREATE EXTENSION IF NOT EXISTS vector; SELECT '[1,2,3]'::vector;"
   ```

The application uses the local values shown in the tracked Supabase reference: PostgreSQL at `localhost:5432`, Supabase URL `http://localhost:8000`, JWT issuer `http://localhost:8000/auth/v1`, and JWT audience `authenticated`.

### 3. Configure application secrets

The application reads local secret values from .NET User Secrets or another secure environment-specific provider. Set values locally; do not place credentials in `appsettings.json`, `appsettings.Development.json`, this document, or Git.

Required or feature-specific configuration categories are:

| Category | Keys or purpose |
|---|---|
| Database | `ConnectionStrings:Postgres` |
| Supabase | `Supabase:JwtSecret`, `Supabase:AnonKey`, `Supabase:ServiceRoleKey` |
| Seed accounts | Passwords for `Seed:DefaultAdmin`, `Seed:DefaultModerator`, and `Seed:DefaultProStudent` when those local accounts are seeded |
| AI provider | `Groq:ApiKey` or `Gemini:ApiKey`, depending on the configured completion provider |
| PayOS | `PayOs:ClientId`, `PayOs:ApiKey`, `PayOs:ChecksumKey` when payment testing is enabled |
| reCAPTCHA | `Recaptcha:SiteKey` and `Recaptcha:SecretKey` when enabled outside the supported development configuration |

Use User Secrets with values supplied privately in your own shell or password manager. For example:

```powershell
dotnet user-secrets set "ConnectionStrings:Postgres" $env:LOCAL_POSTGRES_CONNECTION --project .\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
dotnet user-secrets set "Supabase:JwtSecret" $env:LOCAL_SUPABASE_JWT_SECRET --project .\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
dotnet user-secrets set "Supabase:AnonKey" $env:LOCAL_SUPABASE_ANON_KEY --project .\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
dotnet user-secrets set "Supabase:ServiceRoleKey" $env:LOCAL_SUPABASE_SERVICE_ROLE_KEY --project .\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
dotnet user-secrets set "Groq:ApiKey" $env:LOCAL_GROQ_API_KEY --project .\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
```

Do not print the values of these environment variables or paste the output of `dotnet user-secrets list` into a public issue or chat. The local Supabase registration policy is also documented in [`infra/supabase/README.md`](infra/supabase/README.md).

### 4. Optional Ollama embeddings

The repository includes an optional Ollama service for local embedding and RAG evaluation. Start Supabase first so the shared Docker network exists, then run:

```powershell
docker compose -f infra\ollama\docker-compose.yml --project-directory infra\ollama up -d
curl.exe -s http://localhost:11434/api/tags
```

The Compose file pulls `all-minilm:l6-v2`. Ollama is not required for every unit test, but RAG ingestion and embedding integration scenarios require a running, configured provider. Do not describe this local embedding path as equivalent to a trained semantic-quality evaluation without running the relevant benchmarks.

### 5. Build and test

Build the solution:

```powershell
dotnet build .\AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
```

Run the default test suite:

```powershell
dotnet test .\AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
```

PostgreSQL tests require a disposable test database whose name ends in `_test` and the `AI_STUDY_HUB_TEST_POSTGRES` environment variable. Do not point these tests at a production database.

```powershell
$env:AI_STUDY_HUB_TEST_POSTGRES = $env:LOCAL_TEST_POSTGRES_CONNECTION
dotnet test .\AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj --nologo --filter TestCategory=Postgres
```

### 6. Run the application

From the repository root, start the application on the documented local URL:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj --no-launch-profile --urls http://localhost:5240
```

Open <http://localhost:5240/login>. The application startup applies pending Entity Framework migrations and then performs its configured seed/reconciliation work. The per-file moderation release migration is `20260801120000_AddPerFileModeration`.

### 7. Stop and clean up

Stop the application with `Ctrl+C` in the terminal running `dotnet run`.

Stop Supabase while keeping its data:

```powershell
docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase stop
```

Remove containers but keep volumes:

```powershell
docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase down
```

Destroy local database and storage volumes only when you intentionally want to lose local data:

```powershell
docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase down -v
```

Stop the optional Ollama service:

```powershell
docker compose -f infra\ollama\docker-compose.yml --project-directory infra\ollama down
```

## III. User Manual

### 1. Overview

AI Study Hub v2 helps students collect course materials, process supported files into searchable chunks, ask document-grounded questions, generate practice quizzes, and share selected folders with the community. The application uses Blazor Interactive Server with MudBlazor. Uploaded files are stored through Supabase Storage; application metadata, document chunks, moderation state, chat history, quizzes, plans, and audit records are persisted in PostgreSQL. pgvector supports vector retrieval, while the RAG pipeline can combine vector and keyword search and apply re-ranking according to configuration.

The normal student journey is:

1. Create an account or sign in.
2. Upload supported course files and wait for ingestion.
3. Use the document library to organize files into folders.
4. Ask questions in `/ai/chat` and inspect citations.
5. Generate and complete a multiple-choice quiz.
6. Submit a folder for community sharing when appropriate.
7. Review file-level decisions and keep rejected or unresolved files private.

### 2. Registration, login, and profile

**Purpose:** Create and maintain an authenticated student account.

**Role:** Guest for registration/login; authenticated users for profile management.

**Routes:** `/register`, `/login`, `/profile`.

**Steps:**

1. Open `/register` when self-registration is enabled by the application policy.
2. Enter the requested account details and complete the visible validation requirements.
3. After successful registration, use `/login` to authenticate.
4. The application redirects an authenticated user to the appropriate landing area for the account role.
5. Open `/profile` to view or update the student profile information available to the account.
6. Sign out from the authenticated navigation when the session is no longer needed.

Registration availability is policy-controlled; a deployment can disable public self-registration. Administrators should verify the configured policy before treating `/register` as available in a demo environment.

### 3. Upload, ingestion, and document library

**Purpose:** Store academic materials and turn extractable content into searchable chunks.

**Role:** Authenticated student or other account permitted to manage documents.

**Routes:** `/documents/upload`, `/documents`, `/documents/{id}`, and the folder dashboard under `/dashboard`.

**Supported files and limits:** The upload UI accepts PDF, DOC, DOCX, PPT, and PPTX extensions; the primary document model and ingestion documentation cover PDF, DOCX, and PPTX. The seeded Free plan has a 50 MB per-file limit. Higher plans can have different configured limits. Empty, unsupported, unreadable, or non-extractable content can fail ingestion.

**Steps:**

1. Open `/documents/upload`.
2. Select one or more supported files and provide the required subject and semester metadata.
3. Submit the upload. The file is stored in Supabase Storage and the document record enters the upload/processing lifecycle.
4. Open `/documents` to monitor the result. `Ready` means the document completed the available ingestion path; `Failed` means the library retains an error state that needs attention or re-ingestion.
5. Open a ready document to inspect its details, download/view it where permitted, move it between folders, or remove it according to the account permissions.
6. Use the folder dashboard to create, rename, favorite, and organize folders. Folder and document quotas are plan-dependent.

The upload page and server enforce file-size and MIME/extension validation. A successful upload response does not replace checking the library: ingestion may finish later or may result in `Failed` when extraction or embedding cannot complete.

### 4. RAG chat and source citations

**Purpose:** Ask questions against selected documents or folders and receive an answer accompanied by retrieved source information.

**Role:** Authenticated user with access to the selected document scope.

**Route:** `/ai/chat`.

**Steps:**

1. Open `/ai/chat` and choose the document or folder scope for the conversation.
2. Enter a question that can be answered from the selected study material.
3. Submit the question and wait for the assistant response.
4. Review the answer together with its source entries. Source data can include the file name, page number, chunk/page metadata, and a text excerpt.
5. Use the citation to cross-check the answer in the original document. Copy an answer when needed, or continue the same session for follow-up questions.
6. If the question is outside the selected material, treat a “not found in the provided documents” style response as an expected boundary rather than evidence that the system knows the answer from elsewhere.

The current pipeline is configured for semantic chunking, hybrid vector/keyword retrieval, optional embedding caching, and re-ranking. Retrieval and generated-answer quality depends on document text extraction, embedding/provider availability, configuration, and source content; citations improve traceability but do not guarantee factual correctness.

### 5. Quiz generation, resume, and grading

**Purpose:** Generate exam-style multiple-choice practice from the selected study scope.

**Role:** Authenticated student or other user with access to the source documents.

**Route:** Quiz tools are available in the `/ai/chat` workspace and document-library workflow.

**Steps:**

1. Select at least one document or folder in the AI workspace.
2. Choose optional subject or semester filters when a narrower quiz scope is required.
3. Select **Generate Quiz**. The application creates multiple-choice questions with A–D options and answer keys through the configured AI service.
4. Open the generated quiz and answer questions in the quiz dialog.
5. If you leave an in-progress quiz, use the active-quiz banner or the **Resume Quiz** action to continue.
6. Submit the quiz to save the result. The completion view shows the score, total questions, and accuracy information.
7. If generation fails, inspect the displayed error, verify the selected source scope and provider configuration, and retry when appropriate.

Quiz state is persisted with chat/workspace context. A failed generation is distinct from a completed quiz and should not be reported as a graded result.

### 6. Folder management and community sharing

**Purpose:** Organize private study files and publish eligible material for other users.

**Role:** Folder owner for management; authenticated community users for discovery and interaction.

**Routes:** `/dashboard`, `/documents`, and `/community`.

**Private folder steps:**

1. Open `/dashboard` or `/documents`.
2. Create or select a folder, then add or move documents into it.
3. Rename, favorite, or delete folders using the available owner actions.
4. Keep a folder private until its contents are ready for sharing.

**Community steps:**

1. From the owner dashboard/library, submit an eligible folder for sharing.
2. Open `/community` to browse folders that are currently public.
3. Open a public folder to inspect its approved public files.
4. Signed-in users can vote, report a folder, or copy the public folder into their own library when the action is available.
5. A copied folder contains the approved public files exposed by the source folder; private, pending, rejected, or escalated files are not made public through the community view.

**Visibility constraint:** A folder becomes publicly useful only when its share state and file-level outcomes permit it. Public queries require an approved folder with at least one file that is both `Ready` and per-file `Approved`. An already-public folder can keep approved files visible while newly added files remain private until review completes.

### 7. Per-file share review

**Purpose:** Let the folder owner make an informed decision about each file before or after moderation feedback.

**Role:** Folder owner/student.

**Route:** `/share-review/{FolderId}`.

**Steps:**

1. Open the share-review page for the folder from the library or sharing flow.
2. Review each file's current visibility and moderation feedback.
3. For rejected files, choose **Resubmit for review** after correcting the content, **Delete rejected file**, or **Keep folder private** when the material should not be published.
4. For an approved folder, open the community view only after confirming that the intended files are marked public.
5. Escalated files remain locked for owner publication changes until an administrator resolves the escalation.

The release implements file-level decisions rather than treating the folder as one indivisible moderation item. A rejected file can remain private while other approved, ready files remain eligible for public display.

### 8. Moderator per-file review

**Purpose:** Review submitted files for community publication and provide a reasoned outcome.

**Role:** Moderator.

**Routes:** `/dashboard/share-reviews` for the queue, and `/share-review/{FolderId}` in moderator mode.

**Steps:**

1. Open the moderator share-review queue.
2. Select a pending folder and inspect its individual files.
3. Open a file when more context is needed.
4. Choose **Approve** for material that can be published, **Reject** and provide a reason for material that cannot be published, or **Escalate** when administrator review is required.
5. Confirm that the resulting file statuses and the folder's derived publication state match the intended outcome.

Only actionable files are shown for direct moderation: the implemented service requires a `Ready` document with no prior file-level review outcome. Escalated files are locked until an administrator resolves them.

### 9. Administrator operations

**Purpose:** Govern accounts, content, moderation, configuration, payments, and auditability.

**Role:** Admin.

**Routes:**

| Area | Route |
|---|---|
| Admin dashboard | `/admin` or `/admin/dashboard` |
| Users and user detail | `/admin/users`, `/admin/users/{id}` |
| Documents and document detail | `/admin/documents`, `/admin/documents/{id}` |
| Escalations and per-file resolution | `/admin/escalations` |
| Community reports | `/admin/community-reports` |
| System settings | `/admin/settings` |
| Audit logs | `/admin/audit-logs` |
| RAG/benchmark history | `/admin/benchmarks` |

**Steps:**

1. Open the admin dashboard after signing in with an Admin account.
2. Use **Users** to search accounts, inspect user details, update permitted roles or quotas, and activate/deactivate accounts subject to the implemented safeguards.
3. Use **Documents** to inspect document metadata and perform administrative document actions.
4. Use **Escalations** to inspect escalated files and resolve each item as **Approved** or **Rejected**, recording the administrator response where requested.
5. Use **Community Reports** to review reports submitted against public community folders.
6. Use **Settings** to manage system-controlled configuration exposed by the application, including registration and document-related settings where enabled.
7. Use **Audit Logs** to review security-sensitive and administrative changes.
8. Use **Benchmarks** to inspect persisted RAG/chunking benchmark results when benchmark data exists.

The per-file release migration stores moderation generation, escalation-item resolution state, administrator response, resolver identity/time, and related notification data so that administrator decisions remain distinguishable from earlier folder-level outcomes.

### 10. Pricing and PayOS payments

**Purpose:** Review plan limits and purchase a paid plan through PayOS.

**Role:** Authenticated student for purchase; Admin for plan/payment administration.

**Routes:** `/pricing` for plan selection and `/payment/result` for the browser return/result view. Admin plan/payment operations are available from the administrative area and corresponding API services.

**Steps:**

1. Open `/pricing` and compare the available Free, Pro, and Unlimited plan limits and prices shown by the application.
2. Select a paid plan and start checkout.
3. Complete or cancel the PayOS checkout flow.
4. Return to `/payment/result` and verify the displayed transaction result.
5. If a payment is pending, failed, expired, or cancelled, use the available status or retry action rather than assuming that the plan changed.
6. Confirm the resulting plan limits in the profile/library experience.

The seeded plan data currently defines a Free plan with a 50 MB per-file limit, a Pro plan with a 100 MB per-file limit, and an Unlimited plan without a configured per-file limit. Actual availability and pricing remain deployment configuration and provider dependent. PayOS credentials are never included in this document.

### 11. Roles, permissions, and status flows

#### Roles and permissions

| Role | Main permissions |
|---|---|
| Guest | View public entry pages; register or log in when registration policy allows. |
| Student | Manage own profile, documents, folders, chat sessions, quizzes, and eligible sharing submissions; browse community content; vote, report, and copy public folders when signed in. |
| Moderator | Review eligible per-file community submissions; approve, reject with a reason, or escalate files to Admin. |
| Admin | Manage users and permitted role/quota state, documents, reports, settings, audit logs, benchmarks, and escalated per-file moderation decisions. |

#### Document and sharing status notes

- **Document processing:** Uploaded files can pass through uploading/processing before becoming `Ready` or `Failed`.
- **`Ready`:** The document completed the available ingestion path and can be considered for downstream search or sharing eligibility.
- **`Failed`:** Ingestion or extraction failed. Check the document error information and retry through the supported ingestion path when available.
- **File review:** `None`, `Approved`, `Rejected`, and `Escalated` are distinct per-file moderation outcomes.
- **Folder sharing:** A folder can be private, pending share, approved/public, or rejected. The folder state is derived from its file outcomes and explicit sharing lifecycle.
- **Public visibility:** Public community queries expose only approved folders and files that are both `Ready` and per-file `Approved`.
- **Escalation:** An escalated file is private/locked for the relevant owner workflow until Admin resolves it as approved or rejected.

## IV. Testing and Release Evidence

### 1. Reproducible commands

```powershell
dotnet build .\AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
dotnet test .\AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
```

For PostgreSQL integration coverage, configure `AI_STUDY_HUB_TEST_POSTGRES` with a disposable database ending in `_test`, then run the PostgreSQL category as described in [Installation Guides](#5-build-and-test).

### 2. Verified release evidence

The per-file release handoff records the following evidence:

- Default test suite: **672/672 passed**.
- PostgreSQL integration category: **60/60 passed** when run against the configured test database.
- Six ignored tests are not represented as executed in this release statement.

These figures are release evidence from the current handoff, not a promise that every optional provider, browser, or deployment-specific smoke path runs without its required environment.

## V. Troubleshooting and Security

### Docker or Supabase does not start

- Confirm Docker Desktop is running with `docker info`.
- Inspect service state with `docker compose ... ps` and logs with `docker compose ... logs <service>`.
- Check that ports `5432` and `8000` are free.
- Confirm `infra/supabase/.env` exists locally and contains valid, private values derived from `.env.example`.

### Application cannot connect to PostgreSQL or Supabase

- Confirm the Compose stack is healthy.
- Check that `ConnectionStrings:Postgres` points to the local PostgreSQL service.
- Check the Supabase URL, issuer, audience, JWT secret, anonymous key, and service-role key categories without printing their values.
- Restart the application after changing User Secrets.

### Upload remains `Failed`

- Confirm the file is supported and within the current plan's per-file limit.
- Confirm the document contains extractable text. A scanned image-only PDF may fail without an OCR path.
- If embeddings are required, confirm the configured AI/Ollama provider is reachable.
- Check the document's error message and application logs, then retry ingestion where the UI/API provides that action.

### RAG answers are weak or lack useful citations

- Select the correct document or folder scope.
- Confirm the source document is `Ready` and contains extractable text.
- Check embedding provider availability and model configuration.
- Review the configured chunking, hybrid-search, re-ranking, and context limits.
- Use the benchmark pages and documented test guides for measurement; do not infer semantic quality from a single answer.

### Payment result is not final

- Treat a pending, expired, cancelled, or failed result as non-final.
- Re-open the payment status/result view or use the supported retry flow.
- Confirm PayOS configuration is present in the deployment environment and that callback/return URLs match the deployed application.
- Never place PayOS credentials in source, logs, screenshots, or support messages.

### Security rules

- Never commit `infra/supabase/.env`, local database/storage volumes, User Secrets, API keys, JWT secrets, service-role keys, PayOS credentials, or passwords.
- Do not paste `dotnet user-secrets list` output into public channels because it can include secret values.
- Keep service-role and payment credentials server-side only.
- Use a disposable database ending in `_test` for PostgreSQL tests; never run destructive test setup against production.
- Treat uploaded documents and extracted text as user data. Do not share a private folder or file merely because it exists in the local library.
- Review public visibility and per-file moderation state before announcing a community link.

## VI. Release Notes

This final release document reflects the repository's implemented release surface, including:

- .NET 8 Blazor Interactive Server UI with MudBlazor.
- PostgreSQL/pgvector persistence with Supabase Auth and Storage integration.
- PDF/DOCX/PPTX-oriented ingestion and document library workflows, with upload validation for the supported document extensions in the UI.
- RAG chat with persisted sessions and source/citation data.
- Quiz generation, in-progress resume, completion, and score display.
- Folder management and community sharing with voting, reporting, and approved-folder copying.
- File-level student/owner review actions, Moderator approve/reject/escalate decisions, and Admin per-file escalation resolution.
- Admin users, documents, reports, settings, audit logs, and benchmark areas.
- Plan limits and PayOS payment-result handling.
- Migration [`20260801120000_AddPerFileModeration`](AI_Study_Hub_v2/Migrations/20260801120000_AddPerFileModeration.cs), with production startup migration application implemented in `Program.cs`.

For operational details beyond this release document, start with [`Project-Docs/`](Project-Docs/) and the supplementary [`setup tutorial/README.md`](setup%20tutorial/README.md).
