# AI Study Hub v2 — Complete System Workflow

> Generated from codebase analysis 2026-06-30

---

## 1. Authentication Flow

```mermaid
flowchart TD
    A[User visits Login/Register page] --> B{Has account?}
    B -->|No| C[Register]
    C --> C1[POST /api/auth/register]
    C1 --> C2[GoTrue SignUp]
    C2 --> C3[Create User row in DB]
    C3 --> C4[Return JWT + refresh token]
    
    B -->|Yes| D[Login]
    D --> D1[POST /api/auth/login]
    D1 --> D2[GoTrue SignInWithPassword]
    D2 --> D3[Validate password]
    D3 -->|Fail| D4[401 Unauthorized]
    D3 -->|OK| D5[Query User by SupabaseUserId]
    D5 --> D6{IsActive?}
    D6 -->|No| D7[403 Account locked]
    D6 -->|Yes| D8[Load Role from DB]
    D8 --> D9[Return JWT + UserDto with Role]
    
    C4 --> E[AuthSessionState stores token + user info]
    D9 --> E
    
    E --> F{Access protected page?}
    F -->|Admin page| G[Authorize Roles=Admin]
    F -->|Moderator page| H[Authorize Roles=Admin,Moderator]
    F -->|User page| I[Authorize]
    
    G --> G1{Has Admin role?}
    G1 -->|Yes| G2[Grant access]
    G1 -->|No| G3[403 Forbidden]
    
    H --> H1{Has Admin or Moderator?}
    H1 -->|Yes| H2[Grant access]
    H1 -->|No| H3[403 Forbidden]
    
    E --> J[Logout]
    J --> J1[Clear AuthSessionState]
    J --> J2[GoTrue SignOut]
    J2 --> J3[Redirect /login]
```

---

## 2. Document Upload & Ingestion Flow (RAG Pipeline)

```mermaid
flowchart TD
    A[User uploads document] --> B{File type valid?}
    B -->|No| B1[400 Invalid MIME type]
    B -->|Yes| C{File size < 50MB?}
    C -->|No| C1[400 File too large]
    C -->|Yes| D{Document limit reached?}
    D -->|Yes| D1[400 Max docs per folder]
    D -->|No| E[Upload to Supabase Storage]
    
    E --> F[Create Document row: Status=Uploading]
    F --> G[Return DocumentDto to UI]
    
    G --> H[Background: DocumentIngestionService]
    H --> I{File type?}
    I -->|PDF| I1[PdfPig extract text + images]
    I -->|DOCX| I2[OpenXML extract text]
    I -->|PPTX| I3[OpenXML extract slides]
    
    I1 --> J{Has images?}
    I2 --> K
    I3 --> K
    J -->|Yes| J1[GroqVisionDescriptionService: describe images]
    J -->|No| K[ChunkingService: split into 1000-char chunks, 200 overlap]
    J1 --> K
    
    K --> L[FakeEmbeddingService: FNV-1a hash → 384-dim vector]
    L --> M[Insert DocumentChunk rows with embeddings]
    M --> N[Update Document status = Ready]
    
    N --> O{All chunks indexed?}
    O -->|Yes| O1[Status = Ready / Indexed]
    O -->|Error| O2[Status = Failed + ErrorMessage]
```

---

## 3. AI Chat & RAG Query Flow

```mermaid
flowchart TD
    A[User enters question in AiChat page] --> B[ChatPersistenceService: save user message]
    B --> C[RagSearchService: pgvector cosine search]
    C --> D[topK=5 chunks from vector DB]
    
    D --> E[Build system prompt with citations]
    E --> F[Format: [S1]chunk1 [S2]chunk2 ...]
    F --> G[Add chat history to context]
    
    G --> H{ChatCompletionClientFactory}
    H --> H1{AiModel selection}
    H1 -->|gemini-*| H2[GeminiChatCompletionClient → Gemini 2.5 Flash]
    H1 -->|default| H3[GroqChatCompletionClient → Llama 3.3 70B]
    
    H2 --> I[Stream response to UI]
    H3 --> I
    
    I --> J[ChatPersistenceService: save assistant message]
    J --> K[Update AI quota for user]
    K --> L[Return complete chat exchange]
```

---

## 4. Quiz Generation Flow

```mermaid
flowchart TD
    A[User requests quiz] --> B[Select parameters: subject, difficulty, count]
    B --> C[QuizService: build prompt for LLM]
    C --> D[LLM generates MCQ JSON]
    
    D --> E{Parse successful?}
    E -->|No| E1[Retry up to 2 times]
    E1 -->|Still fail| E2[Status = GeneratingFailed]
    E1 -->|Success| F[Parse JSON questions + answers]
    E -->|Yes| F
    
    F --> G[Save Quiz + QuizQuestions to DB as JSONB]
    G --> H[Status = Ready]
    H --> I[QuizDialog renders questions in UI]
    I --> J[User submits answers]
    J --> K[Score calculation]
    K --> L[Show results]
```

---

## 5. Admin — Role & Permission Flow

```mermaid
flowchart TD
    A[Admin visits /admin/users] --> B[AdminApiClient.ListUsersAsync]
    B --> C[AdminUsersController.List]
    C --> D[AdminUserService.ListAsync: query all users + roles + doc counts]
    D --> E[Render Users.razor table]
    
    E --> F[Click ⋮ → Change role]
    F --> G[EntitySheet: show assignable roles]
    G --> G1{User is self?}
    G1 -->|Yes| G2[No Change role option]
    G1 -->|No| G3{User is Admin?}
    G3 -->|Yes| G4[No Change role option]
    G3 -->|No| H[Show Moderator/Student dropdown]
    
    H --> I[Select role → SaveRoleAsync]
    I --> J[AdminApiClient.UpdateRoleAsync]
    J --> K[AdminUsersController.PATCH /api/admin/users/{id}/role]
    K --> L[AdminUserService.UpdateRoleAsync]
    
    L --> M{Validate permissions}
    M -->|Own role| M1[400 cannot_change_own_role]
    M -->|Admin role| M2[403 cannot_change_admin_role]
    M -->|Not admin| M3[403 admin_required]
    M -->|OK| N[Update RoleId in DB]
    
    N --> O[GoTrue: AdminUpdateUserById app_metadata.role]
    O --> P[GoTrue: AdminSignOutUser force logout]
    P --> Q[AuditLog: ROLE_CHANGE with before/after]
    Q --> R[Return updated AdminUserDto]
    R --> S[Snackbar success + refresh UI]
```

---

## 6. Admin — User Lock/Unlock Flow

```mermaid
flowchart TD
    A[Admin visits /admin/users] --> B[Click Active/Inactive chip]
    
    B --> C{Currently Active?}
    C -->|Yes| D[ToggleActiveAsync: deactivate]
    C -->|No| E[ToggleActiveAsync: activate directly]
    
    D --> D1{Is current admin?}
    D1 -->|Yes| D2[Snackbar: cannot deactivate self]
    D1 -->|No| D3[Show confirm dialog]
    D3 --> D4[User confirms]
    D4 --> F[AdminApiClient.ToggleActiveAsync activate=false]
    
    E --> F
    
    F --> G[AdminUsersController.PATCH /api/admin/users/{id}/deactivate]
    G --> H[AdminUserService.ToggleActiveAsync]
    
    H --> I{Is self?}
    I -->|Yes| I1[400 cannot_toggle_self]
    I -->|No| J{Already correct state?}
    J -->|Yes| J1[Return unchanged]
    J -->|No| K[Set IsActive = false/true]
    
    K --> L{Deactivating?}
    L -->|Yes| L1[GoTrue: AdminUpdateUserById banned=true]
    L1 --> L2[GoTrue: AdminSignOutUser force logout]
    L -->|No| L3[GoTrue: AdminUpdateUserById banned=false]
    
    L2 --> M[AuditLog: USER_LOCK with before/after]
    L3 --> M[AuditLog: USER_UNLOCK with before/after]
    M --> N[Return updated AdminUserDto]
    N --> O[Snackbar success]
```

---

## 7. Content Moderation Flow

```mermaid
flowchart TD
    A[Moderator/Admin visits /admin/documents/moderation] --> B[DocumentModerationService.GetQueueAsync]
    B --> C[Query: all docs where Status != Ready]
    C --> D[Include User info + first chunk preview]
    D --> E[Render DocumentModeration.razor table]
    
    E --> F[Click ⋮ on document]
    F --> G{Action choice}
    
    G -->|Approve| H[ModerateAsync ApprovedStatus]
    H --> H1[ApproveAsync: Status = Ready, ErrorMessage = null]
    H1 --> H2[AuditLog: MODERATION_APPROVE]
    H2 --> H3[Remove from queue + snackbar success]
    
    G -->|Reject| I[ModerateAsync RejectedStatus]
    I --> I1[ModerationDecisionDialog: enter reason]
    I1 --> I2[RejectAsync: Status = Failed, ErrorMessage = reason]
    I2 --> I3[AuditLog: MODERATION_REJECT]
    I3 --> I4[Update row to Rejected]
    
    G -->|Remove| J[ModerateAsync RemovedStatus]
    J --> J1[ModerationDecisionDialog: enter reason]
    J1 --> J2[DeleteAsync: delete from Supabase Storage + DB]
    J2 --> J3[AuditLog: MODERATION_DELETE]
    J3 --> J4[Remove from queue + snackbar]
    
    G -->|Escalate| K[ModerateAsync EscalatedStatus]
    K --> K1[EscalateAsync: ErrorMessage = Escalated to admin]
    K1 --> K2[AuditLog: MODERATION_ESCALATE]
    K2 --> K3[Update row to Escalated + snackbar warning]
    
    G -->|Restore| L[ModerateAsync RestoredStatus]
    L --> L1[RestoreAsync: Status = Processing, ErrorMessage = null]
    L1 --> L2[AuditLog: MODERATION_RESTORE]
    L2 --> L3[Update row to Pending + snackbar success]
```

---

## 8. Community Reports Flow

```mermaid
flowchart TD
    A[User sees shared folder in Public Hub] --> B[Click Report Folder]
    B --> C[ReportFolderDialog: select reason]
    C --> D[POST /api/community/reports]
    D --> E[CommunityService: Create CommunityReport row]
    E --> E1{Duplicate Pending?}
    E1 -->|Yes| E2[409 Already reported]
    E1 -->|No| F[Status = Pending]
    
    F --> G[Admin/Moderator visits /admin/community-reports]
    G --> H[CommunityApiClient.GetPendingReportsAsync]
    H --> I[Render Reports.razor table]
    
    I --> J[Click Resolve]
    J --> K[ResolveReportDialog: select resolution + notes]
    K --> L{Decision?}
    L -->|Resolve| M[Status = Resolved + notes]
    L -->|Dismiss| N[Status = Dismissed + notes]
    
    M --> O[Update CommunityReport in DB]
    N --> O
    O --> P[Remove from pending queue]
```

---

## 9. Admin Dashboard Flow

```mermaid
flowchart TD
    A[Admin visits /admin/dashboard] --> B[AdminDashboardApiClient.GetAdminStatsAsync]
    B --> C[DashboardController GET /api/dashboard/admin/stats]
    C --> D[DashboardService.GetAdminStatsAsync]
    D --> D1[SELECT COUNT(*) FROM users]
    D --> D2[SELECT COUNT(*) FROM documents]
    D --> D3[SELECT COUNT(*) WHERE status=uploading/processing]
    D --> D4[SELECT COUNT(*) WHERE status=failed]
    D --> E[Return AdminDashboardStatsDto]
    
    E --> F[Render KPI cards: Users, Docs, Jobs, Failed, Storage]
    
    F --> G[LoadTrendsAsync period=day]
    G --> H[DashboardController GET /api/dashboard/admin/activity-trends?period=day]
    H --> I[DashboardService.GetActivityTrendsAsync]
    I --> I1[Query docs by date buckets last 7 days]
    I --> I2[Query failed docs by date buckets]
    I --> J[Return ActivityTrendsDto with Points]
    J --> K[Render MudChart Line chart]
    
    K --> L{User clicks Week/Month chip?}
    L -->|Yes| G
    L -->|No| M[Done]
```

---

## 10. Audit Log Flow

```mermaid
flowchart TD
    A[Any admin action triggers audit] --> B[AuditLogService.Add]
    B --> C[Record: ActorUserId, Action, EntityType, EntityId, Severity, BeforeJson, AfterJson, IpAddress]
    C --> D[Save to audit_logs table]
    
    E[Admin visits /admin/audit-logs] --> F[AdminApiClient.ListAuditLogsAsync]
    F --> G[AuditLogsController GET /api/admin/audit-logs]
    G --> H[AuditLogService.ListAsync: filter by action/from/to/limit]
    H --> I[Render AuditLogs.razor table]
    
    I --> J{Apply filters?}
    J -->|Action| J1[Filter by action type]
    J -->|Actor| J2[Filter by actor]
    J -->|Severity| J3[Filter by severity]
    J -->|Date| J4[Filter by date range]
    J -->|Search| J5[Full-text search]
    
    J1 --> K[Show filtered rows]
    J2 --> K
    J3 --> K
    J4 --> K
    J5 --> K
    
    K --> L[Expand row → view Before/After JSON]
    
    I --> M[Click Export → GET /api/admin/audit-logs/export]
    M --> N[Download CSV file]
```

---

## 11. Database Seed Flow (Startup)

```mermaid
flowchart TD
    A[App starts] --> B[EF Core: apply pending migrations]
    B --> C{Admin exists?}
    C -->|Yes| C1[Skip admin seed]
    C -->|No| C2[GoTrue: AdminCreateUser with app_metadata.role=Admin]
    C2 --> C3[Create User row with RoleId=1 Admin]
    
    C3 --> D{Moderator configured?}
    C1 --> D
    D -->|No| D1[Skip moderator seed]
    D -->|Yes| D2[GoTrue: AdminCreateUser with app_metadata.role=Moderator]
    D2 --> D3[Create User row with RoleId=3 Moderator]
    
    D3 --> E[Done: app ready]
    D1 --> E
```

---

## 12. Complete System Overview

```mermaid
flowchart LR
    subgraph Auth
        A1[Register] --> A2[Login] --> A3[JWT Session]
    end
    
    subgraph User Features
        B1[Upload Document] --> B2[RAG Ingestion]
        B3[AI Chat] --> B4[RAG Search + LLM]
        B5[Quiz] --> B6[LLM Generate MCQ]
        B7[Public Hub] --> B8[Community Reports]
    end
    
    subgraph Admin Features
        C1[Dashboard] --> C2[Real-time Stats]
        C3[User Management] --> C4[Role + Lock/Unlock]
        C5[Moderation] --> C6[Approve/Reject/Delete]
        C7[Audit Logs] --> C8[Track + Export]
    end
    
    subgraph Infrastructure
        D1[(PostgreSQL + pgvector)]
        D2[Supabase Auth + Storage]
        D3[Groq + Gemini LLM]
        D4[Docker Supabase Stack]
    end
    
    A3 --> B1
    A3 --> B3
    A3 --> B5
    A3 --> B7
    A3 --> C1
    A3 --> C3
    A3 --> C5
    A3 --> C7
    
    B1 --> D2
    B2 --> D1
    B4 --> D1
    B4 --> D3
    B6 --> D3
    
    C2 --> D1
    C3 --> D1
    C3 --> D2
    C5 --> D1
    C5 --> D2
    C8 --> D1
    
    D1 --- D4
    D2 --- D4
```

---

## Actions Catalog

| Action | Source | Entity | Severity |
|--------|--------|--------|----------|
| `QUOTA_UPDATE` | AdminUserService | users | Medium |
| `ROLE_CHANGE` | AdminUserService | users | High |
| `USER_LOCK` | AdminUserService | users | High |
| `USER_UNLOCK` | AdminUserService | users | High |
| `MODERATION_APPROVE` | DocumentModerationController | documents | Medium |
| `MODERATION_REJECT` | DocumentModerationController | documents | Medium |
| `MODERATION_DELETE` | DocumentModerationController | documents | High |
| `MODERATION_ESCALATE` | DocumentModerationController | documents | High |
| `MODERATION_RESTORE` | DocumentModerationController | documents | Medium |
| `FORCE_LOGOUT_FAILED` | AdminUserService | users | Medium |

---

## Role Hierarchy

```
Admin ──┬── Can manage users (role/lock/quota)
        ├── Can moderate content (approve/reject/delete)
        ├── Can view audit logs
        ├── Can access dashboard
        └── Cannot assign/revoke Admin role
        
Moderator ── Can review community reports
            └── Can moderate documents (escalate to Admin)
            
Student ── Default role
          └── Can upload docs, chat with AI, take quizzes, report folders
```
