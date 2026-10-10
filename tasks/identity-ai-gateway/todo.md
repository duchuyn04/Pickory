# Task List: identity + ai-gateway

Plan và quyết định thiết kế: `tasks/identity-ai-gateway/plan.md`. Spec: `spec/SPEC-identity.md`, `spec/SPEC-ai-gateway.md`.
Lịch 6 tuần, 6 sprint 1 tuần (chi tiết ở `tasks/identity-ai-gateway/plan.md`): Sprint 1: T01 đến T05, AI-00, AI-01. Sprint 2: ID-01 đến ID-04, AI-02, AI-03. Sprint 3: UI-05, ID-05, UI-01, ID-06, AI-04 đến AI-06. Sprint 4: ID-07, UI-02, ID-08, UI-03, AI-07, AI-08. Sprint 5: ID-09, ID-10, UI-04, ID-11, UI-06, AI-09. Sprint 6: ID-12, ID-13, UI-07, ID-14, UI-08, E2E-01.

Quy ước: mỗi task phải qua `pnpm lint && pnpm typecheck && pnpm test` trước khi commit. Lệnh test tập trung: `pnpm --filter @pickory/backend test -- <đường dẫn>`. Kích thước: XS 1 file, S 1-2, M 3-5.

---

## Phase 0: Nền tảng chung

### T01: Khởi tạo pnpm workspace và tooling
**Mô tả:** Dựng monorepo `apps/backend`, `apps/frontend`, `packages/shared`, TypeScript strict, ESLint, Prettier, Vitest. Chưa có logic nghiệp vụ.
**Tiêu chí nghiệm thu:**
- [x] `pnpm install`, `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build` chạy sạch trên cây rỗng
- [x] Có `.env.example` (chưa có secret), `.gitignore` đã đủ
- [x] Vitest cấu hình ngưỡng coverage 80% dòng cho `**/service.ts` và các file logic
**Kiểm tra:** chạy 5 lệnh trên; thêm 1 test mẫu để Vitest chạy được
**Phụ thuộc:** không · **File:** `package.json`, `pnpm-workspace.yaml`, `tsconfig.base.json`, `eslint.config.js`, `vitest.config.ts` · **Quy mô:** M

### T02: API skeleton (errors, logger, config, health)
**Mô tả:** Express app, `AppError`, middleware lỗi (`{ error: { code, message } }`, ZodError thành 400, message tiếng Việt), logger chung, đọc config bằng Zod, event bus có kiểu `shared/events.ts`, `GET /api/health`.
**Tiêu chí nghiệm thu:**
- [x] `AppError` đúng mẫu trong `SPEC.md`; ZodError trả 400; lỗi lạ trả 500 không lộ stack
- [x] Config thiếu biến bắt buộc thì app không khởi động và báo rõ biến nào
- [x] Logger không in được giá trị của key có tên `password`, `token`, `apiKey`
- [x] Event bus: handler ném lỗi thì được log, không ném ra nơi phát
**Kiểm tra:** Supertest cho `/api/health` và đường lỗi; unit test event bus và logger redact
**Phụ thuộc:** T01 · **File:** `apps/backend/src/{app,server}.ts`, `shared/{errors,logger,config,events}.ts` · **Quy mô:** M

### T03: PostgreSQL, Prisma và test DB
**Mô tả:** Docker Compose cho PostgreSQL + pgvector, Prisma, `pnpm db:migrate`, `pnpm db:seed` (khung rỗng), DB test riêng, hạ tầng test integration.
**Tiêu chí nghiệm thu:**
- [x] `docker compose up -d db` rồi `pnpm db:migrate` chạy được; extension `vector` được bật trong migration đầu
- [x] Xác nhận bản Prisma khóa hỗ trợ schema nhiều file; nếu có thì `prisma/schema/` tách theo module, nếu không thì ghi quy tắc rebase vào `plan.md`
- [x] Test integration dùng DB riêng, migrate trước bộ test, dọn bảng giữa các test
- [x] Có 1 test integration mẫu chạy qua DB thật
**Kiểm tra:** `pnpm test` có test integration pass; DB dev và DB test tách biệt
**Phụ thuộc:** T02 · **File:** `docker-compose.yml`, `apps/backend/prisma/schema/base.prisma`, `apps/backend/src/shared/db.ts`, `apps/backend/test/setup.ts` · **Quy mô:** M

### T04: Web skeleton
**Mô tả:** React + Vite, React Router, TanStack Query, client gọi API, proxy dev tới port 3000, trang placeholder.
**Tiêu chí nghiệm thu:**
- [x] `pnpm dev` chạy web 5173 và api 3000 cùng lúc, web gọi được `/api/health`
- [x] `pnpm build` build được cả web
**Kiểm tra:** mở trình duyệt xem trang hiển thị trạng thái health
**Phụ thuộc:** T01, T02 · **File:** `apps/frontend/{vite.config.ts,index.html}`, `src/{main,app}.tsx`, `src/shared/api.ts` · **Quy mô:** S

### T05: ESLint chặn import sai ranh giới module
**Mô tả:** Quy tắc `no-restricted-imports`: ngoài `ai-gateway/providers/` cấm `ai`, `@ai-sdk/*`, `openai`; ngoài `knowledge-base/ingest/` cấm `@langchain/*`; module chỉ import `modules/<id>/index.ts` của module khác.
**Tiêu chí nghiệm thu:**
- [x] Có fixture import sai cho từng quy tắc và test (script) khẳng định ESLint báo lỗi đúng
- [x] Code hợp lệ không bị báo
**Kiểm tra:** `pnpm lint` sạch; chạy test fixture thấy từng import sai bị chặn
**Phụ thuộc:** T01 · **File:** `eslint.config.js`, `tools/lint-fixtures/*`, `tools/test/unit/lint-rules.test.ts` · **Quy mô:** S

### Checkpoint 0: Nền tảng
- [x] `pnpm lint && pnpm typecheck && pnpm test && pnpm build` sạch
- [x] `pnpm dev` chạy cả web và api, DB test chạy được
- [x] Review với người dùng, chốt Open Questions 1 và 2 trong `plan.md` trước khi sang Phase 1

---

## Track A: identity

### ID-01: Danh mục quyền, RBAC schema và seed role
**Mô tả:** `identity/permissions.ts` (danh mục đầy đủ theo spec, kiểu `Permission`), bảng `users`, `roles`, `role_permissions`, `user_roles`, hàm hợp quyền nhiều role, seed 4 role với quyền mặc định. `admin` có mọi quyền ngầm định. Kiểm tra lúc khởi động bỏ quyền không còn trong danh mục và ghi cảnh báo. Cập nhật `SPEC-identity.md` nếu duyệt lệch Decision 1.
**Tiêu chí nghiệm thu:**
- [x] Sau seed có đủ `customer`, `admin`, `sales`, `support`, quyền `sales`/`support` đúng bảng mặc định
- [x] Thêm 1 quyền mới vào danh mục thì `admin` có ngay mà không cần seed lại
- [x] Hợp quyền của nhân viên giữ `sales` + `support` đúng (unit test)
- [x] Quyền lạ trong `role_permissions` bị bỏ qua và có log cảnh báo
**Kiểm tra:** unit test hợp quyền; integration test seed so với bảng mặc định
**Phụ thuộc:** T03 · **File:** `modules/identity/{permissions,service,repository}.ts`, `prisma/schema/identity.prisma`, `prisma/seed.ts` · **Quy mô:** M

### ID-02: Actor khách vãng lai, kiểm tra Origin, GET /api/me
**Mô tả:** `actorMiddleware` đặt cookie `pk_anon` (httpOnly, Secure, SameSite=Lax, 1 năm), gắn `req.actor` kiểu `guest`, chặn request ghi có `Origin` lạ, `GET /api/me` trả actor (consent mặc định tắt).
**Tiêu chí nghiệm thu:**
- [x] Lần đầu truy cập nhận cookie `pk_anon` UUID v4 với đúng thuộc tính; lần sau giữ nguyên
- [x] POST/PUT/PATCH/DELETE với `Origin` lạ nhận 403; GET không bị ảnh hưởng
- [x] `GET /api/me` của khách trả `kind: 'guest'`, `permissions` rỗng, `consent.personalization = false`
**Kiểm tra:** Supertest các trường hợp trên
**Phụ thuộc:** ID-01 · **File:** `modules/identity/{middleware,routes,index}.ts`, `shared/privacy.ts` (chưa cần) · **Quy mô:** S

### ID-03: Đăng nhập nhân viên và phiên
**Mô tả:** Bảng `sessions` (lưu hash SHA-256), băm Argon2id, `POST /api/admin/auth/login`, `POST /api/auth/logout`, phiên nhân viên 12 giờ không gia hạn, cookie `pk_session`, `actor` kiểu `staff` nạp quyền mỗi request bằng 1 truy vấn join. Lệnh `pnpm admin:create -- --email`.
**Tiêu chí nghiệm thu:**
- [x] Mật khẩu lưu Argon2id; DB không chứa token gốc; so sánh token constant-time
- [x] Sai email và sai mật khẩu cho cùng thông báo lỗi
- [x] Đổi quyền của role thì request kế tiếp của nhân viên thấy ngay (không cache)
- [x] `admin:create` tạo được admin đầu tiên, chạy lại với email đó không tạo trùng
**Kiểm tra:** unit (băm, hash token); integration đăng nhập, đăng xuất, phiên hết hạn
**Phụ thuộc:** ID-02 · **File:** `modules/identity/{auth,sessions,password}.ts`, `prisma/schema/identity.prisma`, `scripts/admin-create.ts` · **Quy mô:** M

### ID-04: requirePermission, recordAudit và API audit log
**Mô tả:** `requireAuth`, `requireStaff`, `requirePermission` (401 chưa đăng nhập, 403 thiếu quyền, nhiều quyền thì cần đủ), `recordAudit` ghi trong cùng transaction, `GET /api/admin/audit-logs` có lọc. Dựng khung test ma trận quyền chạy theo bảng dữ liệu (endpoint, quyền cần có) để module sau thêm dòng.
**Tiêu chí nghiệm thu:**
- [x] 401 và 403 đúng; nhiều quyền yêu cầu đủ tất cả
- [x] Audit log chỉ thêm, không có route sửa hoặc xóa; `before`/`after` loại bỏ trường mật khẩu và token
- [x] Ma trận quyền chạy được với endpoint hiện có, mỗi role chỉ gọi được endpoint được phép
- [x] `index.ts` xuất đúng interface public theo spec
**Kiểm tra:** integration các trường hợp 401/403, kiểm tra ma trận quyền, unit audit log
**Phụ thuộc:** ID-03 · **File:** `modules/identity/{middleware,audit,routes}.ts` · **Quy mô:** M

### UI-05: Khung back-office tối thiểu và đăng nhập nhân viên
**Mô tả:** `features/identity/admin/` trang `/admin/login`, layout back-office, `useActor()`, `useCan()`, menu ẩn theo quyền, chuyển hướng khi chưa đăng nhập.
**Tiêu chí nghiệm thu:**
- [x] Nhân viên đăng nhập được, menu chỉ hiện mục có quyền; khách hoặc người chưa đăng nhập vào `/admin` bị chuyển về đăng nhập
- [x] Ẩn nút chỉ để tiện dùng, backend vẫn kiểm tra
**Kiểm tra:** chạy `pnpm dev`, đăng nhập bằng admin dev, kiểm tra thủ công; test hook `useCan`
**Phụ thuộc:** ID-04, T04 · **File:** `apps/frontend/src/features/identity/{use-actor,admin/login-page,admin/admin-layout}.tsx` · **Quy mô:** M

### ID-05: Đăng ký, đăng nhập khách và UserLinkedEvent
**Mô tả:** `POST /api/auth/register`, `POST /api/auth/login`, role `customer`, phiên khách 30 ngày (gia hạn khi còn dưới 7 ngày), phát `UserLinkedEvent { anonymousId, userId }` khi đăng ký hoặc đăng nhập khách. Email thuộc tài khoản nhân viên thì không đăng ký làm khách được.
**Tiêu chí nghiệm thu:**
- [x] Đăng ký xong đăng nhập luôn; mật khẩu tối thiểu 8 ký tự
- [x] Mỗi lần đăng nhập khách phát đúng 1 `UserLinkedEvent`; nhân viên đăng nhập không phát
- [x] Handler sự kiện lỗi không làm đăng nhập thất bại
- [x] Gia hạn phiên đúng ngưỡng 7 ngày
**Kiểm tra:** integration các nhánh trên, unit logic gia hạn
**Phụ thuộc:** ID-04 · **File:** `modules/identity/{auth,events,routes,schemas}.ts` · **Quy mô:** M

### UI-01: Trang đăng ký và đăng nhập khách
**Mô tả:** `features/identity/` trang đăng ký, đăng nhập, header hiển thị trạng thái đăng nhập, đăng xuất.
**Tiêu chí nghiệm thu:**
- [x] Đăng ký, đăng xuất, đăng nhập lại chạy được trên UI; lỗi API hiện thông báo tiếng Việt
**Kiểm tra:** kiểm tra thủ công trên `pnpm dev`
**Phụ thuộc:** ID-05 · **File:** `features/identity/{register-page,login-page}.tsx`, `shared/header.tsx` · **Quy mô:** S

### ID-06: Chống dò mật khẩu
**Mô tả:** Bộ đếm trong bộ nhớ theo cặp (email, IP): sai 5 lần trong 15 phút thì khóa 15 phút, trả 429. Áp cho cả đăng nhập khách và nhân viên.
**Tiêu chí nghiệm thu:**
- [x] Lần sai thứ 5 vẫn 401; lần thứ 6 nhận 429 kể cả mật khẩu đúng
- [x] Hết 15 phút thì thử lại được; cặp (email, IP) khác không bị ảnh hưởng
**Kiểm tra:** unit với fake timer; integration qua endpoint
**Phụ thuộc:** ID-05 · **File:** `modules/identity/login-throttle.ts` (+ test), `auth.ts` · **Quy mô:** S

### ID-07: Email token, xác thực email, quên và đặt lại mật khẩu
**Mô tả:** Bảng `email_tokens` (hash, purpose, expiresAt, usedAt), port `EmailSender` (in-memory cho test, file outbox cho dev), `verify-email`, `forgot-password` (luôn cùng response), `reset-password` (xóa mọi phiên). Gửi email xác thực sau đăng ký.
**Tiêu chí nghiệm thu:**
- [x] Link đặt lại hết hạn sau 30 phút và dùng 1 lần; link xác thực hạn 24 giờ
- [x] `forgot-password` trả cùng response và cùng độ trễ xấp xỉ dù email có tồn tại hay không
- [x] Đặt lại mật khẩu xong, mọi phiên cũ của người đó bị xóa
- [x] DB không lưu token gốc
**Kiểm tra:** integration với fake timer cho hạn dùng; dùng lại token nhận lỗi
**Phụ thuộc:** ID-05 · **File:** `modules/identity/{email-tokens,email-sender,auth}.ts`, `prisma/schema/identity.prisma` · **Quy mô:** M

### UI-02: Trang quên và đặt lại mật khẩu, xác thực email
**Mô tả:** Trang quên mật khẩu, trang đặt lại (dùng cả cho lời mời nhân viên), trang xác thực email.
**Tiêu chí nghiệm thu:**
- [x] Mở link trong file outbox dev đặt được mật khẩu mới và đăng nhập lại được
**Kiểm tra:** thủ công trên `pnpm dev`
**Phụ thuộc:** ID-07 · **File:** `features/identity/{forgot-password-page,reset-password-page,verify-email-page}.tsx` · **Quy mô:** S

### ID-08: Đồng ý thu thập dữ liệu (consent)
**Mô tả:** Bảng `consent_records`, `PUT /api/me/consent` (công khai, theo anonymousId hoặc userId), actor nạp lựa chọn mới nhất, gộp lấy lựa chọn mới nhất giữa vãng lai và tài khoản khi `UserLinkedEvent`.
**Tiêu chí nghiệm thu:**
- [x] Mỗi lần đổi lưu 1 bản ghi mới kèm phiên bản chính sách; `necessary` không tắt được
- [x] Khách chưa chọn thì `consent.personalization = false`
- [x] Sau khi đăng nhập, consent áp dụng là bản ghi mới nhất giữa hai bên
**Kiểm tra:** unit chọn bản mới nhất; integration `PUT` rồi `GET /api/me`
**Phụ thuộc:** ID-05 · **File:** `modules/identity/{consent,routes}.ts`, `prisma/schema/identity.prisma` · **Quy mô:** M

### UI-03: Banner đồng ý và trang quyền riêng tư
**Mô tả:** Banner lần đầu truy cập với "Đồng ý" và "Chỉ cần thiết" ngang nhau, link "Tùy chỉnh"; đổi lựa chọn bất cứ lúc nào.
**Tiêu chí nghiệm thu:**
- [x] Hai nút cùng kích thước và độ nổi bật; chọn xong banner ẩn và lựa chọn lưu qua API
- [x] Đổi lựa chọn sau đó được ghi nhận
**Kiểm tra:** thủ công; kiểm tra bản ghi `consent_records`
**Phụ thuộc:** ID-08 · **File:** `features/identity/{consent-banner,privacy-settings}.tsx` · **Quy mô:** S

### ID-09: Đăng nhập Google (OAuth 2.0 + PKCE)
**Mô tả:** `GET /api/auth/google/start`, `/callback`, bảng `oauth_accounts`, kiểm tra `state`, liên kết khi email Google đã xác thực trùng tài khoản khách; email chưa xác thực thì không tự liên kết. Test bằng phản hồi giả lập.
**Tiêu chí nghiệm thu:**
- [x] `state` sai bị từ chối; dùng PKCE
- [x] Email đã xác thực trùng thì liên kết; chưa xác thực thì không
- [x] Email thuộc nhân viên thì từ chối
- [x] Phát `UserLinkedEvent` như đăng nhập thường
**Kiểm tra:** integration với `msw` giả lập Google
**Phụ thuộc:** ID-07 · **File:** `modules/identity/{google-oauth,routes}.ts`, `prisma/schema/identity.prisma` · **Quy mô:** M

### ID-10: Hồ sơ, đổi mật khẩu, xóa tài khoản khách
**Mô tả:** `PATCH /api/me`, `POST /api/me/password` (xóa mọi phiên), `DELETE /api/me` (xác nhận lại bằng mật khẩu, hoặc đăng nhập Google trong 5 phút gần nhất), ẩn danh email và tên, đặt `deletedAt`, phát `UserDeletedEvent`.
**Tiêu chí nghiệm thu:**
- [x] Xóa xong không đăng nhập lại được; email gốc không còn trong DB (kể cả `email_tokens`, `oauth_accounts`)
- [x] Bản ghi `users` giữ lại với `deletedAt`
- [x] Phát đúng 1 `UserDeletedEvent`; thiếu xác nhận lại thì từ chối
- [x] Đổi mật khẩu xóa mọi phiên
**Kiểm tra:** integration; kiểm tra DB bằng truy vấn tìm email gốc
**Phụ thuộc:** ID-07 (và ID-09 cho nhánh Google) · **File:** `modules/identity/{account,routes,events}.ts` · **Quy mô:** M

### UI-04: Trang tài khoản khách
**Mô tả:** Thông tin cá nhân, đổi mật khẩu, quyền riêng tư (dùng lại từ UI-03), xóa tài khoản.
**Tiêu chí nghiệm thu:**
- [x] Sửa tên, SĐT, đổi mật khẩu, xóa tài khoản chạy được trên UI, có bước xác nhận trước khi xóa
**Kiểm tra:** thủ công trên `pnpm dev`
**Phụ thuộc:** ID-10, UI-03 · **File:** `features/identity/{account-page,change-password-form,delete-account-dialog}.tsx` · **Quy mô:** S

### ID-11: Quản lý role
**Mô tả:** `GET /api/admin/roles`, `GET /api/admin/permissions`, `POST/PATCH/DELETE /api/admin/roles[/:id]` (quyền `roles:manage`), ghi audit. Role hệ thống (`admin`, `customer`) không sửa, không xóa; không xóa role đang có người giữ.
**Tiêu chí nghiệm thu:**
- [x] Tạo role "Quản lý kho", tick quyền, lưu thành công và có đúng 1 dòng audit (trước và sau)
- [x] Sửa, xóa role hệ thống bị từ chối; xóa role đang có người giữ bị từ chối
- [x] Quyền gửi lên không thuộc danh mục thì 400
- [x] Thêm các endpoint này vào test ma trận quyền
**Kiểm tra:** integration; chạy lại ma trận quyền
**Phụ thuộc:** ID-04 · **File:** `modules/identity/{roles,routes,schemas}.ts` (+ test) · **Quy mô:** M

### UI-06: Màn quản lý role
**Mô tả:** Danh sách role, form tạo và sửa với bảng tick quyền theo nhóm.
**Tiêu chí nghiệm thu:**
- [x] Tạo, sửa, xóa role trên UI; role hệ thống hiển thị khóa; lỗi quy tắc an toàn hiện thông báo rõ
**Kiểm tra:** thủ công; menu chỉ hiện với `roles:manage`
**Phụ thuộc:** ID-11, UI-05 · **File:** `features/identity/admin/{roles-page,role-form,permission-grid}.tsx` · **Quy mô:** M

### ID-12: Mời nhân viên
**Mô tả:** `GET /api/admin/staff`, `POST /api/admin/staff/invitations` (email, tên, role), gửi link đặt mật khẩu hạn 72 giờ dùng 1 lần qua `EmailSender`, nhân viên đặt mật khẩu qua `reset-password`.
**Tiêu chí nghiệm thu:**
- [x] Link mời hết hạn sau 72 giờ, dùng 1 lần
- [x] Mời email đã thuộc khách bị từ chối; không gán được `customer` cùng role nhân viên
- [x] Mời tạo đúng 1 dòng audit; nhân viên đặt mật khẩu xong đăng nhập được
**Kiểm tra:** integration với fake timer
**Phụ thuộc:** ID-07, ID-11 · **File:** `modules/identity/{staff,routes}.ts` (+ test) · **Quy mô:** M

### ID-13: Gán role, khóa và mở khóa nhân viên
**Mô tả:** `PUT /api/admin/staff/:id/roles`, `POST .../lock`, `.../unlock`. Quy tắc: không khóa hoặc gỡ quyền admin cuối cùng, không tự sửa role của mình. Khóa hoặc gỡ hết role thì xóa mọi phiên ngay.
**Tiêu chí nghiệm thu:**
- [x] Nhân viên giữ `sales` + `support` có hợp quyền (integration)
- [x] Khóa hoặc gỡ hết role thì request tiếp theo bằng phiên cũ nhận 401
- [x] Không khóa được admin cuối cùng; không tự sửa role mình
- [x] Mỗi thao tác đúng 1 dòng audit; bổ sung ma trận quyền
**Kiểm tra:** integration các quy tắc an toàn
**Phụ thuộc:** ID-12 · **File:** `modules/identity/{staff,sessions,routes}.ts` (+ test) · **Quy mô:** M

### UI-07: Màn nhân viên
**Mô tả:** Danh sách nhân viên, mời, gán role, khóa và mở khóa.
**Tiêu chí nghiệm thu:**
- [x] Mời, gán role, khóa chạy được trên UI; không hiện hành động khi thiếu `staff:manage`
**Kiểm tra:** thủ công
**Phụ thuộc:** ID-13, UI-05 · **File:** `features/identity/admin/{staff-page,invite-dialog,assign-roles-dialog}.tsx` · **Quy mô:** M

### ID-14: Tra cứu và khóa khách
**Mô tả:** `GET /api/admin/customers[/:id]` (`customers:read`), `POST .../lock|unlock` (`customers:manage`), khóa xóa mọi phiên, ghi audit.
**Tiêu chí nghiệm thu:**
- [x] Không trả `passwordHash`; khách bị khóa không đăng nhập được và phiên cũ mất hiệu lực
- [x] `sales` và `support` xem được nhưng khóa bị 403 (ma trận quyền)
**Kiểm tra:** integration
**Phụ thuộc:** ID-04, ID-05 · **File:** `modules/identity/{customers,routes}.ts` (+ test) · **Quy mô:** S

### UI-08: Màn khách hàng và audit log
**Mô tả:** Tra cứu khách (khóa, mở khóa), xem audit log lọc theo người, hành động, thời gian.
**Tiêu chí nghiệm thu:**
- [x] Lọc audit log hoạt động; chỉ hiện khi có `audit-logs:read`
**Kiểm tra:** thủ công
**Phụ thuộc:** ID-14, ID-04, UI-05 · **File:** `features/identity/admin/{customers-page,audit-log-page}.tsx` · **Quy mô:** M

### ID-15: Follow-up sau review Track A (không chặn merge)
**Mô tả:** Hai việc còn lại sau review Track A lần 3. (1) Trang admin đang nuốt lỗi tải dữ liệu: 401/403 hiện như danh sách rỗng, phiên nhân viên hết hạn mà actor cache không biết. (2) Người giữ `staff:manage` không phải admin vẫn khóa, mở khóa hoặc đổi role được nhân viên có nhiều quyền hơn mình (thao tác ngang hàng, chưa phải leo thang, nhưng nên siết).
**Tiêu chí nghiệm thu:**
- [x] `QueryClient` có `QueryCache` và `MutationCache` với `onError`: gặp lỗi `status === 401` thì invalidate `['actor']` để layout chuyển về trang đăng nhập
- [x] Các trang `staff-page`, `roles-page`, `customers-page`, `audit-log-page` hiển thị `error.message` (có `role="alert"`) khi tải thất bại, không hiện thông báo "không có dữ liệu"
- [x] `lockStaff`, `unlockStaff`, `assignStaffRoles`: actor không phải admin thì hợp quyền hiện tại của target phải nằm trong `actor.permissions`, nếu không trả 403 `FORBIDDEN`; admin không bị ảnh hưởng
- [x] Integration test: người có `staff:manage` khóa và đổi role của nhân viên giữ quyền mình không có nhận 403; khóa nhân viên có quyền là tập con vẫn 200
**Kiểm tra:** `pnpm lint && pnpm typecheck && pnpm test`; thủ công trên `pnpm dev`: xóa phiên trong DB rồi bấm sang trang admin khác thì bị chuyển về `/admin/login`
**Phụ thuộc:** UI-07, UI-08, ID-13 · **File:** `apps/frontend/src/main.tsx`, `features/identity/admin/{staff-page,roles-page,customers-page,audit-log-page}.tsx`, `modules/identity/{staff,routes}.ts`, `staff.int.test.ts` · **Quy mô:** S

### ID-16: Sửa lỗi frontend từ đợt test thủ công identity
**Mô tả:** Sửa 10 lỗi giao diện tìm thấy khi chạy `docs/testing/identity-manual-test-cases.md` (run `20261004-mutnnjow`). Backend và bảo mật đã đạt, các lỗi đều ở frontend. Hai mục đầu là P1, phải xong trước khi coi UI-01 và UI-02 là hoàn tất.
**Tiêu chí nghiệm thu:**
- [x] (P1, MT-ID-053..055) `verify-email-page.tsx` không kẹt "Đang xác thực..." khi chạy StrictMode: link hợp lệ báo thành công, link đã dùng hoặc hết hạn báo lỗi. Có test component chạy trong `<StrictMode>`
- [x] (P1, MT-ID-005, 007, 095) Sau đăng nhập hoặc đăng ký, người dùng thấy header có trạng thái đăng nhập, nút đăng xuất và link "Quyền riêng tư"; header có trên mọi route khách, kể cả `/`
- [x] (P2, MT-ID-052) Đặt lại mật khẩu thành công thì xóa actor khỏi cache, bấm "Đăng nhập" ở lại `/login` (hoặc `/admin/login` với nhân viên)
- [x] (P2, MT-ID-021) Query không retry với lỗi 4xx, nên nhân viên bị khóa được chuyển về `/admin/login` trong vòng 2 giây khi điều hướng
- [x] (P2, MT-ID-082, 099) Form identity dùng `noValidate` và tự kiểm tra, thông báo lỗi email và mật khẩu bằng tiếng Việt, không phụ thuộc ngôn ngữ trình duyệt
- [x] (P3, MT-ID-092) Mở `/reset-password` không có token thì hiện ngay thông báo link không hợp lệ, không hiện form
- [x] (P3, MT-ID-098) `/login` và `/register` không cuộn ngang ở viewport 375px
- [x] (P3, MT-ID-096) Dialog mời nhân viên, gán role, tạo/sửa role: focus vào dialog khi mở, Esc đóng, focus trả về nút đã mở
- [x] (P3, MT-ID-097) Hai ô ngày ở bộ lọc audit có nhãn truy cập được
- [x] (P3, MT-ID-100) Trang reset và verify xóa `token` khỏi thanh địa chỉ sau khi đọc (`navigate(pathname, { replace: true })`)
**Kiểm tra:** `pnpm lint && pnpm typecheck && pnpm test`; chạy lại bằng Playwright mọi case FAILED trong `docs/testing/identity-manual-test-cases.md` và 3 case đã sửa kịch bản (MT-ID-045, 067, 078), cập nhật trạng thái và bảng tổng hợp
**Kết quả:** 2026-10-04, run `20261004-mutpl1w0`: 18/18 case chạy lại PASS, tổng hợp 96 PASS / 0 FAILED / 4 UNKNOWN. `pnpm lint && pnpm typecheck && pnpm test` đạt 201 test trong 32 file.
**Dependency:** jsdom (devDependency, test component web) được người dùng duyệt ngày 2026-10-04 theo mục Ask first của SPEC.md.
**Phụ thuộc:** ID-15, UI-01, UI-02, UI-03, UI-06, UI-07, UI-08 · **File:** `apps/frontend/src/main.tsx`, `apps/frontend/src/app.tsx`, `shared/header.tsx`, `features/identity/{verify-email-page,reset-password-page,login-page,register-page}.tsx`, `features/identity/admin/{invite-dialog,assign-roles-dialog,role-form,audit-log-page}.tsx` · **Quy mô:** M

### ID-17: Chống spam đăng ký và chặn chiếm tài khoản trước khi liên kết Google
**Mô tả:** Hai lỗ hổng tìm thấy sau review. (1) `register` và `forgot-password` không giới hạn tần suất: bot có thể tạo tài khoản rác, dùng Pickory gửi email hàng loạt tới địa chỉ bất kỳ và làm quá tải CPU (Argon2). (2) Chiếm tài khoản trước (pre-hijacking): kẻ xấu đăng ký trước bằng email nạn nhân, nạn nhân sau đó đăng nhập Google, hệ thống tự liên kết vào tài khoản có mật khẩu do kẻ xấu đặt, kẻ xấu vẫn vào được. Đổi hành vi nên cập nhật `SPEC-identity.md` trước.
**Tiêu chí nghiệm thu:**
- [x] `SPEC-identity.md` có mục chống spam (ngưỡng bên dưới, honeypot, trust proxy) và quy tắc liên kết Google với tài khoản chưa xác thực email
- [x] `POST /api/auth/register` giới hạn 5 lần / giờ / IP; vượt ngưỡng trả 429 `TOO_MANY_REQUESTS`, thông báo tiếng Việt, không tạo user, không gửi email
- [x] `POST /api/auth/forgot-password` giới hạn 3 lần / 15 phút / email và 10 lần / giờ / IP; vượt ngưỡng vẫn trả đúng response chung như bình thường nhưng không tạo token, không gửi email (không lộ email có tồn tại)
- [x] Bộ đếm viết tại chỗ theo mẫu `login-throttle.ts` (trong bộ nhớ, có tham số `now` cho test), không thêm dependency
- [x] Form đăng ký có ô honeypot ẩn (không hiện với người dùng và trình đọc màn hình, `tabIndex=-1`, `autoComplete="off"`); request có giá trị honeypot thì trả response giống thành công giả hoặc 400 chung, không tạo user, không gửi email
- [x] Cấu hình `TRUST_PROXY` trong `shared/config.ts` (mặc định tắt), bật thì `app.set('trust proxy', ...)` để `req.ip` là IP thật khi chạy sau proxy
- [x] Liên kết Google vào tài khoản khách có `emailVerified = false`: trong cùng transaction xóa `passwordHash`, xóa mọi phiên cũ, vô hiệu token reset/verify còn hạn, đặt `emailVerified = true` rồi mới tạo phiên mới; tài khoản đã xác thực email giữ nguyên hành vi cũ
- [x] Unit test bộ đếm (fake timer: vượt ngưỡng, hết cửa sổ thì mở lại, khóa khác không ảnh hưởng); integration test: đăng ký lần 6 cùng IP nhận 429, forgot-password lần 4 cùng email không gửi email nhưng response giống hệt, honeypot không tạo user, kịch bản pre-hijacking (đăng ký bằng mật khẩu, chưa xác thực, rồi Google cùng email) thì mật khẩu cũ không còn đăng nhập được và phiên cũ bị xóa
**Kiểm tra:** `pnpm lint && pnpm typecheck && pnpm test` đạt 214 test trong 34 file; Playwright trên `pnpm dev`: đăng ký lần 6 thấy thông báo 429 tiếng Việt trên UI, user 6 không được tạo, ảnh `.playwright-mcp/identity-mt/ID-17-register-429.png`, đã dọn sạch dữ liệu test
**Phụ thuộc:** ID-05, ID-06, ID-07, ID-09 · **File:** `spec/SPEC-identity.md`, `modules/identity/{rate-limit,auth,email-tokens,google-oauth,routes}.ts`, `shared/config.ts`, `app.ts`, `features/identity/register-page.tsx` (+ test) · **Quy mô:** M

---

## Track B: ai-gateway

Xác minh cục bộ đã chạy qua SDK HTTP mock, PostgreSQL và UI thật; Claude review qua Herdr và sửa findings. API provider thật còn bị chặn do thiếu `LLM_API_KEY`, `LLM_MODEL`, `EMBEDDING_API_KEY`; giữ nguyên checkbox live chưa đạt. Chi tiết bằng chứng và giới hạn ngân sách postpaid: ADR 0002.


### AI-00: Spike xác minh DeepSeek và AI SDK
**Mô tả:** Không viết code sản phẩm. Đối chiếu tài liệu chính thức DeepSeek và OpenAI, rồi chạy script thử (nằm ngoài `src/`, không commit key): id model, giới hạn context, JSON mode, tool calling, đơn giá. Với bản `ai` và `@ai-sdk/*` sẽ khóa: kiểm tra còn `generateText`, `streamText`, `embedMany`, cách làm structured output (`generateObject` hoặc API thay thế), cách chạy nhiều bước (`maxSteps` hoặc `stopWhen`), hook giữa các bước (`prepareStep`, `onStepFinish`).
**Tiêu chí nghiệm thu:**
- [ ] Ghi kết quả vào `docs/adr/0002-ai-gateway-provider-findings.md`, gồm tên API thật, điểm lệch so với spec, và quyết định "dùng hook của SDK" hay "gateway tự chạy từng bước"
- [x] Cập nhật `SPEC-ai-gateway.md` nếu có điểm lệch (id model, tên hàm)
- [x] Danh sách dependency xin duyệt khớp với bản thực tế
**Kiểm tra:** script chạy thật được 1 lần chat, 1 JSON, 1 tool call, 1 embed (cần API key do người dùng cung cấp)
**Phụ thuộc:** T03, Open Questions 1 và 3 · **File:** `docs/adr/0002-*.md`, `spec/SPEC-ai-gateway.md` · **Quy mô:** S

### AI-01: redactPII
**Mô tả:** `shared/privacy.ts` thay SĐT Việt Nam, email, số thẻ và số tài khoản ngân hàng bằng `[đã ẩn]`.
**Tiêu chí nghiệm thu:**
- [x] `0912 345 678`, `+84 912345678`, `091.234.5678`, `a@b.vn` đều bị thay; số thẻ 16 chữ số và số tài khoản dài bị thay
- [x] Giá VND thường gặp (`25.990.000`, `25990000đ`, `5 triệu`) và mã model (`RTX 4070`, `SM-S918B`) không bị ẩn nhầm
- [x] Idempotent: chạy 2 lần cho cùng kết quả
**Kiểm tra:** unit với bộ ví dụ dương và âm (cả hai nhóm đều bắt buộc)
**Phụ thuộc:** T02 · **File:** `apps/backend/src/shared/privacy.ts` (+ test) · **Quy mô:** S

### AI-02: Hợp đồng gateway, fake provider và usage log
**Mô tả:** Kiểu public trong `ai-gateway/index.ts` (`AiFeature`, `CallContext`, `AiGateway`, `ToolDefinition`, 3 lớp lỗi), cổng `Provider`, `fake-provider.ts` (kết quả cố định, ghi lại request), bảng `ai_usage_logs`, `pricing.ts`, hàm tính chi phí, `chat()` đi qua `redactPII` rồi provider, ghi log theo Decision 8.
**Tiêu chí nghiệm thu:**
- [x] Gọi `chat` bằng fake provider ghi đúng 1 dòng `ai_usage_logs`, không chứa nội dung prompt, `actorKeyHash` là hash
- [x] Fake provider nhận text đã qua `redactPII` (kiểm bằng request đã ghi)
- [x] Lời gọi lỗi và lời gọi bị chặn cũng có 1 dòng log (`status` `error`/`blocked`)
- [x] Không có API key thì dev chạy bằng fake provider
**Kiểm tra:** unit tính chi phí; integration ghi log vào PostgreSQL
**Phụ thuộc:** AI-01, T03 · **File:** `modules/ai-gateway/{index,types,fake-provider,usage-log,pricing,service}.ts`, `prisma/schema/ai-gateway.prisma` · **Quy mô:** M

### AI-03: Timeout, retry và circuit breaker
**Mô tả:** Bộ bọc độ tin cậy quanh provider: timeout (chat 30 s, token đầu stream 10 s, embed 15 s), retry tối đa 2 lần chỉ với 429, 5xx, lỗi mạng (backoff 500 ms rồi 1500 ms có jitter), circuit breaker theo provider (5 lỗi liên tiếp mở 60 giây, sau đó thử 1 request).
**Tiêu chí nghiệm thu:**
- [x] 4xx khác 429 không retry; không bao giờ quá 3 lần gọi cho một bước
- [x] 5 lỗi liên tiếp rồi các lần gọi trong 60 giây ném `AiUnavailableError` dưới 50 ms và không phát sinh request ra ngoài
- [x] Sau 60 giây chỉ cho 1 request thử; thành công thì đóng mạch
**Kiểm tra:** unit với fake timer và provider giả lỗi theo kịch bản
**Phụ thuộc:** AI-02 · **File:** `modules/ai-gateway/{reliability,circuit-breaker}.ts` (+ test) · **Quy mô:** M

### AI-04: Adapter DeepSeek (chat, stream) và cấu hình
**Mô tả:** `providers/deepseek.ts` dùng Vercel AI SDK, `maxRetries: 0`, đọc `LLM_BASE_URL`, `LLM_API_KEY`, `LLM_MODEL` từ config, `chat` và `chatStream`, usage từ callback của SDK, lỗi ánh xạ sang lỗi nội bộ. Live contract test (1 lần chat).
**Tiêu chí nghiệm thu:**
- [x] Đổi `LLM_MODEL` hoặc `LLM_BASE_URL` không cần sửa code (kiểm bằng test với HTTP mock)
- [x] Adapter test bằng HTTP mock: 200, 429, 500, timeout đều ánh xạ đúng và đi qua bộ retry của AI-03
- [x] API key không xuất hiện trong log hay lỗi trả ra
- [ ] Với `AI_LIVE_TESTS=1`: live test chat với DeepSeek thật pass
**Kiểm tra:** `pnpm test`; chạy thêm `AI_LIVE_TESTS=1 pnpm --filter @pickory/backend test -- ai-gateway/providers`
**Phụ thuộc:** AI-03, AI-00 · **File:** `modules/ai-gateway/providers/{deepseek,index}.ts`, `shared/config.ts` (+ test, `msw`) · **Quy mô:** M

### AI-05: Ngân sách và hạn mức
**Mô tả:** Bảng `ai_settings` (ngân sách tháng, hạn mức chat), mặc định lấy từ biến môi trường. Tổng chi phí tháng tính từ log theo giờ `Asia/Ho_Chi_Minh`. Ngưỡng 80% cảnh báo, 100% chặn bậc thấp, 120% chặn hết (bảng bậc ở `plan.md` Decision 9). Hạn mức chat ngày theo người (50 và 15), áp cho `shopping-assistant.chat`, đếm trong bộ nhớ, phân biệt bằng tiền tố `actorKey`.
**Tiêu chí nghiệm thu:**
- [x] Đạt 100% ngân sách thì `personalized-content.*` ném `AiBudgetExceededError` còn `shopping-assistant.chat` vẫn chạy; đạt 120% thì chat cũng dừng
- [x] Khách vãng lai gửi tin thứ 16 trong ngày bị `AiBudgetExceededError` (thông báo dễ hiểu); người đã đăng nhập chặn ở tin 51; sang ngày mới theo giờ VN thì đếm lại
- [x] Lời gọi bị chặn ghi 1 dòng log `blocked`, không gọi provider
- [x] `actorKey` sai định dạng bị báo lỗi lập trình
**Kiểm tra:** unit ngưỡng và biên giờ VN (23:59 và 00:00); integration với log trong DB
**Phụ thuộc:** AI-04 · **File:** `modules/ai-gateway/{budget,rate-limit,settings}.ts`, `prisma/schema/ai-gateway.prisma` (+ test) · **Quy mô:** M

### AI-06: chatJson
**Mô tả:** `chatJson<T>` bật JSON mode của provider, parse và validate bằng Zod, sai schema thì thử lại 1 lần kèm thông báo lỗi validate, vẫn sai thì ném `AiInvalidOutputError`. Live test.
**Tiêu chí nghiệm thu:**
- [x] Trả object đúng schema; sai schema lần đầu rồi đúng lần hai thì thành công
- [x] Sai hai lần thì `AiInvalidOutputError` sau đúng 2 lần gọi, mỗi lần 1 dòng log
- [ ] Với `AI_LIVE_TESTS=1`: live test `chatJson` pass
**Kiểm tra:** unit với fake provider theo kịch bản; live test
**Phụ thuộc:** AI-05, AI-00 · **File:** `modules/ai-gateway/{service,chat-json}.ts`, `providers/deepseek.ts` (+ test) · **Quy mô:** S

### AI-07: Vòng lặp tool nhiều bước
**Mô tả:** `ToolDefinition` của Pickory thành tool của SDK, `maxSteps`, mỗi bước qua kiểm tra ngân sách, `redactPII` (cả kết quả tool trả về trước khi đưa lại cho LLM) và ghi log, theo quyết định ở AI-00 (hook SDK hoặc gateway tự chạy từng bước). Live test tool calling.
**Tiêu chí nghiệm thu:**
- [x] Vòng lặp 3 bước ghi đúng 3 dòng `ai_usage_logs` và dừng ở `maxSteps` dù model còn muốn gọi tool
- [x] Ngân sách chạm ngưỡng giữa chừng thì bước kế tiếp bị chặn và vòng lặp kết thúc bằng `AiBudgetExceededError`
- [x] Kết quả tool chứa SĐT hoặc email được ẩn trước khi gửi cho LLM (kiểm bằng request đã ghi)
- [x] Tool ném lỗi thì lỗi được đưa lại cho LLM dạng thông báo, không làm sập vòng lặp
- [ ] Với `AI_LIVE_TESTS=1`: live test tool calling pass
**Kiểm tra:** unit với fake provider có kịch bản nhiều bước; live test
**Phụ thuộc:** AI-05, AI-00 · **File:** `modules/ai-gateway/{tool-loop,service}.ts`, `providers/deepseek.ts` (+ test) · **Quy mô:** M

### AI-08: Embed, cache và adapter OpenAI
**Mô tả:** `providers/openai-embedding.ts` (`embedMany`, `text-embedding-3-small`), bảng `ai_embedding_cache` (`Float[]`, key SHA-256 của `model + "\0" + text đã redact`), `embed(texts)` chỉ gọi provider cho text chưa có cache, giữ đúng thứ tự kết quả. Live test.
**Tiêu chí nghiệm thu:**
- [x] Gọi `embed` 2 lần cùng text chỉ phát sinh 1 request tới provider; mảng trộn text cũ và mới chỉ gửi text mới, kết quả đúng thứ tự
- [x] Mỗi lời gọi `embed` có đúng 1 dòng log; trúng cache toàn bộ thì `costUsd = 0`
- [ ] Vector có 1536 chiều (kiểm bằng live test)
- [ ] Với `AI_LIVE_TESTS=1`: live test embed pass
**Kiểm tra:** integration cache trong PostgreSQL; unit thứ tự kết quả
**Phụ thuộc:** AI-05 · **File:** `modules/ai-gateway/{embedding-cache,service}.ts`, `providers/openai-embedding.ts`, `prisma/schema/ai-gateway.prisma` (+ test) · **Quy mô:** M

### AI-09: API back-office AI và giao diện cấu hình
**Mô tả:** `GET /api/admin/ai/usage` (`ai-usage:read`), `GET /api/admin/ai/settings` (`ai-usage:read`), `PUT /api/admin/ai/settings` (`ai-settings:manage`, ghi audit), `getUsageSummary(range)` (chi phí theo ngày và feature, tỉ lệ lỗi, p95 độ trễ), `aiPermissions`. Trang nhỏ trong back-office để xem chi phí và sửa ngân sách.
**Tiêu chí nghiệm thu:**
- [x] `PUT` sửa ngân sách có hiệu lực ở lần gọi kế tiếp và tạo đúng 1 dòng audit
- [x] `sales` gọi `PUT` nhận 403, gọi `GET usage` được; thêm các endpoint vào test ma trận quyền
- [x] `getUsageSummary` đúng số liệu trên dữ liệu log mẫu (gồm p95)
- [x] Giá trị âm hoặc không hợp lệ bị 400
**Kiểm tra:** integration; thủ công trang cấu hình bằng admin dev
**Phụ thuộc:** AI-05, ID-04 · **File:** `modules/ai-gateway/{routes,usage-summary,index}.ts`, `apps/frontend/src/features/ai-gateway/admin/ai-settings-page.tsx` (+ test) · **Quy mô:** M

---

## Phase 5: E2E

### E2E-01: Playwright và hai luồng E2E của identity
**Mô tả:** Cài Playwright, DB và seed cho E2E, đọc link từ file outbox dev. Luồng 1: khách đăng ký, đăng xuất, đăng nhập. Luồng 2: admin mời nhân viên `sales`, nhân viên đặt mật khẩu, đăng nhập.
**Tiêu chí nghiệm thu:**
- [x] `pnpm test:e2e` chạy sạch cả hai luồng
- [x] Luồng 2 xác nhận nhân viên `sales` không thấy các mục menu ngoài quyền của mình. Phần "thấy menu đơn hàng" hoãn tới khi `commerce` và `admin-console` có (ghi trong Traceability)
- [x] Chạy lại toàn bộ E2E lần hai với cùng email vẫn PASS, không dùng dữ liệu còn từ lần trước
- [x] DB `pickory_e2e` riêng; migrate, reset và seed trước khi API báo ready; không tái sử dụng API dev
- [x] Locator dùng `getByRole`/`getByLabel`, chờ điều kiện thay vì sleep cố định; mật khẩu fixture sinh ở runtime, outbox/report bị gitignore
- [x] `pnpm lint && pnpm typecheck && pnpm test && pnpm build` PASS
**Kiểm tra:** `pnpm test:e2e`: lần 1 có 2 PASS (18.8s), lần 2 có 2 PASS (13.3s). Suite Vitest có 361 PASS, 4 live test SKIP như trước task này. Số user và fingerprint bảng `users` của DB dev không đổi qua lần chạy E2E thứ hai. Đã xem ảnh profile customer và menu sales trong `test-results/`. Build còn cảnh báo chunk frontend 668.23 kB và runner có cảnh báo `NO_COLOR`/`FORCE_COLOR` từ môi trường terminal.
**Phụ thuộc:** UI-01, UI-02, UI-07, ID-12 · **File:** `e2e/{auth,staff-invite}.spec.ts`, `e2e/{environment,outbox}.ts`, `playwright.config.ts`, `tsconfig.e2e.json`, `apps/backend/scripts/e2e-start.ts`, script/dependency root, compiler backend và proxy Vite · **Quy mô:** M

### SEC-01: Rà soát dependency qua Prisma
**Trạng thái:** Chưa hoàn tất.
**Hạn rà soát:** 2026-10-12 hoặc trước release, tùy mốc nào đến trước.
**Mô tả:** Audit hiện có 2 High và 1 Moderate qua chuỗi dependency của Prisma.
**Đánh giá hiện tại:**
- [deepmerge-ts](https://github.com/advisories/GHSA-ggr8-5vv4-36mx) cần đồ thị object tự tham chiếu. Cấu hình Prisma là object tĩnh từ repo. Cấu hình không nhận đồ thị object từ request.
- [Rò thông tin kết nối](https://github.com/advisories/GHSA-3f6p-5ww8-9rcr) của `mysql2` cần kết nối MySQL.
- [Giải nén không giới hạn](https://github.com/advisories/GHSA-rgwj-5xj2-c3m3) của `mysql2` cần kết nối MySQL.
- Backend dùng PostgreSQL qua adapter `PrismaPg`. E2E dùng cùng adapter.
- [INFERENCE] Chưa thấy đường khai thác ba cảnh báo trong luồng E2E hiện tại.
**Tiêu chí nghiệm thu:**
- [ ] Rà soát ba advisory trước hạn.
- [ ] Ghi nhận đường khai thác hoặc lý do chưa thấy đường khai thác.
- [ ] Chốt biện pháp cho dependency trước release.
**Phạm vi hiện tại:** Không nâng dependency trong lần sửa E2E-01 này. Không thêm override.
**Cách rà soát:**
1. Chạy `pnpm audit`.
2. Đối chiếu điều kiện khai thác của từng advisory với cấu hình Prisma.
3. Ghi lại đánh giá trong SEC-01.

---

## Checkpoints

### Checkpoint 1: Sau ID-04, UI-05, AI-03
- [ ] Lint, typecheck, test, build sạch
- [ ] Nhân viên đăng nhập vào khung back-office; `requirePermission` và `recordAudit` dùng được
- [ ] `chat` qua fake provider có redactPII, log, retry và circuit breaker
- [ ] Review với người dùng: đã có API key cho AI-04?

### Checkpoint 2: Sau UI-03, AI-06
- [ ] Khách đăng ký, đăng nhập, quên mật khẩu, chọn consent qua UI
- [ ] Live test chat và `chatJson` với DeepSeek thật pass; ngân sách và hạn mức chặn đúng
- [ ] Review với người dùng trước khi làm tiếp

### Checkpoint 3: Sau UI-04, AI-09
- [ ] Mọi tiêu chí trong `SPEC-ai-gateway.md` đạt (xem Traceability)
- [ ] Luồng tài khoản khách đầy đủ gồm Google và xóa tài khoản
- [ ] Review với người dùng

### Checkpoint cuối: Sau E2E-01
- [ ] Mọi dòng trong bảng Traceability đạt, trừ hai mục ghi "một phần"/"hoãn"
- [ ] Coverage `service.ts` và file logic của hai module ≥ 80%
- [ ] `AI_LIVE_TESTS=1` pass cho chat, `chatJson`, tool calling, embed
- [ ] `pnpm test:e2e` pass; không có secret trong git
- [ ] Review với người dùng, rồi mới bắt đầu `catalog`, `shipping`, `knowledge-base`
