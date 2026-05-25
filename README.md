# AI Study Hub

**A centralized academic document management platform integrated with a smart RAG-powered AI Chatbot**, designed specifically for FPT University students to eliminate AI hallucinations and provide accurate source citations.

---

## ✨ Features

- **Academic Knowledge Base Lifecycle**: Upload and organize study materials (PDF, DOCX, PPTX) by subject code and semester. Documents are automatically chunked, embedded, and saved securely on Cloud Storage.
- **Contextual AI Assistant (RAG Workflow)**:Ask questions and get answers based strictly on the content of the uploaded documents, preventing the AI from fabricating information (anti-hallucination).
- **Accurate Source Citation**: Every AI-generated academic answer includes verified source citations (file name, page number, and text snippet) so students can cross-check information directly.
- **Admin Governance**: Administrators can monitor API token usage, manage user accounts, and directly configure AI model parameters such as System Prompt, Model type, and Temperature.
- **Hybrid Search Strategy**: Combines traditional keyword search with advanced semantic vector search (Cosine Similarity) to ensure no academic information is missed.

## 🛠️ Tech Stack

**This system is built with a Cloud-native architecture utilizing the following core technologies:**

- **Frontend**: Blazor WebAssembly (Full-stack C# to reduce context switching.) + Figma
- **Backend**: .NET 8 Web API
- **AI Engine**: Microsoft Semantic Kernel + LLM Services (GPT / Gemini)
- **Database**: PostgreSQL with pgvector extension for vector storage.
- **Cloud Storage**: Azure Blob Storage

## 📸 Screenshots / Demo(on-going)

## 📋 System Architecture

The system follows a clean layered architecture:
```text
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
 ```                     
**Core Pipeline**:  
**Data Ingestion** → **Chunking & Embedding** → **Indexing** → **Hybrid Retrieval** → **Augmentation** → **Generation with Citation**

## 🗄️ Database Design

The storage system uses **Hybrid Database**:

- **Relational Database (PostgreSQL)**: Stores user profiles, document metadata (Subject Code, Semester), and AI chat history.
- **Vector Database (pgvector)**: Stores chunked text converted into numerical vectors to enable rapid semantic similarity searches (Cosine distance).

*(Entity-Relationship Diagram (ERD) will be updated later)*

## 🚀 Installation

*(Detailed installation instructions are being finalized - Building is On-going)*

## 💻 Usage

### For Students/Users:
Log in to your account, create a workspace, and upload academic documents (PDF, DOCX, PPTX). Wait for the system to process the document (Status: Indexed), then navigate to the Chatbot interface to ask questions based on the selected document's context.

### Đối với Admin:
Access the Admin Dashboard to manage users, monitor API/Token consumption, review uploaded public documents, and dynamically configure AI settings like System Prompts and Model parameters.

## 📡 API Documentation

*(The project is still in the process of completion, so the API documentation content is still being updated.)*

Swagger UI is available at:

## 🧪 Testing & Research

This project focuses heavily on **Experimental Research for the RAG pipeline**:
- Measuring retrieval performance using **Hit Rate** and **MRR** (Mean Reciprocal Rank) metrics.
- Conducting comparative studies on chunking techniques (Recursive vs. Semantic Chunking) and optimizing overlap ratios to find the "sweet spot" that maintains contextual integrity and improves AI source citation accuracy.

## 🤝 Contributing

Contributions are welcome! The features planned for development after the MVP are:- AI Auto-summarization
- Quiz generation from documents
- Community document sharing
- OCR support for scanned documents.

You can open an Issue or a Pull Request.

## 👥 Contributors

- **[Ngô Đình Tuấn Kiệt]** - Main Developer

*(Update the list of contributors when a new member joins.)*

## 📄 License

---

