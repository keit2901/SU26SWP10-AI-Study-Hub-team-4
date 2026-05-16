AI Study Hub
A centralized academic document management platform integrated with a smart RAG-powered AI Chatbot, designed specifically for FPT University students to eliminate AI hallucinations and provide accurate source citations
.
    
✨ Features
Academic Knowledge Base Lifecycle: Upload and organize study materials (PDF, DOCX, PPTX) by subject code and semester
. Documents are automatically chunked, embedded, and saved securely on Cloud Storage
.
Contextual AI Assistant (RAG Workflow): Ask questions and get answers based strictly on the content of the uploaded documents, preventing the AI from fabricating information (anti-hallucination)
.
Accurate Source Citation: Every AI-generated academic answer includes verified source citations (file name, page number, and text snippet) so students can cross-check information directly
.
Admin Governance: Administrators can monitor API token usage, manage user accounts, and directly configure AI model parameters such as System Prompt, Model type, and Temperature
.
Hybrid Search Strategy: Combines traditional keyword search with advanced semantic vector search (Cosine Similarity) to ensure no academic information is missed
.
🛠️ Tech Stack
This system is built with a Cloud-native architecture utilizing the following core technologies
:
Frontend: Blazor WebAssembly (Full-stack C# to reduce context switching)
.
Backend: .NET 8 Web API
.
AI Engine: Microsoft Semantic Kernel integrated with LLM Services (GPT / Gemini)
.
Database: PostgreSQL integrated with the pgvector extension for vector storage
.
Cloud Storage: Azure Blob Storage
.
📸 Screenshots / Demo
(Add your dashboard and chatbot interface screenshots here)
 
📋 System Architecture
The AI Study Hub operates on a layered architecture connecting the Frontend, Backend, and Cloud AI services
:
[Guest/Student] --------> (Frontend: Blazor WASM)
                               │
                               ▼
                   (Backend: .NET 8 Web API)
                               │
          ┌────────────────────┼──────────────────────┐
          ▼                    ▼                      ▼
┌─────────────────┐   ┌─────────────────┐    ┌─────────────────┐
│  Cloud Storage  │   │  Relational DB  │    │    AI Engine    │
│ (Azure Blob DB) │   │  (PostgreSQL)   │    │(Semantic Kernel)│
└─────────────────┘   └─────────────────┘    └─────────────────┘
                               │                      │
                               ▼                      ▼
                      ┌─────────────────┐    ┌─────────────────┐
                      │   Vector Store  │    │   LLM Service   │
                      │    (pgvector)   │    │  (GPT / Gemini) │
                      └─────────────────┘    └─────────────────┘
Core Pipeline: Data Ingestion (Upload → Pending) → Knowledge Digitization (Chunking & Embedding → Indexed) → Retrieval & Augmentation (Querying → Context Matching) → Generation & Verification (Generated → Cited)
.
🗄️ Database Design
The storage system uses a hybrid model:
Relational Database (PostgreSQL): Stores user profiles, document metadata (Subject Code, Semester), and AI chat history
.
Vector Database (pgvector): Stores chunked text converted into numerical vectors to enable rapid semantic similarity searches (Cosine distance)
.
(Please insert your Entity-Relationship Diagram (ERD) and Database Structure image here)
🚀 Installation
Lưu ý: Phần này không có thông tin lệnh cài đặt cụ thể trong tài liệu gốc, tôi sử dụng mẫu tiêu chuẩn dành cho ứng dụng .NET/Blazor:
Clone the repository:
Configure environment variables in appsettings.json (PostgreSQL connection string, Azure Blob Storage key, OpenAI/Gemini API Key).
Restore .NET dependencies:
Run database migrations:
Start the Backend API and Blazor WebAssembly Client:
💻 Usage
For Students: Log in to your account, create a workspace, and upload academic documents (PDF, DOCX, PPTX)
. Wait for the system to process the document (Status: Indexed), then navigate to the Chatbot interface to ask questions based on the selected document's context
.
For Admins: Access the Admin Dashboard to manage users, monitor API/Token consumption, review uploaded public documents, and dynamically configure AI settings like System Prompts and Model parameters
.
📡 API Documentation
(If the project uses Swagger/OpenAPI, provide the link or description here)
Swagger UI is available at: http://localhost:<port>/swagger
🧪 Testing (Research Phase)
This project focuses heavily on Experimental Research for the RAG pipeline
:
Measuring retrieval performance using Hit Rate and MRR (Mean Reciprocal Rank) metrics
.
Conducting comparative studies on chunking techniques (Recursive vs. Semantic Chunking) and optimizing overlap ratios to find the "sweet spot" that maintains contextual integrity and improves AI source citation accuracy
.
🤝 Contributing
Contributions are welcome! If you'd like to implement future post-MVP features such as AI auto-summarization, quiz generation from documents, community sharing features, or OCR support for complex documents
, please open an Issue or submit a Pull Request.
👥 Contributors:
📄 License:
