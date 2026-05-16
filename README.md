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

Hệ thống sử dụng mô hình **Hybrid Database**:

- **Relational Database (PostgreSQL)**: Quản lý user profiles, document metadata (Subject Code, Semester), chat history.
- **Vector Database (pgvector)**: Lưu trữ vector embeddings của các document chunks để hỗ trợ semantic search.

*(Entity-Relationship Diagram (ERD) sẽ được cập nhật sớm nhất)*

## 🚀 Installation

*(Hướng dẫn cài đặt chi tiết đang được hoàn thiện - On-going)*

## 💻 Usage

### Đối với Sinh viên:
1. Đăng nhập vào tài khoản.
2. Tạo Workspace theo môn học.
3. Upload tài liệu (PDF, DOCX, PPTX).
4. Chờ tài liệu được xử lý (trạng thái **Indexed**).
5. Sử dụng AI Chatbot để hỏi đáp dựa trên tài liệu đã upload.

### Đối với Admin:
- Quản lý người dùng
- Giám sát token/API usage
- Review tài liệu public
- Điều chỉnh System Prompt, Model, Temperature

## 📡 API Documentation

Swagger UI có sẵn tại:  
`http://localhost:<port>/swagger`

## 🧪 Testing & Research

Dự án tập trung mạnh vào nghiên cứu thực nghiệm cho pipeline RAG:
- Đánh giá hiệu suất retrieval qua **Hit Rate** và **MRR** (Mean Reciprocal Rank).
- So sánh các kỹ thuật chunking (Recursive vs Semantic Chunking) và tối ưu overlap ratio.

## 🤝 Contributing

Contributions are welcome! Các tính năng đang dự định phát triển sau MVP:
- AI Auto-summarization
- Quiz generation từ tài liệu
- Community document sharing
- OCR hỗ trợ tài liệu scan

Bạn có thể mở Issue hoặc Pull Request.

## 👥 Contributors

- **[Tên của bạn]** - Main Developer

*(Cập nhật danh sách contributors khi có thành viên tham gia)*

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

---

**Bạn chỉ cần copy toàn bộ nội dung trên** và dán vào file `README.md` là có thể dùng ngay.

Bạn muốn mình chỉnh thêm gì không?
- Thêm phần **Prerequisites**?
- Làm ASCII diagram đẹp hơn?
- Thêm badges (GitHub, license, .NET, etc.)?
- Viết phần Installation chi tiết hơn?

Cứ nói mình chỉnh tiếp nhé!
