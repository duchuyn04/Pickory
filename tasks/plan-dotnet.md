# Kế hoạch: chuyển backend từ Node.js sang .NET

Trạng thái: người dùng đã duyệt ngày 2026-10-05, gồm cả danh sách package .NET.

## Mục tiêu

Viết lại backend bằng ASP.NET Core 10. Frontend React giữ nguyên. DB giữ PostgreSQL.

Bản .NET phải giữ đúng hợp đồng API của bản Node. Hợp đồng gồm route, JSON, mã lỗi, cookie và mã HTTP. Frontend không đổi code khi chuyển.

## Quyết định đã chốt (2026-10-05)

1. Backend dùng ASP.NET Core 10 (.NET 10, bản hỗ trợ dài hạn).
2. DB giữ PostgreSQL. Tìm kiếm vector giữ pgvector.
3. Chuyển từng module và giữ nguyên API. Xong module nào thì tắt bản Node của module đó.
4. Code tổ chức theo modular monolith. Mỗi module chỉ gọi module khác qua interface công khai.
5. Nhánh `feat/catalog` đã merge vào `main`. Bản Node của `catalog` là bản tham chiếu khi port.
6. Ngày 2026-10-05 người dùng quyết định xóa bản Node ngay sau DN-00, trước khi port. Code Node xem bằng tag `node-backend-final`. Schema DB gốc lưu ở `apps/backend-dotnet/db/reference-schema.sql`. Mục "Cách chạy song song khi chuyển" không còn áp dụng.

## Bảng thay thế công nghệ

| Phần | Bản Node hiện tại | Bản .NET |
|---|---|---|
| Web framework | Express 5 | ASP.NET Core 10, Minimal API, nhóm route theo module |
| Truy cập DB | Prisma 7 | EF Core 10 với Npgsql |
| Vector | pgvector qua Prisma | `Pgvector.EntityFrameworkCore` |
| Kiểm tra input | Zod | FluentValidation |
| Băm mật khẩu | `@node-rs/argon2` | `Isopoh.Cryptography.Argon2` |
| Gọi AI | AI SDK (`ai`, `@ai-sdk/deepseek`) | `Microsoft.Extensions.AI` với OpenAI .NET SDK |
| Log | pino | `ILogger` của ASP.NET Core, xuất JSON |
| Sự kiện giữa module | event bus trong process | dispatcher trong process, tự viết |
| Test | Vitest, Supertest | xUnit, `WebApplicationFactory` |
| Ranh giới module | quy tắc ESLint | test kiến trúc bằng `NetArchTest.Rules` |

Mọi package ở cột .NET là dependency mới. `SPEC.md` xếp việc thêm dependency vào nhóm "Ask first". Duyệt kế hoạch này nghĩa là duyệt danh sách package trên.

## Hợp đồng phải giữ

Các mục dưới đây là điều kiện để frontend không phải đổi.

- Route và phương thức HTTP giống hệt bản Node.
- Response thành công có dạng `{ data }`. Response lỗi có dạng `{ error: { code, message } }`, `message` bằng tiếng Việt.
- Tên khóa JSON theo camelCase. Thời gian theo ISO 8601 có đuôi `Z`, đúng 3 chữ số phần giây lẻ như bản Node.
- Cookie `pk_session`, `pk_anon` và `pk_oauth_state` giữ tên, thời hạn và thuộc tính `HttpOnly`, `SameSite=Lax`, `Secure`.
- Token phiên băm SHA-256 như cũ. Phiên do backend này tạo thì backend kia đọc được.
- Hash Argon2id cũ trong DB vẫn xác minh được. Người dùng không phải đặt lại mật khẩu.
- Kiểm tra `Origin` cho request ghi, giới hạn tần suất và thông báo lỗi giữ nguyên hành vi.

## DB và migration

DB không đổi trong lúc chuyển. Hai backend dùng chung một DB nên có thể chạy song song.

1. Tạo model EF Core khớp đúng schema hiện tại của `identity`, `ai-gateway` và `catalog`.
2. Tạo migration EF Core đầu tiên làm mốc. Migration này không đổi gì trong DB.
3. Đóng băng migration Prisma sau mốc này.
4. Viết mọi thay đổi schema sau đó bằng migration EF Core.

Bảng `_prisma_migrations` giữ lại cho tới khi xóa bản Node.

### Mốc DN-01

Entity Framework Core (EF Core) dùng ba `DbContext` độc lập trong project của từng module.

| `DbContext` | Số bảng | Schema lịch sử |
|---|---|---|
| `IdentityDbContext` | 9 | `ef_identity` |
| `AiGatewayDbContext` | 3 | `ef_ai_gateway` |
| `CatalogDbContext` | 8 | `ef_catalog` |

Mỗi schema lịch sử có bảng `__EFMigrationsHistory`.

`Up` của ba migration `PrismaBaseline` tạo schema theo `apps/backend-dotnet/db/reference-schema.sql`:

- 20 bảng ứng dụng.
- Bốn enum PostgreSQL.
- Index và khóa ngoại.
- Extension `vector` trong `public`.

`Down` xóa các đối tượng ứng dụng thuộc module theo thứ tự phụ thuộc.

DB đã có schema dùng `apps/backend-dotnet/db/adopt-existing-schema.sql` để ghi nhận mốc.

DB trống dùng `dotnet ef database update` theo thứ tự `Identity`, `AiGateway`, `Catalog`.

Model giữ tên cột camelCase theo `apps/backend-dotnet/db/reference-schema.sql`.
Model không thêm index khóa ngoại ngoài bản tham chiếu này.

- Enum dùng `MapEnum` của Npgsql. `PgName` giữ nhãn DB. `HasPostgresEnum` giữ thứ tự nhãn như `reference-schema.sql`.
- JSON dùng `JsonDocument` với kiểu `jsonb`.
- Thời gian dùng `timestamp(3) without time zone`. Giá trị ứng dụng dùng UTC (Coordinated Universal Time).
- Bộ chuyển đổi gắn `DateTimeKind.Utc` khi đọc. Bộ chuyển đổi bỏ `Kind` khi ghi, sau khi đổi giờ địa phương về UTC.
- Npgsql đặt múi giờ kết nối là UTC. Giá trị mặc định `CURRENT_TIMESTAMP` không phụ thuộc múi giờ máy chủ DB.
- ID dạng `text` dùng UUID (Universally Unique Identifier) do `StringValueGenerator` cấp khi thêm bản ghi.
- `embedding` giữ `double precision[]` nullable theo DB thật. Migration AiGateway tạo extension `vector` trong `public`.

`PostgresConnectionString.Convert` đổi `DATABASE_URL` dạng URL PostgreSQL sang chuỗi kết nối Npgsql.
Hàm giải mã thông tin đăng nhập trước khi dùng `NpgsqlConnectionStringBuilder`.
Hàm hỗ trợ `schema`, `sslmode`, `connect_timeout` và `application_name`.
Hàm từ chối tùy chọn khác thay vì bỏ qua yêu cầu kết nối.

Các factory migration yêu cầu `DATABASE_URL` trong môi trường. Factory không tự đọc `.env`.
Ứng dụng không chạy migration khi khởi động.

Integration test chỉ dùng `TEST_DATABASE_URL` của DB test chuyên dụng.
Fixture giữ khóa phiên PostgreSQL trước khi reset schema.
Fixture tự dựng schema bằng migration EF Core.
Test báo mọi khác biệt giữa metadata PostgreSQL và `reference-schema.sql`.
Test đọc/ghi toàn bộ bảng trong giao dịch có rollback.

Npgsql không biểu diễn `ON UPDATE` trong metadata khóa ngoại của EF Core.
Test kiểm tra riêng `ON UPDATE CASCADE` trong DB theo `reference-schema.sql`.
Migration mốc tạo khóa ngoại theo bản tham chiếu.

Phép so `pg_dump --schema-only` dùng DB trống đã chạy migration EF Core.
Phép so dùng dump của DB đã phục hồi từ `reference-schema.sql` làm bản tham chiếu.
Phép so bỏ `_prisma_migrations` và ba schema lịch sử EF Core.
Phép so chuẩn hóa xuống dòng cùng khóa ngẫu nhiên của `\restrict` và `\unrestrict`.
Dump schema ứng dụng khớp hoàn toàn sau phép so.

## Cách chạy song song khi chuyển

Vite proxy chuyển `/api/...` theo tiền tố route. Route của module đã port đi sang .NET. Route chưa port vẫn đi sang Node.

Việc này chỉ đổi `apps/frontend/vite.config.ts`, không đổi code giao diện. Khi xong toàn bộ, proxy trỏ hết sang .NET.

## Cấu trúc thư mục

```
apps/
  backend-dotnet/
    Pickory.sln
    src/
      Pickory.Api/                → khởi động app, cấu hình, gắn route của các module
      Pickory.Shared/             → lỗi chung, log, cấu hình, dispatcher sự kiện
      Pickory.Modules.Identity/   → route, service, dữ liệu, interface công khai của identity
      Pickory.Modules.AiGateway/
      Pickory.Modules.Catalog/
    tests/
      Pickory.UnitTests/
      Pickory.IntegrationTests/   → test API qua WebApplicationFactory với DB test riêng
      Pickory.ArchitectureTests/  → kiểm tra ranh giới module
```

Mỗi module là một project. Project module chỉ tham chiếu `Pickory.Shared` và project `Contracts` của module khác. Test kiến trúc chặn mọi tham chiếu khác.

### Quy ước thư mục trong module

Mọi project module dùng cùng một cây thư mục. Ví dụ dưới đây dùng module Identity.

```
Pickory.Modules.Identity/
  IdentityModule.cs               → điểm đăng ký duy nhất của module
  Features/
    Login/                        → một tính năng
      LoginEndpoint.cs
      LoginRequest.cs
      LoginRequestValidator.cs
      LoginHandler.cs
  Sessions/                       → code dùng chung trong module, đặt tên theo khái niệm
  Entities/                       → một entity hoặc một enum trong một file
    User.cs
    UserKind.cs
  Persistence/
    IdentityDbContext.cs
    IdentityDbContextFactory.cs   → factory thiết kế cho lệnh `dotnet ef`
    Configurations/               → cấu hình của từng bảng
      UserConfiguration.cs
    Migrations/
```

- `<Module>Module.cs` ở gốc project module là điểm đăng ký duy nhất của module.
- `<Module>Module.cs` có hàm `Add<Module>Module` và hàm `Map<Module>Endpoints`.
- Module có middleware chạy cho mọi request thêm hàm `Use<TênMiddleware>`, ví dụ `UseIdentityActor`.
- `Program.cs` chỉ gọi các hàm công khai của `<Module>Module.cs`.
- Project `Pickory.Modules.<Module>.Contracts` chứa kiểu mà module khác được dùng, ví dụ `Actor` của identity.
- `Features/<TínhNăng>/` chứa endpoint, request, validator và handler của một tính năng.
- `Entities/` chứa entity và enum. Tên file trùng tên kiểu.
- `Persistence/` chứa `DbContext`, factory thiết kế, `Configurations/` và `Migrations/`.
- `Configurations/` có một lớp `IEntityTypeConfiguration<T>` cho mỗi bảng. `DbContext` nạp các lớp này bằng `ApplyConfigurationsFromAssembly`.
- `DbContext` chỉ giữ `DbSet`, `ConfigureOptions`, quy ước kiểu và khai báo enum PostgreSQL.
- Code dùng chung trong module nằm ở thư mục đặt theo tên khái niệm, ví dụ `Sessions/`.
- Không tạo thư mục `Helpers/`, `Utils/`, `Common/` hoặc `Misc/`. Các tên này không cho người đọc biết nội dung của thư mục.
- Namespace khớp đường dẫn thư mục. Build báo lỗi `IDE0130` khi namespace khác thư mục.

Thư mục test theo quy ước sau:

- `Pickory.UnitTests` theo đường dẫn của code được test, ví dụ `Shared/Persistence/`.
- `Pickory.IntegrationTests/Infrastructure/` chứa factory, fixture và công cụ đọc schema.
- `Pickory.IntegrationTests/Database/` chứa test migration, test ghi nhận mốc, test so schema và test đọc/ghi.
- `Pickory.IntegrationTests/Api/` chứa test hợp đồng HTTP.

Khi xong toàn bộ, đổi tên `apps/backend-dotnet` thành `apps/backend`. Xóa bản Node.

## Danh sách task

Mỗi task phải qua build, test .NET và lint của repo trước khi commit.

| ID | Việc | Kiểm tra |
|---|---|---|
| DN-00 | Dựng solution, `/api/health`, cấu hình, định dạng lỗi chung, log, hạ tầng test, test kiến trúc. Cập nhật mục Tech Stack của `SPEC.md`. | `dotnet build`, `dotnet test`, `/api/health` trả 200 |
| DN-01 | Model EF Core cho cả 3 module và migration mốc | Migration mốc chạy trên DB hiện có, không đổi schema |
| DN-02 | `identity`: actor middleware, cookie, phiên, kiểm tra `Origin`, `GET /api/me` | Test hợp đồng cookie và phiên theo bản Node. Token băm SHA-256 khớp bản Node. |
| DN-03 | `identity`: đăng ký, đăng nhập, Google OAuth, quên và đặt lại mật khẩu, tài khoản | Hash Argon2 cũ đăng nhập được |
| DN-04 | `identity`: nhân viên, role, khách hàng, nhật ký thao tác | Ma trận quyền giống bản Node |
| DN-05 | Chuyển route `identity` sang .NET qua Vite proxy | E2E Playwright của identity xanh. Chạy lại test thủ công identity. |
| DN-05B | Đưa Node vào riêng `apps/frontend`. Gốc repo không còn `package.json`. | Lint, test, build và E2E của frontend xanh. Build và test .NET xanh. |
| DN-06 | Port `ai-gateway` | Test của module xanh trên bản .NET |
| DN-07 | Port `catalog` (CAT-01 đến CAT-03, CAT-13) | Test của module xanh trên bản .NET |
| DN-08 | Xóa bản Node, Prisma, quy tắc ESLint backend. Đổi tên thư mục. Cập nhật `SPEC.md`, `CLAUDE.md`, `AGENTS.md`. | `pnpm lint`, build và test .NET, E2E đều xanh |

Task catalog còn lại (CAT-04 trở đi) viết thẳng bằng .NET sau DN-07.

## Kiểm chứng tương đương

Bằng chứng chính là bộ test có sẵn chạy trên bản .NET.

- E2E Playwright chạy không sửa với frontend gọi bản .NET.
- Test integration của bản Node là danh sách hành vi phải có. Mỗi test Node có một test .NET tương ứng.
- Thêm test hợp đồng: cùng một request gửi tới hai backend, so sánh mã HTTP, JSON và cookie.

## Rủi ro

| Rủi ro | Cách giảm |
|---|---|
| Hash Argon2 cũ không xác minh được | Test DN-03 dùng hash thật do bản Node tạo |
| Định dạng thời gian khác bản Node | Bộ chuyển đổi JSON riêng cho `DateTime`, test hợp đồng kiểm tra |
| Thông báo lỗi validate khác bản Node | Định dạng lại lỗi FluentValidation theo mẫu của bản Node |
| Kiểu enum và JSON của PostgreSQL | Map enum qua Npgsql. Map cột JSON sang `jsonb`. |
| Schema EF Core lệch DB thật | Migration mốc phải rỗng. Nếu không rỗng thì sửa model, không sửa DB. |
| Mất kiểm tra ranh giới module khi bỏ ESLint | Viết test kiến trúc ở DN-00, trước mọi module |
| Schema Zod dùng chung không còn ở backend | Viết lại bằng C#. Hai bên chạy chung bộ dữ liệu mẫu JSON. |
