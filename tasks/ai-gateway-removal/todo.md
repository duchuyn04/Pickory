# Task List: gỡ ai-gateway khỏi Pickory

Plan và quyết định thiết kế: `tasks/ai-gateway-removal/plan.md`. Quy ước chung: `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Mỗi task để build xanh. Không xóa file mà code khác còn tham chiếu.
- Trước khi báo xong, quét chỗ sót bằng lệnh `rg` ghi trong task. Bỏ qua thư mục `bin/`, `obj/`, `node_modules/`.
- Kích thước: XS 1 file, S 1-2, M 3-5, L trên 5. Số file không tính file test và file bị xóa.

---

## Phase 0: Duyệt

### AIR-00: Người dùng duyệt các việc không đảo ngược được
**Mô tả:** Nhóm "Ask first" của `spec/SPEC.md` và hồ sơ builder `.claude/builder-loop.md`. Người dùng duyệt từng mục trước khi task tương ứng bắt đầu.
**Tiêu chí nghiệm thu:** người dùng duyệt mọi mục ngày 2026-10-18, xem mục "Quyết định của người dùng" của plan.
- [x] Duyệt xóa bảng `ai_usage_logs`, `ai_settings`, `ai_embedding_cache` và schema `ef_ai_gateway` (AIR-06). Chốt Câu hỏi mở 1 và 2 của plan
- [x] Duyệt migration dữ liệu xóa dòng quyền AI trong `role_permissions` (AIR-05)
- [x] Duyệt sửa `apps/frontend/playwright.config.ts` (AIR-07)
- [x] Duyệt sửa `render.yaml` và `docker-compose.yml` (AIR-08)
- [x] Chốt Câu hỏi mở 3 về `PiiRedactor`
**Kiểm tra:** câu trả lời của người dùng ghi vào mục "Quyết định của người dùng" của plan
**Phụ thuộc:** không · **Quy mô:** XS

---

## Phase 1: Thôi gọi module

### AIR-01: Gỡ màn cấu hình AI ở frontend
**Mô tả:** Xóa màn `/admin/ai` và lối vào màn này. Backend chưa đổi trong task này.
- Xóa thư mục `apps/frontend/src/features/ai-gateway/`.
- Xóa route `/admin/ai` trong `src/app.tsx`.
- Xóa mục "Cấu hình AI" trong `src/features/identity/admin/admin-navigation.ts`.
- Xóa dòng `/admin/ai` trong `e2e/admin-screens.spec.ts`.
- Xóa "Cấu hình AI" khỏi danh sách menu `sales` thấy trong `e2e/staff-invite.spec.ts`.
- Giữ nhãn nhóm `ai-gateway` của `permission-grid.tsx` tới AIR-05, vì API vẫn trả hai quyền AI.
**Tiêu chí nghiệm thu:**
- [ ] Mở `/admin/ai` khi đã đăng nhập admin hiện trang không tìm thấy của back-office
- [ ] Menu back-office của admin không còn mục "Cấu hình AI"
- [ ] `rg -n "ai-settings|/admin/ai" apps/frontend/src apps/frontend/e2e` không còn kết quả
- [ ] Menu của `sales` trong `staff-invite.spec.ts` không còn "Cấu hình AI"
- [ ] Console 0 lỗi và 0 cảnh báo ở hai spec trên
**Kiểm tra:** `pnpm lint && pnpm typecheck && pnpm test`; `npx playwright test e2e/admin-screens.spec.ts e2e/staff-invite.spec.ts`
**Phụ thuộc:** không · **Quy mô:** S

### AIR-02: Gỡ module khỏi host `Pickory.Api`
**Mô tả:** Host thôi đăng ký module, thôi map route `/api/admin/ai/*` và thôi chạy migration AI. Project của module vẫn còn trong solution.
- `Program.cs`: xóa `AddAiGatewayModule` và `MapAiGatewayEndpoints`.
- `E2e/E2eStartup.cs`, `Benchmark/CatalogBenchmark.cs`: xóa lời gọi `MigrateAiGatewayDatabaseAsync` và comment nói migration AI tạo extension `vector`.
- `Deployment/DatabaseMigrationCommand.cs`: xóa bước `AiGateway`.
- Sửa `DatabaseMigrationCommandTests.cs` theo danh sách bước mới.
**Tiêu chí nghiệm thu:**
- [ ] `GET /api/admin/ai/settings` với phiên admin trả 404
- [ ] Lệnh `db:migrate` trên DB trống chạy xong, và các module còn lại không cần extension `vector`
- [ ] `e2e:start` dựng DB và mở API thành công
- [ ] `rg -n "AiGateway" apps/backend/src/Pickory.Api --glob "*.cs"` không còn kết quả
**Kiểm tra:** `dotnet build`; `dotnet test` cho `Pickory.UnitTests` và `Pickory.ArchitectureTests`; integration test có `--filter "FullyQualifiedName~Deployment|FullyQualifiedName~ApiIntegrationTests"`
**Phụ thuộc:** AIR-01 · **Quy mô:** S

### Checkpoint R1
- [ ] Không còn màn `/admin/ai`. API khởi động không đăng ký module AI
- [ ] `dotnet test` và `pnpm test` xanh

---

## Phase 2: Xóa code

### AIR-03: Xóa test của module AI
**Mô tả:** Xóa test chỉ kiểm module AI. Sửa test dùng chung để không chạm `AiGatewayDbContext`.
- Xóa `tests/Pickory.UnitTests/Modules/AiGateway/`.
- Xóa `tests/Pickory.IntegrationTests/Database/AiGateway/`, `Api/AiGateway/AiAdminApiTests.cs`, `Infrastructure/FixtureAiGatewayContextFactory.cs`.
- Sửa `IntegrationDatabaseFixture.cs`: bỏ `CreateAiGatewayContext` và bước migrate AI. Giữ dòng `DROP SCHEMA IF EXISTS ef_ai_gateway CASCADE`.
- Sửa `ApplicationDatabaseSchema.cs`, `DatabaseConnectionCountTests.cs`, `BaselineMigrationIntegrationTests.cs`, `DotnetSchemaIntegrationTests.cs`, `ModulePersistenceRoundtripTests.cs`.
- Sửa `Pickory.ArchitectureTests/ModuleBoundaryTests.cs`: bỏ assembly của module AI.
- Xóa tham chiếu project AI trong ba file `.csproj` của test.
**Tiêu chí nghiệm thu:**
- [ ] `rg -n "AiGateway" apps/backend/tests --glob "*.cs" --glob "*.csproj"` không còn kết quả
- [ ] Test dùng chung vẫn kiểm đủ các schema còn lại. Số schema và số bảng kỳ vọng khớp DB sau AIR-02
- [ ] Unit, architecture và integration test xanh
**Kiểm tra:** `dotnet test` cho ba project test, integration chạy toàn bộ
**Phụ thuộc:** AIR-02 · **Quy mô:** L

### AIR-04: Xóa hai project AI và package không dùng
**Mô tả:** Xóa `src/Pickory.Modules.AiGateway/` và `src/Pickory.Modules.AiGateway.Contracts/`.
- Xóa hai project khỏi `apps/backend/Pickory.sln`.
- Xóa tham chiếu trong `Pickory.Api.csproj`.
- Xóa `Microsoft.Extensions.AI.OpenAI`, `OpenAI`, `Pgvector.EntityFrameworkCore` khỏi `Directory.Packages.props`, nếu không project nào còn dùng.
- Không xóa `PiiRedactor` trước khi AIR-00 chốt Câu hỏi mở 3.
**Tiêu chí nghiệm thu:**
- [ ] `rg -n "AiGateway|AiFeature|IAiGateway" apps/backend --glob "*.cs" --glob "*.csproj" --glob "*.sln" --glob "*.props"` không còn kết quả
- [ ] `dotnet build` 0 warning, 0 error
- [ ] Unit và architecture test xanh
**Kiểm tra:** `dotnet build`; `dotnet test` cho `Pickory.UnitTests` và `Pickory.ArchitectureTests`
**Phụ thuộc:** AIR-03 · **Quy mô:** M

### AIR-05: Gỡ quyền AI
**Mô tả:** Xóa `AiUsageRead` và `AiSettingsManage`.
- `Identity.Contracts/Authorization/Permissions.cs`: xóa hai hằng và hai dòng trong `All`.
- `Identity/Roles/PermissionCatalog.cs`: xóa hai dòng nhóm `ai-gateway`.
- `Identity/Roles/DefaultRoles.cs`: xóa `AiUsageRead` khỏi role `sales`.
- `apps/frontend/src/features/identity/components/permission-grid.tsx`: xóa nhãn nhóm `ai-gateway`.
- Thêm migration dữ liệu của `identity`: xóa dòng `role_permissions` có mã `ai-usage:read` hoặc `ai-settings:manage`. Migration không đổi cấu trúc bảng.
- Sửa `PermissionMatrixTests.cs` và test ma trận quyền ở frontend nếu có.
**Tiêu chí nghiệm thu:**
- [ ] `GET` danh mục quyền của admin không còn nhóm `ai-gateway`
- [ ] Role `sales` mới seed không có quyền AI
- [ ] DB có sẵn dòng quyền AI: sau migration, bảng `role_permissions` không còn dòng đó, và API khởi động không cảnh báo quyền lạ
- [ ] Màn sửa role không còn nhóm "Cấu hình AI"
- [ ] `rg -n "ai-usage|ai-settings|AiUsageRead|AiSettingsManage" apps --glob "!**/bin/**" --glob "!**/obj/**" --glob "!**/node_modules/**"` chỉ còn file migration của task này
**Kiểm tra:** test tích hợp migration dữ liệu; integration `--filter "FullyQualifiedName~Identity|FullyQualifiedName~PermissionMatrixTests"`; `pnpm test`
**Phụ thuộc:** AIR-04, AIR-00 · **Quy mô:** M

### Checkpoint R2
- [ ] Solution không còn project AI. Không còn quyền AI trong code và trong role mặc định
- [ ] Build 0 warning. Unit, architecture và integration test xanh

---

## Phase 3: Dọn DB, cấu hình và tài liệu

### AIR-06: Xóa bảng AI trong DB
**Mô tả:** Thêm một bước dọn vào `DatabaseMigrationCommand`. Bước này chạy SQL sau trong một transaction:
- `DROP TABLE IF EXISTS public.ai_usage_logs, public.ai_settings, public.ai_embedding_cache`;
- `DROP SCHEMA IF EXISTS ef_ai_gateway CASCADE`.
Bước dọn chạy sau migration của mọi module. Xử lý extension `vector` theo Câu hỏi mở 1.
**Tiêu chí nghiệm thu:**
- [ ] DB có ba bảng AI: sau `db:migrate`, ba bảng và schema `ef_ai_gateway` không còn
- [ ] Chạy `db:migrate` lần hai trên cùng DB thành công và không đổi gì thêm
- [ ] DB trống: `db:migrate` chạy xong, không lỗi
- [ ] Bảng của module khác giữ nguyên dữ liệu
**Kiểm tra:** test tích hợp dựng DB có bảng AI giả rồi chạy lệnh; `dotnet test` cho `DatabaseMigrationCommandTests`
**Phụ thuộc:** AIR-04, AIR-00 · **Quy mô:** S

### AIR-07: Gỡ biến AI khỏi cấu hình
**Mô tả:**
- `Pickory.Shared/Configuration/AppConfig.cs`: xóa thuộc tính và luật đọc `LLM_*`, `EMBEDDING_*`, `AI_*`.
- Xóa `tests/Pickory.UnitTests/Shared/Configuration/AppConfigAiTests.cs`. Sửa phần AI của `DotEnvFileTests.cs`.
- `.env.example`: xóa khối biến AI.
- `apps/frontend/playwright.config.ts`: xóa `LLM_API_KEY`, `LLM_MODEL`, `EMBEDDING_API_KEY` (chờ AIR-00).
**Tiêu chí nghiệm thu:**
- [ ] API khởi động khi môi trường vẫn còn biến AI cũ, và bỏ qua các biến đó
- [ ] `rg -n "LLM_|EMBEDDING_|AI_MONTHLY|AI_CHAT|AI_LIVE" apps .env.example --glob "!**/bin/**" --glob "!**/obj/**" --glob "!**/node_modules/**"` không còn kết quả
- [ ] Unit test xanh. `pnpm test:e2e` xanh sau khi sửa `playwright.config.ts`
**Kiểm tra:** `dotnet test` cho `Pickory.UnitTests`; `pnpm test:e2e`
**Phụ thuộc:** AIR-04 · **Quy mô:** S

### AIR-08: Dọn tài liệu deploy và ADR
**Mô tả:**
- `docs/deployment.md`, `docs/deployment-walkthrough.md`: xóa biến AI và hướng dẫn extension `vector`.
- `render.yaml`: xóa 8 biến AI (chờ AIR-00).
- `docker-compose.yml`: đổi image theo Câu hỏi mở 1 (chờ AIR-00).
- `docs/adr/0001-ai-frameworks.md`, `docs/adr/0002-ai-gateway-provider-findings.md`: thêm dòng trạng thái "Đã thay thế ngày 2026-10-18". Ghi lý do và trỏ tới dự án `chatbot-rag`. Không xóa nội dung cũ.
- `docs/testing/identity-manual-test-cases.md`: đánh dấu ca kiểm màn AI là đã bỏ, không xóa ID.
**Tiêu chí nghiệm thu:**
- [ ] Tài liệu deploy không còn yêu cầu biến AI hay extension `vector`
- [ ] Hai ADR có dòng trạng thái mới
- [ ] Không em dash, không en dash trong file đã sửa
**Kiểm tra:** đọc lại diff; `grep -c` hai ký tự gạch trên từng file
**Phụ thuộc:** AIR-07, AIR-00 · **Quy mô:** M

---

## Phase 4: Nghiệm thu

### AIR-09: Nghiệm thu gỡ ai-gateway
**Mô tả:** Kiểm toàn bộ repo sau khi gỡ.
**Tiêu chí nghiệm thu:**
- [ ] `dotnet build` 0 warning, 0 error
- [ ] `Pickory.UnitTests`, `Pickory.ArchitectureTests`, toàn bộ `Pickory.IntegrationTests` xanh
- [ ] `pnpm typecheck`, `pnpm lint`, `pnpm test` xanh
- [ ] `pnpm test:e2e` xanh, chạy ở chế độ ẩn và `--headed`
- [ ] `rg -in "aigateway|ai-gateway|ai-usage|ai-settings|LLM_API_KEY" apps .env.example render.yaml` không còn kết quả, trừ migration dữ liệu của AIR-05 và bước dọn của AIR-06
- [ ] `db:migrate` chạy được trên bản sao DB dev có dữ liệu thật
- [ ] Review với người dùng
**Kiểm tra:** các lệnh trên, ghi kết quả thật vào báo cáo
**Phụ thuộc:** AIR-05, AIR-06, AIR-08 · **Quy mô:** S
