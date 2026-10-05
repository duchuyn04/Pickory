# Todo: chuyển backend sang .NET

Kế hoạch đầy đủ ở `tasks/plan-dotnet.md`. Mỗi task phải qua `dotnet build`, `dotnet test` và `pnpm lint` trước khi commit.

## DN-00: Dựng solution .NET và hạ tầng chung

**Mô tả:** Dựng `apps/backend-dotnet` theo cấu trúc trong kế hoạch. Task này chưa port module nào. Bản Node vẫn chạy như cũ.

**Tiêu chí nghiệm thu:**

- [ ] `apps/backend-dotnet/Pickory.sln` có đủ project theo mục "Cấu trúc thư mục" của kế hoạch, target `net10.0`
- [ ] `Directory.Build.props` bật `Nullable`, `TreatWarningsAsErrors` và `ImplicitUsings` cho mọi project
- [ ] `Directory.Packages.props` ghim phiên bản mọi package (central package management)
- [ ] `GET /api/health` trả 200 với body `{"data":{"status":"ok"}}`, giống bản Node
- [ ] Cấu hình đọc từ biến môi trường cùng tên với bản Node: `PORT`, `WEB_ORIGIN`, `API_BASE_URL`, `DATABASE_URL`, `TRUST_PROXY`. Thiếu `DATABASE_URL` thì app dừng lúc khởi động với thông báo rõ.
- [ ] Lỗi nghiệp vụ trả `{"error":{"code","message"}}` với mã HTTP do code chỉ định
- [ ] Lỗi validate trả 400, mã `VALIDATION_ERROR`, message theo mẫu `Dữ liệu không hợp lệ (<path>: <message>; ...).` như bản Node
- [ ] Lỗi không lường trước trả 500, mã `INTERNAL_ERROR`, message `Đã có lỗi xảy ra, vui lòng thử lại sau.`. Response không lộ stack trace.
- [ ] Route không tồn tại trả 404, mã `NOT_FOUND`, message `Không tìm thấy tài nguyên.`
- [ ] JSON dùng camelCase. `DateTime` xuất ISO 8601 UTC có đúng 3 chữ số phần giây lẻ và đuôi `Z`, ví dụ `2026-10-05T07:00:00.000Z`.
- [ ] Log dạng JSON qua `ILogger`. Không dùng `Console.WriteLine` trong code ứng dụng.
- [ ] Dispatcher sự kiện trong process: handler lỗi chỉ ghi log, không làm hỏng luồng phát sự kiện (giống `apps/backend/src/shared/events.ts`)
- [ ] `Pickory.IntegrationTests` dùng `WebApplicationFactory` và DB test PostgreSQL riêng, không dùng DB dev
- [ ] `Pickory.ArchitectureTests` dùng `NetArchTest.Rules`: project module không tham chiếu thẳng phần nội bộ của module khác. Có test chứng minh một vi phạm mẫu bị bắt.
- [ ] Script gốc trong `package.json`: `dotnet:build`, `dotnet:test`, `dotnet:dev`. Các script cũ của bản Node không đổi.
- [ ] `.gitignore` bỏ qua `bin/` và `obj/` của .NET
- [ ] Mục Tech Stack và Project Structure của `spec/SPEC.md` ghi rõ giai đoạn chuyển đổi: bản .NET ở `apps/backend-dotnet`, bản Node giữ tới DN-08

**Kiểm tra:** `dotnet build` và `dotnet test` xanh; `pnpm lint && pnpm typecheck && pnpm test` của repo vẫn xanh; gọi thử `/api/health` khi chạy `dotnet:dev`.

**Phụ thuộc:** không có · **Quy mô:** M

## DN-01 đến DN-08

Viết tiêu chí chi tiết khi bắt đầu từng task. Danh sách task ở mục "Danh sách task" của `tasks/plan-dotnet.md`.
