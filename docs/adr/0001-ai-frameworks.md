# ADR 0001: Dùng Vercel AI SDK và một phần LangChain.js cho các tính năng AI

- Trạng thái: Đã chấp nhận
- Ngày: 2026-10-03
- Liên quan: `spec/SPEC-ai-gateway.md`, `spec/SPEC-knowledge-base.md`, `spec/SPEC-shopping-assistant.md`

## Bối cảnh

Pickory gọi AI qua API bên thứ ba: DeepSeek cho chat và sinh nội dung, OpenAI cho embedding. Các spec đã duyệt yêu cầu:
- Mọi lời gọi AI đi qua `ai-gateway` để kiểm soát ngân sách, chặn dữ liệu cá nhân, ghi log chi phí, có circuit breaker.
- Chatbot theo kiểu RAG có tool calling nhiều bước và stream.
- Kho tri thức nhập được PDF/DOCX và chia đoạn.
- Truy xuất hybrid (vector + toàn văn + RRF) và lọc quyền ngay trong truy vấn PostgreSQL.

Người làm dự án muốn **giảm code tự viết** và **học framework AI phổ biến**.

## Các phương án đã xem xét

1. **Tự viết toàn bộ** trên SDK `openai`. Ít phụ thuộc nhất, nhưng phải tự viết stream, vòng lặp tool, structured output, chia đoạn.
2. **LangChain.js + LangGraph.js làm khung chính.** Hệ sinh thái lớn, nhưng vector store và retriever có sẵn khó đáp ứng yêu cầu truy xuất hybrid và lọc quyền ở DB. API thay đổi khá thường xuyên. Nhiều lớp trừu tượng làm khó kiểm soát việc mọi lời gọi đều đi qua `ai-gateway`.
3. **LlamaIndex.TS cho RAG.** Mạnh về RAG, nhưng chồng chéo với phần truy xuất tự viết bằng SQL.
4. **Vercel AI SDK trong `ai-gateway`, LangChain.js chỉ cho bước nhập và chia đoạn tài liệu.** *(chọn)*

## Quyết định

- **Vercel AI SDK** (`ai`, `@ai-sdk/deepseek`, `@ai-sdk/openai`) là lớp provider bên trong `ai-gateway/providers/`. Dùng cho: chat, stream, tool calling nhiều bước, structured output theo Zod, embedding.
- **LangChain.js** chỉ dùng trong `knowledge-base/ingest/`:
  - `RecursiveCharacterTextSplitter` (`@langchain/textsplitters`) để cắt đoạn dài.
  - PDF loader (`@langchain/community`) để trích văn bản PDF.
  - DOCX dùng `mammoth` chuyển sang HTML rồi sang Markdown, vì DocxLoader của LangChain chỉ lấy văn bản thô và làm mất heading.
- **Vẫn tự viết:** truy xuất hybrid trong PostgreSQL, kiểm tra grounding, ngân sách, `redactPII`, circuit breaker, chia đoạn theo heading.
- Module tính năng khai báo tool bằng kiểu `ToolDefinition` của Pickory, không dùng kiểu của framework. Đổi hoặc bỏ framework về sau chỉ ảnh hưởng `ai-gateway/providers/` và `knowledge-base/ingest/`.

## Hệ quả

**Tích cực**
- Bỏ được phần lớn code tự viết cho stream, vòng lặp tool, parse JSON, cắt đoạn.
- AI SDK hỗ trợ sẵn DeepSeek và OpenAI, đổi provider dễ.
- Có cơ hội học 2 framework phổ biến mà vẫn giữ kiến trúc đã duyệt.

**Tiêu cực, rủi ro**
- Thêm phụ thuộc bên ngoài, phải theo dõi khi nâng phiên bản lớn. Giảm thiểu: khóa phiên bản trong lockfile, framework chỉ nằm trong 2 thư mục, ESLint chặn import ở nơi khác.
- Retry có sẵn của AI SDK bị tắt để tránh retry 2 tầng. Retry chỉ nằm ở gateway.
- Mức hỗ trợ tool calling và JSON mode của model DeepSeek đã chọn qua AI SDK cần xác minh bằng contract test (`AI_LIVE_TESTS=1`) trước khi làm `shopping-assistant`.

**Quy tắc đi kèm**
- Không bật LangSmith tracing, không gửi trace hay prompt sang dịch vụ quan sát bên ngoài.
- Thêm package AI mới ngoài danh sách trên vẫn thuộc mục "Ask first" trong `spec/SPEC.md`.
