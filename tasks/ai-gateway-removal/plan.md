# Implementation Plan: gỡ ai-gateway khỏi Pickory

## Tóm tắt dễ hiểu

Pickory từng có module `ai-gateway`. Module này gọi DeepSeek và API embedding, giữ ngân sách AI và ghi chi phí từng lần gọi. Ngày 2026-10-18 người dùng chuyển chatbot sang dự án riêng `chatbot-rag`. Pickory chỉ gắn khung chat của dự án đó bằng một thẻ `<script>` (module `chatbot-widget`).

Sau thay đổi này, không còn phần nào của Pickory gọi AI. Module `ai-gateway` thành code thừa: vẫn phải build, test và cấu hình, nhưng không ai dùng. Plan này gỡ module theo từng bước nhỏ. Sau mỗi bước, build và test vẫn xanh.

Ví dụ đời thường: tháo một thiết bị không dùng nữa khỏi nhà. Thợ rút công tắc trên tường trước, rồi tháo dây, rồi tháo thiết bị, cuối cùng trát lại tường. Cả nhà vẫn có điện trong suốt quá trình.

Ngày 2026-10-18 người dùng đã duyệt `AIR-00` và chốt 3 câu hỏi ở mục "Câu hỏi mở". Việc còn lại của người dùng: xem bảng nghiệm thu ở `AIR-09`.

## Overview

Plan gỡ toàn bộ phần AI phía máy chủ và màn cấu hình AI ở back-office:

- hai project `Pickory.Modules.AiGateway` và `Pickory.Modules.AiGateway.Contracts`;
- quyền `ai-usage:read` và `ai-settings:manage`;
- màn `/admin/ai` và nhóm quyền "Cấu hình AI";
- ba bảng `ai_usage_logs`, `ai_settings`, `ai_embedding_cache` và schema lịch sử `ef_ai_gateway`;
- biến môi trường `LLM_*`, `EMBEDDING_*`, `AI_*` trong `AppConfig`;
- test, tài liệu deploy và ADR liên quan.

Danh sách task chi tiết: `tasks/ai-gateway-removal/todo.md`.

Lý do: `tasks/shopping-assistant/` bị bỏ ngày 2026-10-18. `knowledge-base` chỉ giữ trang `/help`, không còn embedding. `support-desk` làm độc lập, không còn AI soạn nháp. `admin-console` bỏ báo cáo "Hiệu quả AI".

## Hiện trạng đã dò (2026-10-18)

Các chỗ dính `ai-gateway`, ngoài hai project của module:

1. Host `Pickory.Api`:
   - `Program.cs` gọi `AddAiGatewayModule` và `MapAiGatewayEndpoints`;
   - `E2e/E2eStartup.cs`, `Deployment/DatabaseMigrationCommand.cs`, `Benchmark/CatalogBenchmark.cs` gọi `MigrateAiGatewayDatabaseAsync`;
   - `Pickory.Api.csproj` tham chiếu project của module.
2. Cấu hình: `Pickory.Shared/Configuration/AppConfig.cs` đọc `LLM_BASE_URL`, `LLM_API_KEY`, `LLM_MODEL`, `EMBEDDING_API_KEY`, `EMBEDDING_MODEL`, `AI_MONTHLY_BUDGET_USD`, `AI_CHAT_DAILY_LIMIT_USER`, `AI_CHAT_DAILY_LIMIT_ANON`, `AI_LIVE_TESTS`.
3. Quyền:
   - `Identity.Contracts/Authorization/Permissions.cs`: `AiUsageRead`, `AiSettingsManage`;
   - `Identity/Roles/PermissionCatalog.cs`: hai dòng nhóm `ai-gateway`;
   - `Identity/Roles/DefaultRoles.cs`: role `sales` có `AiUsageRead`.
4. Frontend:
   - `src/features/ai-gateway/admin/` (`ai-settings-page.tsx`, `ai-settings.css`);
   - route `/admin/ai` trong `src/app.tsx`;
   - mục menu "Cấu hình AI" trong `src/features/identity/admin/admin-navigation.ts`;
   - nhãn nhóm `ai-gateway` trong `src/features/identity/components/permission-grid.tsx`.
5. Test:
   - unit: `tests/Pickory.UnitTests/Modules/AiGateway/`, `Shared/Configuration/AppConfigAiTests.cs`, phần AI của `DotEnvFileTests.cs` và `Api/Deployment/DatabaseMigrationCommandTests.cs`;
   - integration: `Database/AiGateway/`, `Api/AiGateway/AiAdminApiTests.cs`, `Infrastructure/FixtureAiGatewayContextFactory.cs`, phần AI của `IntegrationDatabaseFixture.cs`, `ApplicationDatabaseSchema.cs`, `PermissionMatrixTests.cs`, `DatabaseConnectionCountTests.cs`, `BaselineMigrationIntegrationTests.cs`, `DotnetSchemaIntegrationTests.cs`, `ModulePersistenceRoundtripTests.cs`;
   - architecture: `ModuleBoundaryTests.cs`;
   - E2E: dòng `/admin/ai` trong `e2e/admin-screens.spec.ts`.
6. Package trong `apps/backend/Directory.Packages.props`: `Microsoft.Extensions.AI.OpenAI`, `OpenAI`, `Pgvector.EntityFrameworkCore`. Chỉ module AI dùng ba package này.
7. Extension `vector`: chỉ migration của `ai-gateway` tạo extension này. Không module nào khác có cột vector.
8. `Pickory.Shared/Privacy/PiiRedactor.cs`: trong code, chỉ module AI và test của nó dùng. ACT-03 của `activity-tracking` từng định dùng lại hàm này, nhưng bị bỏ ngày 2026-10-18. Người dùng vẫn giữ hàm để dùng lại cho log hoặc chat CSKH.
9. Tài liệu và deploy:
   - `.env.example`, `render.yaml` (8 biến AI), `docker-compose.yml` (image `pgvector/pgvector:pg17`);
   - `docs/deployment.md`, `docs/deployment-walkthrough.md`;
   - `docs/adr/0001-ai-frameworks.md`, `docs/adr/0002-ai-gateway-provider-findings.md`;
   - `apps/frontend/playwright.config.ts` (xóa `LLM_API_KEY`, `LLM_MODEL`, `EMBEDDING_API_KEY` cho API E2E).

## Đồ thị phụ thuộc

```
AIR-00 người dùng duyệt xóa bảng và dọn file deploy (không chặn AIR-01 đến AIR-04)
AIR-01 gỡ màn /admin/ai ở frontend
 └── AIR-02 gỡ module khỏi host Pickory.Api (không gọi đăng ký, route, migration)
      └── AIR-03 xóa test của module và chỗ test chạm module
           └── AIR-04 xóa hai project, tham chiếu và package
                ├── AIR-05 gỡ quyền AI và dọn dòng quyền cũ trong DB
                ├── AIR-06 xóa bảng AI và schema ef_ai_gateway (chờ AIR-00)
                └── AIR-07 gỡ biến AI khỏi AppConfig và .env.example
                     └── AIR-08 dọn tài liệu deploy và ADR (render.yaml chờ AIR-00)
AIR-05, AIR-06, AIR-08 └── AIR-09 nghiệm thu
```

## Architecture Decisions

1. Gỡ từ ngoài vào trong. Frontend và host thôi gọi module trước, rồi mới xóa test và project. Mỗi task để build và test xanh.
2. Gỡ quyền sau khi xóa module. Endpoint AI đang dùng `Permissions.AiUsageRead`, nên xóa quyền trước sẽ làm build đỏ.
3. Dòng quyền cũ trong DB. Role đã lưu có thể còn `ai-usage:read` trong bảng `role_permissions`.
   - `ActorPermissions` đã bỏ qua quyền lạ, nên người dùng không có quyền thừa.
   - `StoredPermissionCheck` cảnh báo quyền lạ mỗi lần API khởi động.
   - Một migration dữ liệu của `identity` xóa các dòng có hai mã quyền này. Migration không đổi cấu trúc bảng.
4. Xóa bảng AI bằng bước dọn một lần trong lệnh migration. Sau AIR-04 không còn `AiGatewayDbContext` để chạy migration xóa bảng. `DatabaseMigrationCommand` chạy SQL `DROP TABLE IF EXISTS` và `DROP SCHEMA IF EXISTS ef_ai_gateway CASCADE`. Lệnh chạy lại nhiều lần vẫn cho cùng kết quả.
5. Giữ dòng `DROP SCHEMA IF EXISTS ef_ai_gateway` trong reset của E2E và của test tích hợp. DB cũ trên máy dev vẫn còn schema này.
6. Bản E2E và test tích hợp không còn biến AI. `playwright.config.ts` bỏ ba biến AI ở AIR-07. Hồ sơ builder cấm sửa file này, nên task ghi "Ask first".
7. Không đổi API công khai của module khác. Module khác chưa gọi `IAiGateway`.

## Task List

Dòng tiến độ:

- [x] AIR-00 · [ ] AIR-01 · [ ] AIR-02 · [ ] AIR-03 · [ ] AIR-04 · [ ] AIR-05 · [ ] AIR-06 · [ ] AIR-07 · [ ] AIR-08 · [ ] AIR-09

### Phase 0: Duyệt
- AIR-00: người dùng duyệt các việc sau:
  - xóa bảng AI và schema `ef_ai_gateway`;
  - migration dữ liệu xóa dòng quyền AI trong `role_permissions`;
  - dọn `render.yaml`, `docker-compose.yml` và `playwright.config.ts`.

### Phase 1: Thôi gọi module
- AIR-01, AIR-02.

### Checkpoint R1
- [ ] Không còn màn `/admin/ai`. API khởi động không đăng ký module AI.
- [ ] `dotnet test` và `pnpm test` xanh.

### Phase 2: Xóa code
- AIR-03, AIR-04, AIR-05.

### Checkpoint R2
- [ ] Solution không còn project AI. Không còn quyền AI trong code và trong role mặc định.
- [ ] Build 0 warning. Unit, architecture và integration test xanh.

### Phase 3: Dọn DB, cấu hình và tài liệu
- AIR-06, AIR-07, AIR-08.

### Phase 4: Nghiệm thu
- AIR-09.

### Checkpoint cuối
- [ ] Mọi tiêu chí của AIR-09 đạt
- [ ] Review với người dùng

## Câu hỏi mở

1. Extension `vector`. Đã chốt ngày 2026-10-18: giữ extension trong DB đang chạy, nên deploy không cần quyền superuser. AIR-08 đổi image Docker dev sang PostgreSQL thường.
2. Log chi phí AI cũ. Đã chốt ngày 2026-10-18: xóa hẳn, không xuất CSV, vì đây là dữ liệu thử nghiệm.
3. `PiiRedactor`. Đã chốt ngày 2026-10-18: giữ trong `Pickory.Shared`, vì file nhỏ, có test, và dùng lại được cho log hoặc chat CSKH.

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
|---|---|---|
| Xóa bảng AI trên DB thật làm mất log chi phí | Trung bình | AIR-00 cần người dùng duyệt. Sao lưu DB trước khi chạy lệnh migration |
| Role trong DB còn mã quyền AI, API cảnh báo mỗi lần khởi động | Thấp | Migration dữ liệu ở AIR-05 xóa các dòng đó |
| Còn chỗ tham chiếu ẩn làm build đỏ ở AIR-04 | Trung bình | AIR-03 quét bằng `rg` trước khi xóa project. Build lại toàn solution sau mỗi task |
| Biến AI còn trong `render.yaml` khi code không đọc nữa | Thấp | API bỏ qua biến lạ. AIR-08 dọn sau khi người dùng duyệt |
| Test tích hợp đếm số schema hoặc số bảng bị lệch | Trung bình | AIR-03 sửa `ApplicationDatabaseSchema.cs` và các test đếm bảng cùng lúc |
| Supabase hoặc VPS còn extension `vector` | Thấp | Extension không ảnh hưởng ứng dụng. Câu hỏi mở 1 đã chốt giữ extension |

## Quyết định của người dùng

Ngày 2026-10-18 người dùng duyệt mọi đề xuất:

1. Xóa bảng `ai_usage_logs`, `ai_settings`, `ai_embedding_cache` và schema `ef_ai_gateway`. Không xuất log chi phí ra CSV.
2. Thêm migration dữ liệu xóa dòng quyền AI trong `role_permissions`.
3. Cho phép sửa `apps/frontend/playwright.config.ts` ở AIR-07, `render.yaml` và `docker-compose.yml` ở AIR-08.
4. Giữ extension `vector` trong DB đang chạy. Image Docker dev đổi sang PostgreSQL thường.
5. Giữ `PiiRedactor` trong `Pickory.Shared`. Lý do: file nhỏ, có test, dùng lại được cho log hoặc chat CSKH.
