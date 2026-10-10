# Implementation Plan: rest-routes

## Overview

Plan này đổi các route cũ đặt theo hành động sang REST thuần, mức tài nguyên (Richardson mức 2). Người dùng chốt luật này ngày 2026-10-10. Xem quyết định 20 của `tasks/shipping/plan.md`.

Luật:
- Đường dẫn là danh từ.
- Hành động đi qua method HTTP và mã trạng thái chuẩn.
- Response giữ dạng `{ data }`. Không có HATEOAS.

Plan chạy sau Phase 2 của `shipping` và trước `commerce`. `commerce` đã đề xuất cách đặt tên hành động ở quyết định 17 của `tasks/commerce/plan.md`. Plan này dùng cùng kiểu đặt tên.

Task list chi tiết: `tasks/rest-routes/todo.md`.

Plan này chỉ là tài liệu. Chưa có code nào đổi. Người dùng duyệt plan trước khi làm.

## Kết quả khảo sát route (2026-10-10)

Backend có 62 route trong `apps/backend/src`. Mỗi module gắn route qua `MapGroup(string.Empty)` trong `*Module.cs`. Đường dẫn tuyệt đối nằm trong từng file `*Endpoints.cs`.

| Nhóm | Số route | Kết luận |
| --- | --- | --- |
| Đã đúng REST | 46 | Giữ nguyên |
| Cần đổi | 14 route cũ, thành 12 route mới | Đổi trong plan này |
| Ngoại lệ có lý do | 2 route Google OAuth | Giữ nguyên, xem quyết định 6 |

`/p/{slug}` và `/c/{slug}` nằm trong 46 route đúng REST ở dòng đầu bảng. Hai route này trả HTML, không phải API. Xem quyết định 7.

### Bảng route cần đổi

| # | Route cũ | File và dòng | Route mới | Thành công | Lý do |
| --- | --- | --- | --- | --- | --- |
| 1 | `POST /api/auth/login` | `Pickory.Modules.Identity/Features/Login/CustomerLoginEndpoint.cs:19` | `POST /api/auth/sessions` | 201 | Đăng nhập tạo ra một phiên |
| 2 | `POST /api/admin/auth/login` | `.../Features/Login/StaffLoginEndpoint.cs:18` | `POST /api/admin/auth/sessions` | 201 | Cùng lý do, phiên nhân viên có hạn 12 giờ |
| 3 | `POST /api/auth/logout` | `.../Features/Logout/LogoutEndpoint.cs:18` | `DELETE /api/auth/sessions/current` | 200 | Đăng xuất xóa phiên hiện tại |
| 4 | `POST /api/auth/register` | `.../Features/Register/RegisterEndpoint.cs:21` | `POST /api/auth/registrations` | 201 | Đăng ký tạo một bản đăng ký |
| 5 | `POST /api/auth/verify-email` | `.../Features/VerifyEmail/VerifyEmailEndpoint.cs:15` | `POST /api/auth/email-verifications` | 200 | Mỗi lần gửi token là một lần xác thực |
| 6 | `POST /api/auth/forgot-password` | `.../Features/ForgotPassword/ForgotPasswordEndpoint.cs:15` | `POST /api/auth/password-resets` | 200 | Mỗi lần gọi tạo một yêu cầu đặt lại |
| 7 | `POST /api/auth/reset-password` | `.../Features/ResetPassword/ResetPasswordEndpoint.cs:16` | `PUT /api/auth/password` | 200 | Đặt mật khẩu bằng token. `PUT` idempotent |
| 8 | `POST /api/me/password` | `.../Features/ChangePassword/ChangePasswordEndpoint.cs:20` | `PUT /api/me/password` | 200 | Thay giá trị mật khẩu của chính người gọi |
| 9 | `POST /api/admin/customers/{id}/lock` | `.../Features/AdminCustomers/AdminCustomersEndpoints.cs:23` | `PATCH /api/admin/customers/{id}` với `{ "locked": true }` | 200 | Khóa là đổi một trường của khách |
| 10 | `POST /api/admin/customers/{id}/unlock` | `.../AdminCustomersEndpoints.cs:23` | `PATCH /api/admin/customers/{id}` với `{ "locked": false }` | 200 | Gộp với dòng 9 |
| 11 | `POST /api/admin/staff/{id}/lock` | `.../Features/AdminStaff/AdminStaffEndpoints.cs:23` | `PATCH /api/admin/staff/{id}` với `{ "locked": true }` | 200 | Giống dòng 9 |
| 12 | `POST /api/admin/staff/{id}/unlock` | `.../AdminStaffEndpoints.cs:23` | `PATCH /api/admin/staff/{id}` với `{ "locked": false }` | 200 | Gộp với dòng 11 |
| 13 | `POST /api/admin/products/{id}/status` | `Pickory.Modules.Catalog/Features/Products/ProductsEndpoints.cs:33` | `PUT /api/admin/products/{id}/status` | 200 | `status` là tài nguyên con. `PUT` idempotent |
| 14 | `POST /api/admin/skus/{id}/adjust-stock` | `.../Features/Inventory/InventoryEndpoints.cs:20` | `POST /api/admin/skus/{id}/movements` | 201 | Mỗi lần điều chỉnh tạo một dòng biến động. `GET` cùng đường dẫn đã có ở dòng 23 |

Mã lỗi, body và thông báo lỗi giữ nguyên. Quyền (`RequirePermission`, `RequireAuth`) giữ nguyên. Mã thành công chỉ đổi ở dòng 1, 2 và 14 (200 sang 201).

### Route giữ nguyên

| Route | File và dòng | Lý do giữ |
| --- | --- | --- |
| `GET /api/health` | `Pickory.Api/Program.cs:146` | Danh từ |
| `GET`, `PUT /api/admin/ai/settings`, `GET /api/admin/ai/usage` | `AdminSettingsEndpoints.cs:18,20`, `AdminUsageEndpoint.cs:15` | Danh từ |
| `GET`, `POST /api/brands`, `/api/admin/brands`, `PATCH`, `DELETE /api/admin/brands/{id}` | `BrandsEndpoints.cs:18-23` | Danh từ, đúng method |
| `/api/categories`, `/api/admin/categories`, `/api/admin/categories/{id}` | `CategoriesEndpoints.cs:17-24` | Danh từ, đúng method |
| `/api/admin/products`, `/api/admin/products/{id}` (GET, POST, PATCH, DELETE) | `ProductsEndpoints.cs:20,23,26,29,36` | Danh từ, đúng method |
| `GET /api/admin/skus/{id}/movements`, `GET /api/admin/inventory/low-stock` | `InventoryEndpoints.cs:23,25` | Danh từ |
| `GET /api/products`, `/api/products/{slug}`, `/api/products/compare` | `PublicProductsEndpoints.cs:13`, `PublicProductDetailEndpoints.cs:15,16` | `compare` chỉ đọc. Xem Câu hỏi mở 3 |
| `POST /api/admin/uploads/signature` | `UploadSignatureEndpoint.cs:28` | Danh từ. `POST` tạo một chữ ký ngắn hạn. Xem Câu hỏi mở 4 |
| `GET /api/me`, `PATCH /api/me`, `DELETE /api/me` | `GetMeEndpoint.cs:16`, `UpdateProfileEndpoint.cs:18`, `DeleteAccountEndpoint.cs:20` | Đúng method |
| `PUT /api/me/consent` | `SaveConsentEndpoint.cs:18` | Danh từ, `PUT` idempotent |
| `GET /api/admin/audit-logs`, `/api/admin/customers`, `/api/admin/customers/{id}` | `AdminAuditLogsEndpoints.cs:14`, `AdminCustomersEndpoints.cs:21,22` | Danh từ |
| `/api/admin/roles`, `/api/admin/permissions`, `/api/admin/roles/{id}` | `AdminRolesEndpoints.cs:20-25` | Danh từ, đúng method |
| `GET /api/admin/staff`, `POST /api/admin/staff/invitations`, `PUT /api/admin/staff/{id}/roles` | `AdminStaffEndpoints.cs:20,21,22` | Danh từ |
| `GET /api/addresses/provinces`, `/api/addresses/wards` | `AddressEndpoints.cs:24,25` | Danh từ |
| `GET`, `PUT /api/admin/shipping/settings` | `ShippingSettingsEndpoints.cs:22,23` | Danh từ |
| `POST /api/webhooks/ghn` | `GhnWebhookEndpoint.cs:34` | Danh từ |
| `GET /api/auth/google/start`, `GET /api/auth/google/callback` | `GoogleStartEndpoint.cs:19`, `GoogleCallbackEndpoint.cs:19` | Ngoại lệ. Xem quyết định 6 |
| `GET /p/{slug}`, `GET /c/{slug}` | `PublicPageEndpoints.cs:16,17` | Trang HTML, không phải API. Xem quyết định 7 |

## Chỗ phụ thuộc đường dẫn hoặc method cũ

| Loại | Vị trí | Nhóm route bị ảnh hưởng |
| --- | --- | --- |
| Frontend, client API | `apps/frontend/src/features/identity/customer/{login-page,register-page,forgot-password-page,reset-password-page,verify-email-page}.tsx`, `src/shared/header.tsx:21`, `src/features/identity/admin/{admin-layout.tsx:76,login-page.tsx:43}` | REST-01, REST-02 |
| Frontend, đổi mật khẩu | `src/features/identity/components/change-password-form.tsx:47` | REST-02 |
| Frontend, khóa và mở khóa | `src/features/identity/admin/customers-page.tsx:80`, `src/features/identity/admin/staff-page.tsx:67` | REST-03 |
| Frontend, catalog | `src/features/catalog/admin/product-form.tsx:529`, `src/features/catalog/admin/adjust-stock-dialog.tsx:58` | REST-04 |
| Frontend, Vitest | `apps/frontend/test/unit/features/identity/customer/reset-password-page.test.tsx`, `test/unit/features/catalog/admin/{adjust-stock-dialog,product-form-page-status,sku-inventory-page}.test.tsx` | REST-02, REST-04 |
| E2E Playwright | `apps/frontend/e2e/{catalog-category-page,catalog-compare-page,catalog-inventory,catalog-product-page,catalog-product-status,profile-validation}.spec.ts` (các file dùng route cũ khi đăng nhập hoặc gọi API trực tiếp) | REST-01, REST-04 |
| Test tích hợp | `apps/backend/tests/Pickory.IntegrationTests/Api/Identity/{LoginTests,RegisterTests,EmailTokenTests,AccountTests,AdminCustomersTests,AdminStaffTests,ConsentTests,GoogleSignInTests,ProfileValidationTests}.cs`, `Api/Catalog/{InventoryTests,ProductStatusTests,ProductImageTests,ProductStockConcurrencyTests}.cs`, `Api/PermissionMatrixTests.cs` | REST-01 đến REST-04 |
| Unit test | `apps/backend/tests/Pickory.UnitTests/Modules/Identity/GoogleOAuth/GoogleOAuthClientTests.cs` (chỉ nhắc route Google, không đổi) | Không đổi |
| Spec | `spec/SPEC-identity.md` (dòng 179, 180, 229-256), `spec/SPEC-catalog.md` (dòng 204, 210) | REST-01 đến REST-04 |
| Tài liệu test tay | `docs/testing/identity-manual-test-cases.md` (dòng 76, 91, 131, 151) | REST-01 đến REST-05 |
| Giới hạn tần suất | `IdentityRateLimiters` khóa theo mục đích và IP, không theo đường dẫn. Task kiểm lại, xem tiêu chí REST-02 | REST-02 |
| Audit log | Tên hành động `customer.lock`, `customer.unlock`, `staff.lock`, `staff.unlock` nằm trong `CustomerManagementHandler.cs` và `StaffManagementHandler.cs`. Giữ nguyên tên để bản ghi cũ và mới cùng dạng | REST-03 |
| Middleware so đường dẫn | `IdentityModule.cs:107` chỉ so tiền tố `WebhookRoutes.PathPrefix` (`/api/webhooks`). `RequestOriginGuard` so theo method (`POST`, `PUT`, `PATCH`, `DELETE`), không theo đường dẫn. Cả hai không cần đổi | Không đổi |
| Cookie | `SessionCookies.cs:39,53` đặt `Path = "/"`. Không phụ thuộc route | Không đổi |
| CORS | Không có `WithOrigins` hay `AllowCredentials`. Dev đi qua proxy `'/api'` của Vite (`apps/frontend/vite.config.ts:10`). Không cần đổi | Không đổi |
| Link trong email | Email xác minh và đặt lại mật khẩu trỏ tới trang frontend `/verify-email?token=` và `/reset-password`, không trỏ tới API. Không đổi | Không đổi |
| Google OAuth đã đăng ký ngoài repo | Redirect URI `{apiBaseUrl}/api/auth/google/callback` (`GoogleRequestMode.cs:16`) đăng ký trong Google Cloud Console. Giữ nguyên route nên không cần đổi bên ngoài | Không đổi |
| Tài liệu API | `/api/openapi/v1.json` sinh từ route nên tự cập nhật. Chỉ có ở môi trường `Development` | Tự động |
| Plan khác | `tasks/commerce/plan.md` (quyết định 17), `tasks/activity-tracking/{plan,todo}.md`, `tasks/personalized-content/plan.md` nhắc route cũ. Task REST-05 đối chiếu | REST-05 |

## Đồ thị phụ thuộc

```
REST-01 phiên đăng nhập
REST-02 vòng đời tài khoản
REST-03 khóa và mở khóa
REST-04 catalog
 └── REST-05 kiểm cuối (cần REST-01 đến REST-04)
```

REST-01 đến REST-04 không phụ thuộc nhau về code. Làm tuần tự để không đụng nhau ở `PermissionMatrixTests.cs` và `spec/SPEC-identity.md`. Thứ tự đề xuất là REST-01, REST-02, REST-03, REST-04.

## Architecture Decisions

1. **Luật đặt tên.** Đoạn đường dẫn là danh từ số nhiều khi là tập hợp, ví dụ `sessions`. Đoạn là danh từ số ít khi chỉ có một đối tượng, ví dụ `password`. Không có động từ trong đường dẫn.
2. **Chọn method.** Tạo bản ghi mới dùng `POST` và trả 201. Thay hoàn toàn một giá trị dùng `PUT`. Đổi một phần dùng `PATCH`. Xóa dùng `DELETE`.
3. **Giữ body và mã lỗi.** Plan chỉ đổi đường dẫn, method và mã thành công của 3 route tạo mới (dòng 1, 2, 14). Body request, body response, mã lỗi và thông báo giữ nguyên. Lý do: giảm số chỗ frontend phải sửa và giữ test lỗi nguyên vẹn.
4. **Phiên là tài nguyên.** Đăng nhập là `POST .../sessions`. Đăng xuất là `DELETE /api/auth/sessions/current`. Đường dẫn đăng xuất dùng chung cho khách và nhân viên, giống route hiện tại. Token phiên vẫn nằm trong cookie `pk_session`.
5. **Khóa tài khoản là `PATCH` trường `locked`.** Một route thay cho hai route `lock` và `unlock`. Quyền giữ nguyên: `customers:manage` và `staff:manage`. Luật nghiệp vụ giữ nguyên, ví dụ không khóa chính mình. Audit log vẫn ghi `customer.lock` hoặc `customer.unlock` tùy giá trị `locked`.
6. **Hai route Google OAuth giữ nguyên.** `google/start` là chuyển hướng trình duyệt, không phải gọi API JSON. `google/callback` là địa chỉ đã đăng ký với Google. Đổi route này buộc đổi cấu hình ngoài repo và làm hỏng đăng nhập Google nếu quên. Xem Câu hỏi mở 1.
7. **`/p/{slug}` và `/c/{slug}` không thuộc plan.** Hai route trả HTML của SPA kèm thẻ meta SEO. Đường dẫn trùng với route của trang web, và công cụ tìm kiếm đã lập chỉ mục. Hai route đã là danh từ.
8. **Chuyển một lần, không giữ route cũ.** Frontend nằm cùng repo và cùng được deploy. Không có client bên ngoài gọi các route này. Giữ route cũ làm tăng số route cần test và quyền cần kiểm. Mỗi task đổi backend và frontend trong cùng một lần. Xem Câu hỏi mở 2 cho trường hợp bundle frontend bị cache.
9. **Mỗi task là một lát dọc.** Mỗi task đổi backend, test tích hợp, frontend, Vitest, E2E, spec và tài liệu của một nhóm route. App chạy được sau mỗi task.
10. **Chặn route kiểu hành động mới.** REST-05 thêm một test liệt kê mọi route đã đăng ký và từ chối đoạn đường dẫn nằm trong danh sách động từ. Test này giữ luật 20 cho các module sau.

## Task List

Chi tiết từng task ở `tasks/rest-routes/todo.md`.

### Phase 1: Đổi route theo nhóm
- [x] REST-01 · [x] REST-02 · [x] REST-03 · [x] REST-04

### Checkpoint R1
- [x] Mỗi nhóm route đổi xong, `dotnet test` và `pnpm test` xanh
- [ ] Review với người dùng

### Phase 2: Kiểm cuối
- [x] REST-05

### Checkpoint cuối
- [x] Lệnh grep route cũ trong `apps`, `spec`, `docs` ra 0 kết quả
- [x] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `commerce`

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Sót một chỗ gọi route cũ | Chức năng trả 404 hoặc 405 mà không báo lỗi lúc build | Lệnh grep ở REST-05. Chạy cả bộ E2E sau mỗi task |
| Bundle frontend cũ trong trình duyệt gọi route cũ sau khi deploy | Người dùng thấy lỗi tới khi tải lại trang | Chấp nhận. Xem Câu hỏi mở 2 |
| Đổi `POST` thành `PATCH` làm lệch luật kiểm `Origin` | Mất bảo vệ CSRF | `RequestOriginGuard` đã bao `POST`, `PUT`, `PATCH`, `DELETE`. Test `MT-ID-003` giữ nguyên và task kiểm lại |
| `PUT /api/auth/password` dễ nhầm với `PUT /api/me/password` | Lập trình viên gọi nhầm route | Đặt tên rõ trong spec. Xem Câu hỏi mở 5 |
| Mã 201 làm frontend đọc sai | Lỗi ở nơi chỉ kiểm `status === 200` | Vitest và E2E của từng task kiểm cả mã trả về |
| Đổi tên mất lịch sử audit log | Khó tra cứu | Giữ nguyên tên hành động audit. Plan không đổi |

## Câu hỏi mở

Người dùng chốt cả 6 đề xuất dưới đây ngày 2026-10-10.

1. **Route Google OAuth.** Đề xuất: giữ `GET /api/auth/google/start` và `/callback`. Lý do ở quyết định 6. Nếu đổi, người dùng phải cập nhật redirect URI trong Google Cloud Console cùng lúc.
2. **Giữ route cũ một thời gian hay không.** Đề xuất: không giữ, vì chưa có client ngoài repo. Nếu đã có bản deploy thật với người dùng đang mở web, nên giữ route cũ một lượt deploy và thêm task xóa.
3. **`GET /api/products/compare`.** Đề xuất: giữ, vì chỉ đọc và là truy vấn. Phương án khác là `GET /api/product-comparisons?ids=`. Phương án khác tốn thêm Vitest và E2E của catalog.
4. **`POST /api/admin/uploads/signature`.** Đề xuất: giữ. Nếu muốn đồng nhất số nhiều thì đổi thành `POST /api/admin/uploads/signatures` (201).
5. **Tên `PUT /api/auth/password`.** Phương án khác: `POST /api/auth/password-resets/{token}/completion`, nhưng token sẽ nằm trong đường dẫn và vào log truy cập. Đề xuất: giữ token trong body.
6. **Mã thành công 201 cho đăng nhập.** Đề xuất: đổi sang 201 vì phiên là tài nguyên mới. Nếu muốn giữ 200 cho đăng nhập thì sửa bảng route ở dòng 1 và 2, và bỏ dòng này khỏi quyết định 3.
