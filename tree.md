# AI Study Hub Project Tree

This file maps the main structure of the repository so the team can quickly find where each feature lives.

## Root

```text
SU26SWP10-AI-Study-Hub-team-4/
|-- AI_Study_Hub_v2/                 Main ASP.NET Core + Blazor app
|-- AI_Study_Hub_Project_Overview.md Project notes
|-- QUICK_START.md                   Local setup guide
|-- README.md                        Repository introduction
|-- infra/                           Deployment and infrastructure files
|-- docker/                          Container-related files
|-- previous_session/                Older working notes/assets
|-- setup.ps1                        Local bootstrap script
`-- tree.md                          This project map
```

## Main App

```text
AI_Study_Hub_v2/
|-- AI_Study_Hub_v2.csproj           Main project file
|-- Program.cs                       App startup, DI, middleware
|-- appsettings*.json                Environment configuration
|-- Components/                      Blazor UI
|-- Controllers/                     API endpoints
|-- Data/                            EF Core context, entities, configuration
|-- Dtos/                            Request/response models
|-- Migrations/                      Database migrations
|-- Options/                         Bound configuration classes
|-- Properties/                      Launch settings
|-- Services/                        Business logic and API clients
|-- wwwroot/                         Static assets and global CSS
`-- AI_Study_Hub_v2.Tests/           Test project
```

## Frontend Structure

```text
AI_Study_Hub_v2/Components/
|-- Admin/                           Admin-only UI modules
|   |-- AuditLogs/
|   |-- Documents/
|   |-- Settings/
|   |-- Shared/
|   |-- Subjects/
|   `-- Users/
|-- Layout/                          Shared layouts and navigation
|   |-- MainLayout.razor             Global shell
|   |-- DashboardLayout.razor        Student dashboard shell
|   `-- NavMenu.razor                Top navigation
|-- Pages/                           User-facing pages
|   |-- Home.razor                   Landing page at `/`
|   |-- Login.razor                  Sign in page
|   |-- Register.razor               Sign up page
|   |-- Profile.razor                User profile page
|   |-- DocumentLibrary.razor        Library page
|   |-- DocumentUpload.razor         Upload page
|   |-- DocumentDetail.razor         Document detail page
|   |-- Community.razor              Community sharing page
|   |-- AiChat.razor                 AI workspace/chat page
|   |-- Dashboard/                   Student dashboard pages
|   |   |-- FolderDashboard.razor    Main student dashboard at `/dashboard`
|   |   |-- DocumentDashboard.razor  Student documents dashboard
|   |   |-- SubjectsDashboard.razor  Student subjects dashboard
|   |   |-- SemestersDashboard.razor Student semesters dashboard
|   |   `-- AnalyticsDashboard.razor Student analytics dashboard
|   `-- Moderator/
|       |-- ModeratorDashboard.razor Frontend moderator workspace
|       `-- ModeratorDashboard.razor.css
`-- Shared/
    `-- Quiz/                        Shared quiz dialog/components
```

## Backend Structure

```text
AI_Study_Hub_v2/Controllers/
|-- AuthController.cs                Authentication endpoints
|-- DocumentsController.cs           Document CRUD and processing endpoints
|-- FoldersController.cs             Folder operations and sharing endpoints
|-- CommunityController.cs           Community/report moderation endpoints
|-- AiChatController.cs              Chat endpoints
|-- RagController.cs                 Retrieval-augmented generation endpoints
|-- QuizController.cs                Quiz generation endpoints
|-- RolesController.cs               Role-related endpoints
`-- BenchmarkController.cs           Benchmark/testing endpoints
```

```text
AI_Study_Hub_v2/Data/
|-- AppDbContext.cs                  EF Core database context
|-- AppDbContextFactory.cs           Design-time factory
|-- Entities/                        Database entities
|   |-- User.cs
|   |-- Role.cs
|   |-- Folder.cs
|   |-- Document.cs
|   |-- CommunityReport.cs
|   `-- ...
`-- Configurations/                  EF Core model configuration and seed data
```

```text
AI_Study_Hub_v2/Services/
|-- AuthSessionState.cs              Frontend auth state
|-- AppRouteResolver.cs              Role-aware dashboard routing
|-- DocumentService.cs               Document business logic
|-- FolderService.cs                 Folder business logic
|-- CommunityService.cs              Community business logic
|-- QuizService.cs                   Quiz generation logic
|-- SemanticKernelRagChatService.cs  RAG chat orchestration
|-- GeminiChatCompletionClient.cs    Gemini integration
|-- GroqChatCompletionClient.cs      Groq integration
|-- SupabaseAuthService.cs           Supabase auth integration
`-- ...                              Supporting API clients, exceptions, interfaces
```

## Quick Find

- Landing page UI: `AI_Study_Hub_v2/Components/Pages/Home.razor`
- Student dashboard UI: `AI_Study_Hub_v2/Components/Pages/Dashboard/FolderDashboard.razor`
- Moderator workspace UI: `AI_Study_Hub_v2/Components/Pages/Moderator/ModeratorDashboard.razor`
- Global layout width rules: `AI_Study_Hub_v2/Components/Layout/MainLayout.razor`
- Dashboard sidebar/footer: `AI_Study_Hub_v2/Components/Layout/DashboardLayout.razor`
- Top navigation: `AI_Study_Hub_v2/Components/Layout/NavMenu.razor`
- Static CSS entry: `AI_Study_Hub_v2/wwwroot/app.css`
