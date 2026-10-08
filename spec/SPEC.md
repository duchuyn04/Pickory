# Spec: Pickory (quy ước chung)

File này chứa phần dùng chung cho mọi module. Danh sách module, phụ thuộc và thứ tự build nằm ở `CAPABILITY-MAP.md`. Spec riêng của từng module nằm ở `SPEC-<module-id>.md`.

## Objective

Pickory là web thương mại điện tử bán đồ điện tử, công nghệ cho khách hàng Việt Nam. Điểm khác biệt là trải nghiệm được cá nhân hóa bằng AI: gợi ý sản phẩm, chatbot tư vấn, tìm kiếm theo ý nghĩa và nội dung viết theo từng người. AI chỉ được tích hợp qua API của bên thứ ba (DeepSeek cho LLM, OpenAI cho embedding), không tự train model.

**Người dùng:**
- **Khách mua hàng**: muốn tìm đúng sản phẩm điện tử phù hợp nhu cầu và ngân sách mà không phải tự đọc hết thông số.
- **Quản trị viên**: quản lý sản phẩm, nhân viên, phân quyền, cấu hình AI và theo dõi AI có thực sự giúp tăng doanh số không.
- **Nhân viên bán hàng**: xử lý đơn, đối soát thanh toán COD và SePay.
- **Nhân viên CSKH**: tiếp nhận hội thoại khi chatbot chuyển cho người thật, hỗ trợ khách, kiểm duyệt review.

Phân quyền theo RBAC, chi tiết trong `SPEC-identity.md`.

**Thành công ở mức sản phẩm:**
- Khách vãng lai và khách có tài khoản đều mua hàng được từ đầu đến cuối (COD hoặc chuyển khoản qua SePay).
- Bốn tính năng AI chạy trên dữ liệu thật và có số đo hiệu quả trên dashboard admin.
- API AI lỗi hoặc hết ngân sách thì tính năng mua bán vẫn chạy bình thường.

## Tech Stack

| Thành phần | Lựa chọn |
|---|---|
| Ngôn ngữ | TypeScript (strict) cho frontend, C# cho backend |
| Runtime | Node.js LTS cho frontend và tooling, .NET 10 cho backend |
| Package manager | pnpm cho frontend (`apps/frontend`), NuGet cho backend |
| Frontend | React + Vite, React Router, TanStack Query |
| Backend | ASP.NET Core 10, Minimal API, modular monolith |
| Validation | Zod ở frontend, FluentValidation ở backend |
| Database | PostgreSQL + pgvector, EF Core với Npgsql |
| LLM | DeepSeek, tên model trong biến môi trường |
| AI SDK | `Microsoft.Extensions.AI` trên OpenAI .NET SDK, chỉ dùng trong `Pickory.Modules.AiGateway/Providers/` |
| RAG tooling | LangChain.js (`@langchain/textsplitters`, PDF loader của `@langchain/community`), chỉ dùng trong `knowledge-base/ingest/` |
| Embedding | OpenAI `text-embedding-3-small` (1536 chiều) |
| Thanh toán | COD, SePay (VietQR + webhook) |
| Test | Vitest cho frontend, xUnit cho backend, Playwright cho E2E |
| Lint/format | ESLint + Prettier |
| Môi trường dev | Docker Compose cho PostgreSQL |

Phiên bản cụ thể lấy bản ổn định mới nhất lúc khởi tạo. Frontend khóa phiên bản trong `apps/frontend/pnpm-lock.yaml`, backend khóa trong `apps/backend/Directory.Packages.props`. Lý do chọn framework AI ghi tại `docs/adr/0001-ai-frameworks.md`.

Backend dùng ASP.NET Core 10 tại `apps/backend`. Ngày 2026-10-05 người dùng quyết định xóa bản Node trước khi port xong. Module `catalog` mới có logic nền và sự kiện (DN-07), chưa có API. Các task từ CAT-04 trở đi viết thẳng bằng .NET. Code bản Node cũ xem bằng tag `node-backend-final`. Hàng RAG tooling ở bảng trên là lựa chọn của bản Node, sẽ cập nhật khi làm `knowledge-base`.

## Commands

Chạy ở gốc repo:

```bash
docker compose up -d db                                                       # chạy PostgreSQL + pgvector
dotnet build apps/backend/Pickory.sln                                         # build backend .NET
dotnet test apps/backend/Pickory.sln                                          # test backend (unit, integration, kiến trúc)
dotnet run --project apps/backend/src/Pickory.Api                             # chạy API ở cổng 3000
dotnet run --project apps/backend/src/Pickory.Api -- identity:seed            # tạo role mặc định cho DB trong DATABASE_URL
dotnet run --project apps/backend/src/Pickory.Api -- identity:create-admin    # tạo admin đầu tiên; truyền --email, --password hoặc ADMIN_EMAIL, ADMIN_PASSWORD
dotnet run --project apps/backend/src/Pickory.Api -- catalog:seed             # ghi 30 sản phẩm mẫu vào DB trong DATABASE_URL; chạy lại không tạo trùng; từ chối khi ASPNETCORE_ENVIRONMENT là Production
DATABASE_URL=postgresql://pickory:pickory@localhost:5432/pickory_bench dotnet run --project apps/backend/src/Pickory.Api -- catalog:benchmark   # sinh 500 sản phẩm rồi đo p95 của GET /api/products; chỉ chạy khi tên DB chứa "bench"
```

Chạy trong `apps/frontend`:

```bash
pnpm install                      # cài dependency
pnpm dev                          # chạy web ở cổng 5173; Vite chuyển `/api` sang API ở cổng 3000
pnpm test                         # test TypeScript của frontend (Vitest)
pnpm test -- --coverage           # kèm báo cáo coverage
pnpm test:e2e                     # Playwright; tự chạy API .NET với DB `pickory_e2e`
pnpm lint                         # ESLint và Prettier
pnpm typecheck                    # tsc --noEmit cho code web và test E2E
pnpm build                        # build frontend
```

Khi dev, mở hai terminal: một terminal chạy API bằng `dotnet run`, một terminal chạy `pnpm dev` trong `apps/frontend`.

## Project Structure

```
apps/
  frontend/               → React app (Vite), package @pickory/frontend, mọi cấu hình Node của repo
    src/features/<module-id>/   → trang, component, hook của từng module
      customer/           → trang dành cho khách (module lớn)
      admin/              → trang back-office (module lớn)
      components/         → component dùng trong nhiều trang của module
      hooks/              → hook của module
    src/shared/                 → component và tiện ích dùng chung
    src/shared/catalog/         → kiểu thuộc tính và Zod schema mô tả sản phẩm
    test/unit/features/<module-id>/ → test hook và logic UI, đối chiếu với src/features/
    e2e/                        → Playwright test, cấu hình ở playwright.config.ts
  backend/                → ASP.NET Core 10 API
    Pickory.sln
    src/
      Pickory.Api/        → khởi động app, middleware, gọi đăng ký của từng module
      Pickory.Shared/     → Configuration/, DependencyInjection/, Events/, Exceptions/, Http/, Json/, Persistence/
      Pickory.Modules.<Module>/
        <Module>Module.cs → điểm đăng ký duy nhất của module
        Features/<TínhNăng>/ → endpoint, request, validator, handler của một tính năng
        Entities/         → một entity hoặc một enum trong một file
        Persistence/      → DbContext, factory thiết kế, Configurations/, Migrations/
      Pickory.Modules.<Module>.Contracts/ → kiểu module khác được dùng, ví dụ Actor của identity
    tests/
      Pickory.UnitTests/  → theo đường dẫn code được test, ví dụ Shared/Persistence/
      Pickory.IntegrationTests/ → Infrastructure/, Database/, Api/
      Pickory.ArchitectureTests/ → kiểm tra ranh giới module
    db/reference-schema.sql → schema DB gốc của bản Node, dùng để đối chiếu migration
spec/                     → CAPABILITY-MAP.md, SPEC.md (file này), SPEC-<module-id>.md
docs/adr/                 → Architecture Decision Records
```

Frontend đặt unit test trong `apps/frontend/test/unit/` và chạy bằng Vitest. Code test import trực tiếp từ `src/`, không tạo re-export trung gian. E2E test nằm trong `apps/frontend/e2e/` và chạy bằng Playwright. Backend đặt toàn bộ test trong `apps/backend/tests/` với ba project test: `Pickory.UnitTests`, `Pickory.IntegrationTests` và `Pickory.ArchitectureTests`.

Backend .NET theo mục "Quy ước thư mục trong module" của `tasks/plan-dotnet.md`. Namespace khớp đường dẫn thư mục. Không tạo thư mục `Helpers/`, `Utils/`, `Common/` hoặc `Misc/`.

**Quy tắc ranh giới module:** module backend chỉ được tham chiếu `Pickory.Shared` và project `*.Contracts` của module khác. `Pickory.ArchitectureTests` kiểm tra quy tắc này. Mỗi module chỉ ghi vào bảng của chính nó.

## Code Style

```csharp
// apps/backend/src/Pickory.Modules.Identity/Features/AdminRoles/AdminRolesEndpoints.cs
endpoints.MapPost("/api/admin/roles", CreateRoleAsync)
    .RequirePermission(Permissions.RolesManage);

private static async Task<IResult> CreateRoleAsync(
    HttpContext context,
    CreateRoleRequest? request,
    IValidator<CreateRoleRequest> validator,
    RoleManagementHandler handler)
{
    // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
    var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
    // Handler nhận actor để chặn người gọi cấp quyền vượt quá quyền của chính họ.
    var role = await handler.CreateAsync(context.GetActor(), validRequest, context.RequestAborted);
    // Trả 201 vì route vừa tạo role mới, giống bản Node.
    return Results.Json(new ApiResponse<RoleResponse>(role), statusCode: StatusCodes.Status201Created);
}
```

```csharp
// apps/backend/src/Pickory.Shared/Exceptions/AppException.cs
namespace Pickory.Shared.Exceptions;

// Định nghĩa lớp ngoại lệ nghiệp vụ để mang mã lỗi máy đọc được và thông điệp tiếng Việt.
public class AppException : Exception
{
    // Lưu mã lỗi định danh để client phân loại và hiển thị tương ứng.
    public string Code { get; }

    // Lưu mã trạng thái HTTP để middleware trả đúng mã phản hồi mong muốn.
    public int StatusCode { get; }

    public AppException(string code, int statusCode, string message)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}
```

- Quy ước đặt tên frontend: file và thư mục dùng `kebab-case`, biến và hàm dùng `camelCase`, type và component dùng `PascalCase`.
- Quy ước đặt tên backend .NET: file và type dùng `PascalCase`, một type mỗi file, namespace khớp đường dẫn thư mục. Hằng số dùng `PascalCase`.
- Bảng database dùng `snake_case` số nhiều. EF Core map tên bảng trong thư mục `Persistence/Configurations/`.
- Response thành công: `{ data }`. Response lỗi: `{ error: { code, message } }`, `message` bằng tiếng Việt để hiển thị được cho người dùng.
- Mọi input từ bên ngoài đều validate trước khi dùng: frontend dùng Zod, backend dùng FluentValidation (`AbstractValidator`).
- Tiền lưu dạng số nguyên VND (`int`), không dùng số thực.
- Thời gian lưu UTC, hiển thị theo `Asia/Ho_Chi_Minh`.
- Ở frontend, không dùng `any` và không để `console.log` trong code commit. Backend dùng `ILogger` xuất JSON.

## Testing Strategy

| Cấp | Công cụ | Dùng cho | Vị trí |
|---|---|---|---|
| Unit frontend | Vitest | Logic UI, hook, tiện ích và schema dùng chung | `apps/frontend/test/unit/` |
| Unit backend | xUnit | Logic nghiệp vụ, định nghĩa thuộc tính, định dạng và tính toán | `apps/backend/tests/Pickory.UnitTests/` |
| Integration | xUnit + `WebApplicationFactory` + PostgreSQL thật qua `TEST_DATABASE_URL` | Hợp đồng HTTP, truy vấn EF Core, ràng buộc DB, webhook | `apps/backend/tests/Pickory.IntegrationTests/` |
| Kiến trúc | xUnit + `NetArchTest.Rules` | Kiểm tra ranh giới phụ thuộc giữa các module | `apps/backend/tests/Pickory.ArchitectureTests/` |
| E2E | Playwright | Luồng quan trọng: đăng ký, mời nhân viên, tìm kiếm, thêm giỏ, checkout, chat | `apps/frontend/e2e/` |
| AI contract | xUnit (`[LiveAiFact]`) | Kiểm tra provider AI thật trả đúng định dạng khi `AI_LIVE_TESTS=1` | `apps/backend/tests/Pickory.IntegrationTests/` |

- Test tự động thông thường không bao giờ gọi API AI hay SePay thật. Test dùng fake provider hoặc test double trong bộ nhớ.
- Dự án không cài đặt ngưỡng coverage bắt buộc cho backend và frontend.
- Mỗi bản sửa lỗi phải có test tái hiện lỗi trước khi sửa code.

## Boundaries

**Always:**
- Chạy `dotnet test` cho backend. Chạy `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend` trước khi commit.
- Validate mọi input bên ngoài bằng Zod ở frontend và FluentValidation ở backend.
- Gọi AI qua `ai-gateway`, không gọi SDK provider trực tiếp từ module khác.
- Xác thực webhook SePay trước khi xử lý, và xử lý idempotent.
- Bảo vệ mọi endpoint back-office bằng `RequirePermission`. Ghi nhật ký bằng `IAuditLogWriter` khi nhân viên đổi dữ liệu.
- Cập nhật spec trước khi đổi hành vi đã ghi trong spec.

**Ask first:**
- Thêm dependency mới.
- Đổi schema database của module đã có dữ liệu thật.
- Đổi interface public hoặc project Contracts của một module.
- Thêm quyền mới vào danh mục quyền, hoặc đổi quyền mặc định của role tạo sẵn.
- Đổi provider AI, model, hoặc gửi thêm loại dữ liệu người dùng sang API AI.
- Thay đổi CI/CD hoặc cấu hình deploy.

**Never:**
- Commit secret (API key, mật khẩu DB). Secret chỉ nằm trong `.env`, có `.env.example` mô tả.
- Gửi tên, SĐT, email, địa chỉ của người dùng sang API AI.
- Thu thập hành vi cho cá nhân hóa khi người dùng chưa đồng ý.
- Xóa hoặc skip test đang fail để cho pass.
- Lưu mật khẩu dạng có thể giải mã, hoặc log mật khẩu, token, API key.
- Bật LangSmith tracing, hoặc gửi trace, prompt, hội thoại sang dịch vụ quan sát bên ngoài.

## Success Criteria (toàn dự án)

Từng module có tiêu chí riêng trong spec của module. Toàn dự án coi là xong MVP khi:
- Mọi module trong `CAPABILITY-MAP.md` đạt tiêu chí của spec riêng.
- Mỗi role (`customer`, `admin`, `sales`, `support`) có ít nhất một luồng E2E chạy được từ đầu đến cuối.
- E2E pass cho luồng: khách vãng lai tìm kiếm bằng câu tự nhiên → chat hỏi so sánh → thêm giỏ → đăng ký → checkout SePay → webhook xác nhận đơn.
- Tắt API AI (sai key) thì toàn bộ luồng mua hàng vẫn pass E2E, các khối AI hiển thị phương án dự phòng.

## Open Questions

1. Hạ tầng deploy (VPS, Railway, Render, AWS...) và tên miền.
2. Dịch vụ gửi email (Resend, AWS SES, SMTP riêng...).
3. Rà soát pháp lý về dữ liệu cá nhân: gửi dữ liệu hành vi (đã ẩn danh hóa) sang DeepSeek và OpenAI là chuyển dữ liệu ra nước ngoài. Cần người có chuyên môn xác nhận nghĩa vụ theo quy định bảo vệ dữ liệu cá nhân hiện hành của Việt Nam.
