-- Bật pgvector ngay từ migration đầu để các module sau (smart-search, knowledge-base)
-- dùng cột vector(1536) mà không cần migration riêng cho extension.
CREATE EXTENSION IF NOT EXISTS vector;
