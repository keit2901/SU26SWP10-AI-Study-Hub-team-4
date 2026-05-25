# AI Study Hub - Project Overview

**Last Updated**: May 23, 2026  
**University**: FPT University - SWP391 (SU26)  
**Project Type**: Academic Document Management + RAG-based AI Learning Assistant

---

## 1. Project Overview

**AI Study Hub** is a web application that allows students to:
- Upload academic documents (PDF, DOCX, PPTX)
- Interact with documents through an AI chatbot using **Retrieval-Augmented Generation (RAG)**
- Manage personal documents and workspaces
- Generate quizzes and take mock tests from their documents
- Share documents in a Public Hub (with moderation)

The system helps turn static study materials into interactive knowledge that students can ask questions about, review, and test themselves on.

---

## 2. Technology Stack

| Layer          | Technology                              |
|----------------|-----------------------------------------|
| Frontend       | Blazor 8 (Components-based)            |
| UI Library     | MudBlazor                              |
| Backend        | ASP.NET Core 8 Web API                 |
| Database       | Supabase (PostgreSQL + pgvector)       |
| Authentication | Supabase Auth + JWT                    |
| Storage        | Supabase Storage                       |
| AI             | Groq API (Llama 3.1) + Embeddings      |
| Vector Search  | pgvector                               |

---

## 3. Current Project Status

### ✅ Completed

- **Database Design**
  - Full schema created (10+ tables)
  - Tables: `profiles`, `subjects`, `documents`, `document_chunks`, `chat_sessions`, `chat_messages`, `audit_logs`, `system_configs`, `rag_experiments`, `message_feedback`
  - Vector support enabled (`pgvector`)
  - Seed data script created and tested

- **Project Setup**
  - Blazor 8 project created (`AI_Study_Hub_Admin`)
  - MudBlazor integrated
  - Supabase client configured

- **Admin Module** (Recently Updated)
  - Dashboard with KPIs and charts
  - User Management (search, filter, activate/deactivate, role change, token quota)
  - Document Moderation (Approve/Reject/Remove with reason)
  - System Settings (AI parameters, chunking config)
  - Audit Logs (filterable + expandable JSON)
  - Reusable components (AdminDataGrid, FilterBar, EntitySheet, ConfirmDialog)

### 🚧 In Progress

- **Backend Authentication**
  - Register / Login / Logout
  - JWT + Supabase Auth integration
  - Role-based authorization (Student / Admin)

### 📌 Next Priorities

1. Complete Authentication module (Register, Login, Logout, Role protection)
2. Implement Document Upload + Processing pipeline (Chunking + Embedding)
3. Build RAG Chat functionality (UC06)
4. Add Admin route protection
5. Connect frontend with backend APIs

---

## 4. Key Design Decisions

- Using **Supabase** as the main backend (Database + Auth + Storage)
- Following **Clean Architecture** principles
- Using **MudBlazor** for consistent and professional UI
- Admin Module follows **Components-based structure** (not legacy Pages/Shared)
- All admin actions are logged in `audit_logs` table
- Document processing uses background jobs (status: Pending → Processing → Indexed/Failed)

---

## 5. Important Files & Locations

| File | Purpose | Location |
|------|---------|----------|
| `claude.md` | Rules & guidelines for Claude (Codex) | `/artifacts/claude.md` |
| `Admin_Module_Prompts.md` | Collection of prompts for Admin Module | `/artifacts/Admin_Module_Prompts.md` |
| `AI_Study_Hub_Project_Overview.md` | This file (Project summary) | `/artifacts/AI_Study_Hub_Project_Overview.md` |
| Database Schema | Full SQL schema | Created earlier |
| Seed Data | Sample data script | Created earlier |

---

## 6. Current Focus (May 23, 2026)

**Primary Goal**: Complete the **Authentication Module** (Register - Login - Logout) with proper role-based access control.

**Next Steps**:
- Build backend authentication APIs
- Protect Admin routes
- Connect Blazor frontend with authentication
- Implement document upload + processing flow

---

## 7. Notes for Future Sessions

- Always follow the rules in `claude.md`
- Use the **new Blazor Components structure**
- Prioritize clean code and security
- Keep Admin Module consistent with MudBlazor design system
- Document important decisions in this file

---

**This file serves as a quick reference when starting a new session.**