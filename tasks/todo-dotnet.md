# Todo: chuyển backend sang .NET

Kế hoạch đầy đủ ở `tasks/plan-dotnet.md`. Mỗi task phải qua `dotnet build`, `dotnet test` và `pnpm lint` trước khi commit.

## DN-00: Dựng solution .NET và hạ tầng chung

**Mô tả:** Dựng `apps/backend-dotnet` theo cấu trúc trong kế hoạch. Task này chưa port module nào. Bản Node vẫn chạy như cũ.

**Tiêu chí nghiệm thu:**

- [x] `apps/backend-dotnet/Pickory.sln` có đủ project theo mục "Cấu trúc thư mục" của kế hoạch, target `net10.0`
- [x] `Directory.Build.props` bật `Nullable`, `TreatWarningsAsErrors` và `ImplicitUsings` cho mọi project
- [x] `Directory.Packages.props` ghim phiên bản mọi package (central package management)
- [x] `GET /api/health` trả 200 với body `{"data":{"status":"ok"}}`, giống bản Node
- [x] Cấu hình đọc từ biến môi trường cùng tên với bản Node: `PORT`, `WEB_ORIGIN`, `API_BASE_URL`, `DATABASE_URL`, `TRUST_PROXY`. Thiếu `DATABASE_URL` thì app dừng lúc khởi động với thông báo rõ.
- [x] Lỗi nghiệp vụ trả `{"error":{"code","message"}}` với mã HTTP do code chỉ định
- [x] Lỗi validate trả 400, mã `VALIDATION_ERROR`, message theo mẫu `Dữ liệu không hợp lệ (<path>: <message>; ...).` như bản Node
- [x] Lỗi không lường trước trả 500, mã `INTERNAL_ERROR`, message `Đã có lỗi xảy ra, vui lòng thử lại sau.`. Response không lộ stack trace.
- [x] Route không tồn tại trả 404, mã `NOT_FOUND`, message `Không tìm thấy tài nguyên.`
- [x] JSON dùng camelCase. `DateTime` xuất ISO 8601 UTC có đúng 3 chữ số phần giây lẻ và đuôi `Z`, ví dụ `2026-10-05T07:00:00.000Z`.
- [x] Log dạng JSON qua `ILogger`. Không dùng `Console.WriteLine` trong code ứng dụng.
- [x] Dispatcher sự kiện trong process: handler lỗi chỉ ghi log, không làm hỏng luồng phát sự kiện (giống `apps/backend/src/shared/events.ts`)
- [x] `Pickory.IntegrationTests` dùng `WebApplicationFactory` và DB test PostgreSQL riêng, không dùng DB dev
- [x] `Pickory.ArchitectureTests` dùng `NetArchTest.Rules`: project module không tham chiếu thẳng phần nội bộ của module khác. Có test chứng minh một vi phạm mẫu bị bắt.
- [x] Script gốc trong `package.json`: `dotnet:build`, `dotnet:test`, `dotnet:dev`. Các script cũ của bản Node không đổi.
- [x] `.gitignore` bỏ qua `bin/` và `obj/` của .NET
- [x] Mục Tech Stack và Project Structure của `spec/SPEC.md` ghi rõ giai đoạn chuyển đổi: bản .NET ở `apps/backend-dotnet`, bản Node giữ tới DN-08

**Kiểm tra:** `dotnet build` và `dotnet test` xanh; `pnpm lint && pnpm typecheck && pnpm test` của repo vẫn xanh; gọi thử `/api/health` khi chạy `dotnet:dev`.

**Phụ thuộc:** không có · **Quy mô:** M

## DN-01: Model EF Core và migration mốc

**Mô tả:** Tạo model EF Core khớp đúng schema hiện tại của `identity`, `ai-gateway` và `catalog`. Tạo migration EF Core đầu tiên làm mốc. DB không đổi.

**Tiêu chí nghiệm thu:**

- [ ] Mỗi module có `DbContext` riêng trong project của module, chỉ map bảng của module đó
- [ ] Đủ 20 model và 4 enum có trong `apps/backend/prisma/schema/*.prisma`: tên bảng, tên cột, kiểu, độ dài, khóa, unique, index, khóa ngoại và hành vi khi xóa khớp DB thật
- [ ] Enum PostgreSQL (`UserKind`, `EmailTokenPurpose`, `ProductStatus`, `InventoryMovementType`) map qua Npgsql, không lưu thành số
- [ ] Cột JSON map sang `jsonb`. Cột `TIMESTAMP(3)` không múi giờ map đúng kiểu, đọc ra giá trị UTC
- [ ] Hàm đổi `DATABASE_URL` dạng `postgresql://user:pass@host:port/db` sang chuỗi kết nối Npgsql, có unit test kể cả mật khẩu có ký tự đặc biệt
- [ ] Migration mốc của từng `DbContext` có `Up` và `Down` rỗng. Áp lên DB hiện có không đổi schema.
- [ ] Bảng lịch sử migration của EF Core nằm riêng, không đụng `_prisma_migrations`
- [ ] Integration test chạy trên DB test sau khi bản Node đã migrate: so schema EF Core với schema thật (bảng, cột, kiểu, index) và báo khác biệt
- [ ] Integration test đọc và ghi thử một bản ghi mỗi module qua EF Core
- [ ] Không port route hay logic nghiệp vụ nào ở task này

**Kiểm tra:** `dotnet build`, `dotnet test`; chạy migration mốc trên DB dev bản sao rồi so `pg_dump --schema-only` trước và sau, hai bản phải giống nhau.

**Phụ thuộc:** DN-00 · **Quy mô:** M

## DN-02 đến DN-08

Viết tiêu chí chi tiết khi bắt đầu từng task. Danh sách task ở mục "Danh sách task" của `tasks/plan-dotnet.md`.
