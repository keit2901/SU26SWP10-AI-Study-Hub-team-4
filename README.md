# AI Study Hub

**A centralized academic document management platform integrated with a smart RAG-powered AI Chatbot**, designed specifically for FPT University students to eliminate AI hallucinations and provide accurate source citations.

---

## ✨ Features

- **Academic Knowledge Base Lifecycle**: Upload and organize study materials (PDF, DOCX, PPTX) by subject code and semester. Documents are automatically chunked, embedded, and saved securely on Cloud Storage.
- **Contextual AI Assistant (RAG Workflow)**: Ask questions and get answers based strictly on the content of the uploaded documents, preventing the AI from fabricating information.
- **Accurate Source Citation**: Every AI-generated answer includes verified source citations (file name, page number, and text snippet) for easy cross-checking.
- **Admin Governance**: Administrators can monitor API token usage, manage user accounts, and configure AI model parameters (System Prompt, Model type, Temperature).
- **Hybrid Search Strategy**: Combines traditional keyword search with semantic vector search (Cosine Similarity) to ensure comprehensive retrieval of academic information.

## 🛠️ Tech Stack

- **Frontend**: Blazor WebAssembly (Full-stack C#)
- **Backend**: .NET 8 Web API
- **AI Engine**: Microsoft Semantic Kernel + LLM Services (GPT / Gemini)
- **Database**: PostgreSQL with pgvector extension
- **Cloud Storage**: Azure Blob Storage

## 📸 Screenshots / Demo(on-going)

## 📋 System Architecture

The system follows a clean layered architecture:

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
                      

