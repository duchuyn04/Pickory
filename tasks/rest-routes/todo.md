# Task List: rest-routes

Plan và quyết định thiết kế: `tasks/rest-routes/plan.md`. Quy ước chung: `spec/SPEC.md`, `CLAUDE.md`. Bảng route cũ và route mới nằm trong plan.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Mỗi task chạy các file E2E có đụng route đã đổi bằng `pnpm test:e2e`.
- Mỗi task đổi backend, test, frontend, E2E và spec của một nhóm route cùng lúc. App chạy được sau mỗi task.
- Mỗi task không giữ route cũ. Route cũ phải trả 404 hoặc 405 sau task.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Kích thước: XS 1 file, S 1-2, M 3-5. Số file không tính file test.

Điều kiện bắt đầu: người dùng đã chốt 6 Câu hỏi mở của `tasks/rest-routes/plan.md` ngày 2026-10-10, theo đúng đề xuất.

---

## Phase 1: Đổi route theo nhóm

### REST-01: Phiên đăng nhập
**Mô tả:** Đổi 3 route đăng nhập và đăng xuất sang tài nguyên `sessions`. Route đổi: dòng 1, 2, 3 của bảng trong plan.
**Tiêu chí nghiệm thu:**
- [x] `POST /api/auth/sessions` đăng nhập khách và trả 201. Body và cookie `pk_session` giống route cũ
- [x] `POST /api/admin/auth/sessions` đăng nhập nhân viên và trả 201. Phiên vẫn có hạn 12 giờ
- [x] `DELETE /api/auth/sessions/current` đăng xuất khách và nhân viên, trả 200, xóa cookie `pk_session`
- [x] Mã lỗi giữ nguyên: sai mật khẩu, tài khoản bị khóa, vượt giới hạn tần suất trả đúng mã và thông báo như trước
- [x] `POST /api/auth/login`, `POST /api/admin/auth/login` và `POST /api/auth/logout` không còn đăng ký. Test tích hợp kiểm 404 hoặc 405
- [x] Test `RequestOriginGuard` chặn `DELETE /api/auth/sessions/current` khi thiếu `Origin` và `Referer`
- [x] Frontend gọi route mới ở `customer/login-page.tsx`, `admin/login-page.tsx`, `shared/header.tsx`, `admin/admin-layout.tsx`
- [x] E2E đăng nhập khách, đăng nhập nhân viên và đăng xuất đạt
- [x] `spec/SPEC-identity.md` và `docs/testing/identity-manual-test-cases.md` (dòng 76) dùng route mới
**Kiểm tra:** test tích hợp `LoginTests`, `PermissionMatrixTests`; Vitest; E2E; lệnh `grep -rnE "auth/login|auth/logout" apps spec docs --include=*.cs --include=*.ts --include=*.tsx --include=*.md` ra 0 kết quả ngoài thư mục `bin` và `node_modules`
**Phụ thuộc:** người dùng chốt Câu hỏi mở · **Quy mô:** M
**File:** `Pickory.Modules.Identity/Features/{Login,Logout}/*Endpoint.cs`, `apps/frontend/src/features/identity/{customer,admin}/*`, `apps/frontend/src/shared/header.tsx`, `spec/SPEC-identity.md`

### REST-02: Vòng đời tài khoản
**Mô tả:** Đổi 5 route đăng ký, xác thực email, đặt lại mật khẩu và đổi mật khẩu. Route đổi: dòng 4 đến 8 của bảng trong plan.
**Tiêu chí nghiệm thu:**
- [x] `POST /api/auth/registrations` trả 201, body giống route cũ, đăng nhập luôn
- [x] `POST /api/auth/email-verifications` nhận token trong body và trả 200
- [x] `POST /api/auth/password-resets` trả 200 với cùng thông báo cho email có và không có tài khoản
- [x] `PUT /api/auth/password` nhận `token` và mật khẩu mới trong body, trả 200. Token dùng cho cả link quên mật khẩu và link mời nhân viên
- [x] `PUT /api/me/password` thay `POST /api/me/password`, yêu cầu đăng nhập, trả 200
- [x] Giới hạn tần suất giữ nguyên: đăng ký 5 lần mỗi giờ mỗi IP, quên mật khẩu 3 lần mỗi 15 phút mỗi email. Test hiện có đạt không cần sửa số
- [x] Token không xuất hiện trong đường dẫn. Test kiểm log truy cập không chứa token
- [x] Link trong email vẫn trỏ tới trang `/verify-email` và `/reset-password` của frontend. Không đổi
- [x] Mọi route cũ trong nhóm trả 404 hoặc 405
- [x] Frontend gọi route mới ở 5 trang `customer/*-page.tsx` và `components/change-password-form.tsx`. Vitest `reset-password-page.test.tsx` cập nhật
- [x] E2E đăng ký, xác thực email, quên mật khẩu, đặt lại mật khẩu và đổi mật khẩu đạt
- [x] `spec/SPEC-identity.md` (dòng 179, 180, 229-240) và `docs/testing/identity-manual-test-cases.md` (dòng 131, 163, 164) dùng route mới
**Kiểm tra:** test tích hợp `RegisterTests`, `EmailTokenTests`, `AccountTests`; Vitest; E2E
**Phụ thuộc:** REST-01 · **Quy mô:** M
**File:** `Pickory.Modules.Identity/Features/{Register,VerifyEmail,ForgotPassword,ResetPassword,ChangePassword}/*Endpoint.cs`, `apps/frontend/src/features/identity/customer/*`, `apps/frontend/src/features/identity/components/change-password-form.tsx`

### REST-03: Khóa và mở khóa tài khoản
**Mô tả:** Thay 4 route `lock` và `unlock` bằng 2 route `PATCH` với trường `locked`. Route đổi: dòng 9 đến 12 của bảng trong plan.
**Tiêu chí nghiệm thu:**
- [x] `PATCH /api/admin/customers/{id}` với `{ "locked": true }` khóa khách. Với `{ "locked": false }` mở khóa. Trả 200
- [x] `PATCH /api/admin/staff/{id}` với `{ "locked": true }` và `{ "locked": false }` hoạt động tương tự
- [x] Body thiếu `locked` hoặc sai kiểu trả 400 `VALIDATION_ERROR`
- [x] Quyền giữ nguyên: `customers:manage` cho khách, `staff:manage` cho nhân viên. `PermissionMatrixTests` có dòng `PATCH` mới và không còn dòng `lock`, `unlock`
- [x] Luật nghiệp vụ giữ nguyên: không tự khóa mình, không khóa quản trị viên cuối cùng. Test hiện có đạt
- [x] Audit log vẫn ghi `customer.lock`, `customer.unlock`, `staff.lock`, `staff.unlock` tùy giá trị `locked`, trong cùng transaction
- [x] Khóa xong, phiên đang mở của tài khoản bị thu hồi như trước
- [x] 4 route cũ trả 404 hoặc 405
- [x] Frontend gọi `PATCH` ở `admin/customers-page.tsx` và `admin/staff-page.tsx`. E2E khóa và mở khóa đạt
- [x] `spec/SPEC-identity.md` (dòng 251, 256) và `docs/testing/identity-manual-test-cases.md` (dòng 91) dùng route mới
**Kiểm tra:** test tích hợp `AdminCustomersTests`, `AdminStaffTests`, `PermissionMatrixTests`; Vitest; E2E
**Phụ thuộc:** REST-02 · **Quy mô:** M
**File:** `Pickory.Modules.Identity/Features/{AdminCustomers,AdminStaff}/*`, `apps/frontend/src/features/identity/admin/{customers-page,staff-page}.tsx`

### REST-04: Catalog
**Mô tả:** Đổi trạng thái sản phẩm sang `PUT` và điều chỉnh tồn kho sang `POST .../movements`. Route đổi: dòng 13 và 14 của bảng trong plan.
**Tiêu chí nghiệm thu:**
- [x] `PUT /api/admin/products/{id}/status` giữ body `{ "status": ... }`, trả 200 với sản phẩm đã đổi
- [x] Lỗi không thể công khai (`notPublishable`) giữ nguyên mã và danh sách thiếu
- [x] `POST /api/admin/skus/{id}/movements` giữ body `delta`, `reason`, `type`, trả 201 với kết quả điều chỉnh
- [x] Hai request điều chỉnh đồng thời vẫn không làm tồn kho âm. Test `ProductStockConcurrencyTests` đạt
- [x] Quyền giữ nguyên: `products:write` và `inventory:write`. `PermissionMatrixTests` cập nhật
- [x] `POST /api/admin/products/{id}/status` và `POST /api/admin/skus/{id}/adjust-stock` trả 404 hoặc 405
- [x] Frontend gọi route mới ở `product-form.tsx` và `adjust-stock-dialog.tsx`. Vitest `adjust-stock-dialog`, `product-form-page-status`, `sku-inventory-page` cập nhật
- [x] E2E `catalog-inventory.spec.ts` và `catalog-product-status.spec.ts` đạt
- [x] `spec/SPEC-catalog.md` (dòng 204, 210) dùng route mới
**Kiểm tra:** test tích hợp `ProductStatusTests`, `InventoryTests`, `ProductStockConcurrencyTests`, `PermissionMatrixTests`; Vitest; E2E
**Phụ thuộc:** REST-03 · **Quy mô:** S
**File:** `Pickory.Modules.Catalog/Features/{Products,Inventory}/*Endpoints.cs`, `apps/frontend/src/features/catalog/admin/{product-form.tsx,adjust-stock-dialog.tsx}`

### Checkpoint R1
- [ ] Mỗi nhóm route đã đổi, `dotnet test` và `pnpm test` xanh
- [ ] Review với người dùng

---

## Phase 2: Kiểm cuối

### REST-05: Kiểm không còn route cũ
**Mô tả:** Kiểm toàn repo không còn route cũ. Thêm test giữ luật cho các module sau. Chạy đủ bộ build, test và E2E.
**Tiêu chí nghiệm thu:**
- [ ] Lệnh dưới đây ra 0 kết quả:
  `grep -rnE "auth/(login|logout|register|verify-email|forgot-password|reset-password)|admin/auth/login|/(lock|unlock)\b|adjust-stock|POST /api/me/password" apps spec docs --include=*.cs --include=*.ts --include=*.tsx --include=*.md --exclude-dir=node_modules --exclude-dir=bin --exclude-dir=obj`
- [ ] Test mới liệt kê mọi route từ `EndpointDataSource` và từ chối đoạn đường dẫn nằm trong danh sách động từ (`login`, `logout`, `lock`, `unlock`, `adjust-stock`, `cancel`, `confirm`). Ngoại lệ ghi rõ: `google/start`, `google/callback`
- [ ] Phép thử đột biến: thêm một route `POST /api/admin/x/{id}/lock` thì test mới đỏ
- [ ] Bảng route trong `plan.md` khớp code: mỗi route mới có file và dòng thật
- [ ] `tasks/commerce/plan.md` (quyết định 17) và `spec/SPEC-shipping.md` không mâu thuẫn với luật đặt tên của plan này
- [ ] `tasks/activity-tracking/{plan,todo}.md` và `tasks/personalized-content/plan.md`: route cũ nếu còn thì đổi sang route mới
- [ ] `dotnet build`, `dotnet test` sạch
- [ ] `pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e` sạch
- [ ] Mục "Câu hỏi mở" của `plan.md` đã có quyết định ghi lại
**Kiểm tra:** lệnh grep, `dotnet test`, bộ lệnh frontend
**Phụ thuộc:** REST-01 đến REST-04 · **Quy mô:** S
**File:** `apps/backend/tests/Pickory.ArchitectureTests/` hoặc `apps/backend/tests/Pickory.IntegrationTests/Api/`, `tasks/rest-routes/plan.md`

## Checkpoint cuối
- [ ] Lệnh grep route cũ trong `apps`, `spec`, `docs` ra 0 kết quả
- [ ] Bộ lệnh frontend và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `commerce`
