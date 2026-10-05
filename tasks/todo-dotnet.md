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

**Mô tả:** Tạo model EF Core (Entity Framework Core) khớp schema của `identity`, `ai-gateway` và `catalog`. Tạo migration mốc cho DB trống. Giữ nguyên DB hiện có bằng lệnh ghi nhận mốc.

**Tiêu chí nghiệm thu:**

- [x] Mỗi module có `DbContext` riêng trong project của module, chỉ map bảng của module đó
- [x] Đủ 20 model và bốn enum theo `apps/backend-dotnet/db/reference-schema.sql`.
  - Tên bảng và tên cột khớp DB thật.
  - Kiểu và độ dài khớp DB thật.
  - Khóa, unique và index khớp DB thật.
  - Khóa ngoại và hành vi khi xóa khớp DB thật.
- [x] Enum PostgreSQL (`UserKind`, `EmailTokenPurpose`, `ProductStatus`, `InventoryMovementType`) map qua Npgsql, không lưu thành số
- [x] Cột JSON map sang `jsonb`. Cột `TIMESTAMP(3)` không múi giờ map đúng kiểu, đọc ra giá trị UTC
- [x] Hàm đổi `DATABASE_URL` dạng `postgresql://user:pass@host:port/db` sang chuỗi kết nối Npgsql, có unit test kể cả mật khẩu có ký tự đặc biệt
- [x] Migration mốc tạo đủ schema trên DB trống theo `apps/backend-dotnet/db/reference-schema.sql`.
  - Bảng và enum khớp schema gốc.
  - Index và khóa ngoại khớp schema gốc.
  - Extension `vector` tồn tại trong `public`.
- [x] DB đã có schema dùng lệnh ghi nhận migration mốc. Lệnh không tạo lại bảng ứng dụng.
- [x] Bảng lịch sử migration của EF Core nằm riêng, không đụng `_prisma_migrations`
- [x] DB test của integration test tự dựng schema bằng migration EF Core, không cần bản Node.
- [x] Test so schema migration với `reference-schema.sql`. Test bỏ qua `_prisma_migrations`. Test báo mọi khác biệt.
- [x] Integration test đọc và ghi thử một bản ghi mỗi module qua EF Core
- [x] Không port route hay logic nghiệp vụ nào ở task này

**Kiểm tra:** `dotnet build`, `dotnet test`; chạy migration trên một DB trống rồi so `pg_dump --schema-only` với `reference-schema.sql`.

**Ghi chú 2026-10-05:** người dùng quyết định xóa bản Node trước khi port. Schema gốc nằm ở `reference-schema.sql`. Code Node xem bằng tag `node-backend-final`.

**Ghi nhận schema hiện có:**

Không dùng migration `Down` để hoàn tác ghi nhận mốc. Migration `Down` xóa bảng ứng dụng.

1. So schema DB đích với `apps/backend-dotnet/db/reference-schema.sql`.
   Lệnh ghi nhận chỉ kiểm tra tên bảng, enum và extension.
   Lệnh không kiểm tra mọi cột hoặc index.
2. Đặt `DATABASE_URL` tới DB đã có schema đúng.
3. Chạy `psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f apps/backend-dotnet/db/adopt-existing-schema.sql` trong Bash.
   Dùng `$env:DATABASE_URL` thay cho `"$DATABASE_URL"` khi chạy PowerShell.
4. Kiểm tra ba bảng lịch sử trong `ef_identity`, `ef_ai_gateway` và `ef_catalog`.
   Mỗi bảng có một bản ghi `PrismaBaseline`.

**Tạo schema trên DB trống:**

1. Chạy `dotnet tool restore` tại gốc repo.
   Manifest `.config/dotnet-tools.json` ghim `dotnet-ef` 10.0.12 để công cụ khớp phiên bản EF Core.
2. Đặt `DATABASE_URL` tới DB trống.
3. Chạy `dotnet ef database update --project apps/backend-dotnet/src/Pickory.Modules.Identity --context IdentityDbContext`.
4. Chạy `dotnet ef database update --project apps/backend-dotnet/src/Pickory.Modules.AiGateway --context AiGatewayDbContext`.
5. Chạy `dotnet ef database update --project apps/backend-dotnet/src/Pickory.Modules.Catalog --context CatalogDbContext`.

**Bằng chứng DN-01 (2026-10-05):**

- `pnpm dotnet:build`: thành công, không có warning hoặc error.
- `pnpm dotnet:test --no-build`: 63 test thành công, không có test bị bỏ qua.
- Phân nhóm .NET: 33 unit test, 27 integration test và ba architecture test.
- Fixture reset DB test dưới khóa phiên PostgreSQL. Fixture dùng migration EF Core để dựng schema.
- Test schema đối chiếu 20 bảng và bốn enum với `reference-schema.sql`.
- Test schema kiểm tra thứ tự cột, giá trị mặc định và kiểu cột.
- Test schema kiểm tra index, ràng buộc và hành vi khóa ngoại.
- Test schema kiểm tra extension `vector` cùng mô tả metadata.
- Test đọc/ghi kiểm tra toàn bộ bảng của ba module. Giao dịch rollback giữ dữ liệu thử khỏi các test khác.
- Migration dùng `AlterDatabase` cho enum và extension. Npgsql tự nạp lại cache kiểu sau migration.
- Lệnh EF Core tạo schema trên DB trống khớp dump của bản tham chiếu đã phục hồi.
- Phép so bỏ `_prisma_migrations` và ba schema lịch sử EF Core.
- Phép so chỉ chuẩn hóa xuống dòng cùng khóa ngẫu nhiên của `\restrict` và `\unrestrict`.
- Mã SHA-256 (Secure Hash Algorithm, 256 bit) của dump: `ef11b74b39d5a0a0441103b58d5f35182c5572b090ea2cc6c1e8057b4fea55e3`.
- Lệnh `Down` thật xóa 20 bảng, bốn enum và `vector` trên DB thử.
- Lệnh `Up` sau `Down` khôi phục dump đúng bản tham chiếu.
- Script ghi nhận chạy hai lần trên bản sao DB dev. Dump schema cùng dữ liệu không đổi ngoài lịch sử EF Core.
- Lệnh EF Core sau ghi nhận báo không có migration cần áp dụng cho cả ba module.
- Test ghi nhận từ chối thiếu bảng, sai enum hoặc thiếu `vector`.
- Test khóa phiên từ chối tiến trình cạnh tranh trước bước reset DB.
- `pnpm lint` và `pnpm typecheck`: thành công.
- `pnpm test`: 95 test thành công, 11 tệp thành công, không có test bị bỏ qua.
- Ba DB thử thuộc DN-01 đã được xóa sau phép so. DB dev không bị thay đổi.

**Phụ thuộc:** DN-00 · **Quy mô:** M

## DN-01R: Chuẩn hóa cấu trúc thư mục backend

**Mô tả:** Sắp xếp lại thư mục trước khi port route ở DN-02. Mục tiêu là fresher và junior tìm được code theo tên tính năng hoặc tên bảng. Task này không đổi hành vi, không đổi schema DB.

**Tiêu chí nghiệm thu:**

- [x] `tasks/plan-dotnet.md` có mục "Quy ước thư mục trong module" với cây thư mục mẫu.
  - `<Module>Module.cs` ở gốc project module là điểm đăng ký duy nhất của module.
  - `Features/<TínhNăng>/` chứa endpoint, request, validator và handler của một tính năng.
  - `Entities/` chứa entity và enum.
  - `Persistence/` chứa `DbContext`, factory thiết kế, `Configurations/` và `Migrations/`.
  - Code dùng chung trong module đặt ở thư mục theo tên khái niệm, ví dụ `Sessions/`. Không tạo thư mục `Helpers/`, `Utils/`, `Common/` hoặc `Misc/`.
- [x] Mỗi entity và mỗi enum nằm trong một file riêng dưới `Entities/`. Tên file trùng tên kiểu. Namespace là `Pickory.Modules.<Module>.Entities`.
- [x] Không còn file gộp `IdentityEntities.cs`, `IdentityEnums.cs`, `AiEntities.cs`, `CatalogEntities.cs` và `CatalogEnums.cs`.
- [x] Cấu hình mỗi bảng nằm trong `Persistence/Configurations/<Entity>Configuration.cs`, dùng `IEntityTypeConfiguration<T>`.
- [x] `DbContext` gọi `ApplyConfigurationsFromAssembly` với assembly của chính module. `DbContext` chỉ còn `DbSet`, `ConfigureOptions`, quy ước kiểu và khai báo enum PostgreSQL.
- [x] Model EF Core không đổi. `dotnet ef migrations has-pending-model-changes` báo không có thay đổi cho cả ba `DbContext`. Không có migration mới.
- [x] Tên migration `PrismaBaseline`, mã migration và nội dung `Up`, `Down` giữ nguyên.
- [x] `PrismaModelConventions` đổi thành `DatabaseNamingConventions`. Ngoài thư mục `Migrations/`, không định danh nào trong `src/` chứa chữ `Prisma`.
- [x] Mỗi `<Module>Module.cs` là lớp `static` có hai hàm:
  - `Add<Module>Module(IServiceCollection services, string databaseConnectionString)` đăng ký `DbContext` và dịch vụ của module.
  - `Map<Module>Endpoints(IEndpointRouteBuilder endpoints)` gắn route của module. Hàm có thể chưa gắn route nào ở task này.
- [x] `Program.cs` không tham chiếu `DbContext` của module. `Program.cs` chỉ gọi `Add<Module>Module` và `Map<Module>Endpoints`.
- [x] Test kiến trúc vẫn tìm đủ ba module sau khi bỏ lớp đánh dấu rỗng. Test vi phạm mẫu vẫn báo lỗi.
- [x] `Pickory.Shared/Models/` đổi thành `Pickory.Shared/Http/`. `Pickory.Shared/Extensions/` đổi thành `Pickory.Shared/DependencyInjection/`. Namespace đổi theo thư mục.
- [x] `Pickory.IntegrationTests` chia thư mục:
  - `Infrastructure/`: factory, fixture, `DatabaseSqlFiles`, `ApplicationDatabaseSchema`, `TestEndpointsStartupFilter`.
  - `Database/`: test migration, test ghi nhận mốc, test so schema, test đọc ghi, test an toàn DB test.
  - `Api/`: test hợp đồng HTTP chung.
- [x] `Pickory.UnitTests` chia thư mục theo project và thư mục của code được test, ví dụ `Shared/Persistence/`.
- [x] Namespace khớp đường dẫn thư mục trong mọi project. `.editorconfig` bật `dotnet_style_namespace_match_folder` và đặt `IDE0130` ở mức `warning`. `Directory.Build.props` bật `EnforceCodeStyleInBuild`.
- [x] Code dùng `using` thay cho tên đầy đủ lặp lại, ví dụ `StringValueGenerator`.
- [x] Không đổi logic hoặc assertion của test. Diff test chỉ gồm đường dẫn, namespace và `using`.
- [x] Mục Project Structure của `spec/SPEC.md` ghi cấu trúc trong một module và cấu trúc thư mục test.

**Kiểm tra:** `dotnet build` không có warning. `dotnet test` có số test bằng hoặc lớn hơn trước task. `dotnet ef migrations has-pending-model-changes` sạch cho ba `DbContext`. `pnpm lint && pnpm typecheck && pnpm test` xanh.

**Bằng chứng DN-01R (2026-10-05):**

- `dotnet build`: thành công, không có warning hoặc error.
- `dotnet test`: 63 test thành công, bằng số test trước task.
- Phân nhóm .NET: 33 unit test, 27 integration test và ba architecture test.
- `dotnet ef migrations has-pending-model-changes` báo không có thay đổi cho ba `DbContext`.
- Ba file `*_PrismaBaseline.cs` không đổi.
- Designer và snapshot chỉ đổi `using` và tên kiểu entity sang namespace `Entities`.
- Test kiến trúc tắt `IDE0130` cho namespace `Contracts` giả lập. Namespace này cố ý khác thư mục để mô phỏng module khác.
- `pnpm lint` và `pnpm typecheck`: thành công.
- `pnpm test`: 95 test thành công, 11 tệp thành công.

**Phụ thuộc:** DN-01 · **Quy mô:** M

## DN-02 đến DN-08

Viết tiêu chí chi tiết khi bắt đầu từng task. Danh sách task ở mục "Danh sách task" của `tasks/plan-dotnet.md`.
