# Spec: identity

Module id: `identity` · Phụ thuộc: không có · Quy ước chung: `SPEC.md`

## Objective

Quản lý danh tính, phiên đăng nhập, phân quyền theo role (RBAC), tài khoản nhân viên, nhật ký thao tác và sự đồng ý thu thập dữ liệu. Đây là nền cho mọi module khác: module nào cũng cần biết "ai đang gửi request", "người này được làm gì" và "người này có cho phép cá nhân hóa không".

### Role

| Role | Mã | Loại | Ai dùng |
|---|---|---|---|
| Khách hàng | `customer` | hệ thống, không sửa được | Người mua hàng tự đăng ký |
| Quản trị viên | `admin` | hệ thống, không sửa được | Chủ shop, có mọi quyền |
| Nhân viên bán hàng | `sales` | tạo sẵn, admin sửa được | Xử lý đơn, đối soát thanh toán |
| Nhân viên CSKH | `support` | tạo sẵn, admin sửa được | Tiếp nhận chat từ chatbot, hỗ trợ khách, kiểm duyệt review |

Admin tạo thêm được role mới (ví dụ "Quản lý kho") và chọn quyền cho role đó trên giao diện.

### User stories

**Khách hàng**
- Là khách vãng lai, tôi xem, tìm, chat và mua hàng được mà không cần tạo tài khoản.
- Là khách vãng lai, khi tôi đăng ký hoặc đăng nhập, giỏ hàng và lịch sử xem của tôi được giữ lại.
- Là khách, tôi đăng ký bằng email + mật khẩu hoặc đăng nhập bằng Google, và lấy lại được mật khẩu qua email.
- Là khách, tôi chọn có cho phép cá nhân hóa hay không, và đổi lựa chọn bất cứ lúc nào.
- Là khách, tôi yêu cầu xóa tài khoản và dữ liệu cá nhân của mình.

**Nhân viên**
- Là nhân viên, tôi nhận email mời, tự đặt mật khẩu rồi đăng nhập vào khu vực back-office.
- Là nhân viên, tôi chỉ thấy menu và thao tác được những gì role của tôi cho phép.

**Quản trị viên**
- Là admin, tôi mời nhân viên, gán một hoặc nhiều role, khóa tài khoản nhân viên đã nghỉ.
- Là admin, tôi tạo, sửa, xóa role và chọn quyền cho từng role.
- Là admin, tôi xem nhật ký ai đã làm gì (đổi role, đổi trạng thái đơn, khóa tài khoản...).

## Thiết kế

### Quyền (permission) và role

- **Quyền khai báo trong code**, vì code là nơi kiểm tra quyền. Admin không tạo được quyền mới, chỉ gom quyền có sẵn thành role.
- **Role lưu trong database**, admin tạo và sửa trên giao diện.
- Quyền của một người = hợp quyền của mọi role người đó giữ.
- Mỗi module khai báo quyền của mình trong `modules/<module-id>/permissions.ts`. `identity` gom lại thành danh mục quyền chung.

**Danh mục quyền ban đầu** (`resource:action`):

| Nhóm | Quyền | Mô tả |
|---|---|---|
| identity | `staff:read`, `staff:manage` | Xem; mời, khóa nhân viên, gán role |
| | `roles:manage` | Tạo, sửa, xóa role |
| | `customers:read`, `customers:manage` | Xem thông tin khách; khóa/mở tài khoản khách |
| | `audit-logs:read` | Xem nhật ký thao tác |
| catalog | `products:read`, `products:write`, `inventory:write` | Xem sản phẩm ẩn; sửa sản phẩm; sửa tồn kho |
| commerce | `orders:read`, `orders:update-status`, `orders:cancel` | Xem; cập nhật trạng thái; hủy đơn |
| | `payments:read`, `payments:reconcile` | Xem giao dịch; khớp tay giao dịch SePay không tự khớp được |
| | `reviews:moderate` | Ẩn, hiện review |
| support-desk | `conversations:read`, `conversations:reply` | Xem hội thoại; nhận và trả lời hội thoại chatbot chuyển sang |
| shipping | `shipping-settings:manage` | Sửa địa chỉ lấy hàng, cấu hình GHN |
| commerce (bổ sung) | `commerce-settings:manage` | Sửa ngưỡng miễn phí vận chuyển, hạn mức COD, tài khoản nhận SePay |
| | `orders:fulfill` | Tạo, hủy vận đơn GHN cho đơn hàng |
| | `refunds:manage` | Đánh dấu đã hoàn tiền |
| knowledge-base | `knowledge:read`, `knowledge:write` | Xem mọi tài liệu kể cả nội bộ; soạn, nhập, đăng tài liệu |
| ai-gateway | `ai-usage:read`, `ai-settings:manage` | Xem chi phí AI; sửa ngân sách, hạn mức |
| support-desk | `support-settings:manage` | Giờ làm việc, ngày nghỉ, mẫu trả lời dùng chung |
| admin-console | `reports:read` | Xem dashboard, báo cáo |
| | `reports:export` | Xuất báo cáo, xuất danh sách đơn hàng ra Excel/CSV |

Danh mục này mở rộng khi viết spec các module sau. Thêm quyền mới thuộc mục "Ask first".

**Quyền mặc định của role tạo sẵn:**

| Quyền | `admin` | `sales` | `support` |
|---|:-:|:-:|:-:|
| `staff:*`, `roles:manage`, `audit-logs:read`, `ai-settings:manage` | ✓ | | |
| `shipping-settings:manage`, `commerce-settings:manage`, `refunds:manage` | ✓ | | |
| `support-settings:manage`, `reports:export` | ✓ | | |
| `customers:read` | ✓ | ✓ | ✓ |
| `customers:manage` | ✓ | | |
| `products:read` | ✓ | ✓ | ✓ |
| `products:write`, `inventory:write` | ✓ | | |
| `orders:read` | ✓ | ✓ | ✓ |
| `orders:update-status`, `orders:cancel`, `orders:fulfill` | ✓ | ✓ | |
| `payments:read`, `payments:reconcile` | ✓ | ✓ | |
| `reviews:moderate` | ✓ | | ✓ |
| `conversations:read`, `conversations:reply` | ✓ | | ✓ |
| `knowledge:read` | ✓ | ✓ | ✓ |
| `knowledge:write` | ✓ | | |
| `ai-usage:read`, `reports:read` | ✓ | ✓ | |

- `admin` luôn có mọi quyền, kể cả quyền được thêm sau này. Không sửa, không xóa được.
- `customer` không có quyền back-office nào. Thao tác của khách (xem đơn của mình, sửa giỏ của mình) kiểm tra theo **quyền sở hữu** trong từng module, không theo danh mục quyền.

**Quy tắc an toàn:**
- Không được xóa hoặc khóa admin cuối cùng còn hoạt động.
- Không ai tự sửa role của chính mình.
- Chống leo thang đặc quyền: nhân viên không phải admin không được tự sửa role mà chính mình đang giữ; chỉ được cấp những quyền mà chính actor đang sở hữu (`requestedPermissions ⊆ actor.permissions`); chỉ admin mới có quyền mời hoặc gán role `admin`.
- Không xóa được role đang có người giữ, phải gỡ role khỏi mọi người trước.
- Không gán được `customer` cùng role nhân viên cho một tài khoản (tài khoản khách và tài khoản nhân viên tách riêng).
- Đổi quyền của role có hiệu lực ngay từ request tiếp theo, không cần đăng nhập lại.
### Actor

Mọi request đều có `req.actor`, do middleware gắn vào:

```ts
// apps/backend/src/modules/identity/index.ts
export type Actor = {
  anonymousId: string;              // luôn có, lấy từ cookie pk_anon
  userId: string | null;            // null nếu chưa đăng nhập
  kind: 'guest' | 'customer' | 'staff';
  roles: string[];                  // ['sales', 'support']
  permissions: ReadonlySet<Permission>;
  consent: { personalization: boolean; marketing: boolean };
};

export { actorMiddleware, requireAuth, requireStaff, requirePermission } from './middleware';
export { recordAudit } from './audit';     // module khác ghi nhật ký thao tác của nhân viên
export { onUserLinked, onUserDeleted } from './events';
export type { Permission } from './permissions';
```

```ts
// cách module khác dùng
ordersRouter.patch(
  '/admin/orders/:id/status',
  requirePermission('orders:update-status'),
  async (req, res) => { /* ... */ },
);
```

- `requirePermission` trả 401 nếu chưa đăng nhập, 403 nếu thiếu quyền. Nhận nhiều quyền thì yêu cầu có đủ tất cả.
- Quyền được nạp mỗi request bằng một truy vấn join (session → user → roles → permissions). Chưa cache ở MVP.
- Frontend nhận danh sách quyền qua `/api/me` để ẩn menu và nút. Việc ẩn chỉ để tiện dùng, backend vẫn kiểm tra mọi request.

### Khách vãng lai và gộp dữ liệu

- Lần đầu truy cập, server đặt cookie `pk_anon` (UUID v4, httpOnly, Secure, SameSite=Lax, hạn 1 năm).
- Khi khách đăng nhập hoặc đăng ký thành công, `identity` phát sự kiện `UserLinkedEvent { anonymousId, userId }`.
- `commerce` (gộp giỏ) và `activity-tracking` (gắn lịch sử) tự đăng ký xử lý sự kiện. `identity` không ghi vào bảng của module khác.
- MVP dùng event emitter trong process. Handler lỗi được log nhưng không làm đăng nhập thất bại.
- Nhân viên đăng nhập không phát sự kiện này.

### Phiên đăng nhập

- Phiên lưu phía server trong bảng `sessions`. Cookie `pk_session` chỉ chứa token ngẫu nhiên 32 byte. DB lưu hash SHA-256 của token.
- Cookie: httpOnly, Secure, SameSite=Lax.
- Khách: phiên 30 ngày, tự gia hạn khi còn dưới 7 ngày và vẫn hoạt động.
- Nhân viên: phiên 12 giờ, không tự gia hạn.
- Đăng xuất xóa phiên trong DB. Đổi mật khẩu, bị khóa tài khoản, hoặc bị gỡ hết role thì mọi phiên của người đó bị xóa.
- Request thay đổi dữ liệu (POST/PUT/PATCH/DELETE) kiểm tra header `Origin` khớp domain web để chống CSRF.

### Tài khoản khách hàng

- Đăng ký bằng email + mật khẩu (tối thiểu 8 ký tự, băm Argon2id), được gán role `customer`.
- Sau đăng ký, đăng nhập luôn, đồng thời nhận email xác thực (link hạn 24 giờ).
- Quên mật khẩu: link đặt lại hạn 30 phút, dùng 1 lần. Endpoint luôn trả cùng một response dù email có tồn tại hay không.
- Đăng nhập Google: OAuth 2.0 Authorization Code + PKCE, kiểm tra `state`. Email Google đã xác thực trùng tài khoản khách có sẵn thì liên kết; trùng email chưa xác thực thì không tự liên kết.
- Email đã thuộc tài khoản nhân viên thì không đăng ký làm khách được.

### Tài khoản nhân viên

- Admin đầu tiên được tạo bằng lệnh `pnpm admin:create`.
- Người có `staff:manage` mời nhân viên: nhập email, tên, chọn role. Hệ thống gửi link đặt mật khẩu (hạn 72 giờ, dùng 1 lần).
- Nhân viên đăng nhập bằng email + mật khẩu tại `/admin/login`, không dùng Google.
- Khóa nhân viên: không xóa bản ghi, đặt `lockedAt`, xóa mọi phiên. Lịch sử thao tác vẫn giữ tên người đó.
- Với thao tác khóa, mở khóa hoặc đổi role, nhân viên không phải admin chỉ được quản lý người có toàn bộ quyền hiện tại nằm trong quyền của mình. Đọc role và quyền của người đích trong cùng transaction với thao tác; không đủ quyền trả 403 `FORBIDDEN`. Admin vẫn chịu các ràng buộc bảo vệ chính mình và admin cuối cùng.
- Trang quản trị hiển thị thông báo lỗi tải dữ liệu trong khối `role="alert"`, không coi lỗi 401/403 là danh sách rỗng. `QueryCache` và `MutationCache` gặp lỗi 401 thì invalidate `['actor']` để layout cập nhật phiên và chuyển về `/admin/login` nếu phiên đã mất hiệu lực.

### Chống dò mật khẩu

- Sai 5 lần trong 15 phút theo cặp (email, IP) thì khóa thử đăng nhập 15 phút, trả 429.
- Thông báo lỗi không phân biệt "sai email" và "sai mật khẩu".

### Chống spam đăng ký và yêu cầu đặt lại mật khẩu

- **Đăng ký (`POST /api/auth/register`)**: Giới hạn tối đa 5 lần / giờ / IP. Vượt ngưỡng trả lỗi 429 `TOO_MANY_REQUESTS` với thông báo tiếng Việt, không tạo tài khoản và không gửi email xác thực.
- **Quên mật khẩu (`POST /api/auth/forgot-password`)**: Giới hạn tối đa 3 lần / 15 phút / email và 10 lần / giờ / IP. Vượt ngưỡng vẫn trả phản hồi chung thành công giống hệt bình thường (chống dò email), nhưng không tạo token trong DB và không gửi email.
- **Honeypot form đăng ký**: Form đăng ký chứa trường ẩn (`website`, ẩn khỏi màn hình và trình đọc bằng CSS, `tabIndex=-1`, `autoComplete="off"`, `aria-hidden="true"`). Request có dữ liệu honeypot bị từ chối với lỗi 400 chung, không tạo tài khoản, không gửi email.
- **Cấu hình Trust Proxy**: Biến môi trường `TRUST_PROXY` nhận số hop (số nguyên >= 0, ví dụ 1) hoặc danh sách IP/subnet proxy phân tách bằng dấu phẩy (mặc định tắt: 0 / false). Khi bật, ứng dụng truyền số hop hoặc danh sách IP vào `app.set('trust proxy', ...)`, tuyệt đối không dùng giá trị `true` để ngăn chặn kẻ xấu tự tạo header `X-Forwarded-For` giả mạo nhằm vượt qua các bộ đếm tần suất theo IP.
- **Bộ đếm trong bộ nhớ**: Bộ đếm tần suất tự cài đặt trong bộ nhớ (hỗ trợ tham số thời gian phục vụ kiểm thử), không dùng thư viện ngoài.

### Phòng chống chiếm tài khoản trước (pre-hijacking) khi liên kết Google

- Khi người dùng đăng nhập bằng Google và email Google đã xác thực trùng với một tài khoản khách đã tồn tại trong hệ thống:
  - Nếu tài khoản đã xác thực email (`emailVerified = true`): giữ nguyên mật khẩu và các phiên hiện có, chỉ tạo liên kết OAuth.
  - Nếu tài khoản chưa xác thực email (`emailVerified = false`): để ngăn chặn kẻ xấu đăng ký trước bằng email nạn nhân để giữ quyền truy cập qua mật khẩu, hệ thống thực hiện trong cùng transaction:
    1. Xóa `passwordHash` (đặt về null).
    2. Xóa toàn bộ phiên cũ trong bảng `sessions`.
    3. Vô hiệu hóa mọi token xác thực và đặt lại mật khẩu còn hạn (`usedAt = now`).
    4. Cập nhật `emailVerified = true`.
    5. Tạo liên kết OAuth và cấp phiên mới.
    Mật khẩu do kẻ xấu đặt trước đó lập tức bị vô hiệu, không thể dùng để đăng nhập.

### Nhật ký thao tác (audit log)

- `recordAudit(tx, { actorId, action, targetType, targetId, before?, after? })` ghi vào bảng `audit_logs` trong cùng transaction client (`tx`) với thao tác chính.
- `identity` tự ghi: mời, khóa, mở khóa nhân viên; gán, gỡ role; tạo, sửa, xóa role; khóa tài khoản khách.
- Module khác gọi `recordAudit` cho thao tác back-office của họ (đổi trạng thái đơn, khớp tay thanh toán, ẩn review...). Ghi trong cùng transaction với thao tác chính.
- Audit log chỉ thêm, không sửa, không xóa qua API.
- `before`/`after` không chứa mật khẩu, token hay thông tin thanh toán nhạy cảm.

### Đồng ý thu thập dữ liệu

| Nhóm | Mặc định | Ý nghĩa |
|---|---|---|
| `necessary` | luôn bật | Cookie phiên, giỏ hàng, bảo mật. Không tắt được |
| `personalization` | tắt cho tới khi người dùng chọn | Ghi hành vi, gợi ý và nội dung cá nhân hóa |
| `marketing` | tắt | Email khuyến mãi |

- Lần đầu truy cập hiện banner với hai nút "Đồng ý" và "Chỉ cần thiết" ngang nhau, cùng link "Tùy chỉnh".
- Mỗi lần đổi lựa chọn lưu một bản ghi mới (`consent_records`): anonymousId, userId (nếu có), lựa chọn, phiên bản chính sách, thời điểm.
- Khi gộp khách vãng lai vào tài khoản, lấy lựa chọn mới nhất giữa hai bên.

### Xóa tài khoản khách

- Khách yêu cầu xóa trong trang tài khoản, xác nhận lại bằng mật khẩu hoặc đăng nhập lại Google.
- Xóa phiên, credential, liên kết OAuth, thay email và tên bằng giá trị ẩn danh. Giữ bản ghi `users` (đánh dấu `deletedAt`) để đơn hàng cũ vẫn đúng tham chiếu.
- Phát `UserDeletedEvent { userId }` để các module khác xóa dữ liệu của họ.

## API

**Khách hàng và chung**

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| POST | `/api/auth/register` | công khai | Đăng ký khách |
| POST | `/api/auth/login` | công khai | Đăng nhập khách |
| POST | `/api/auth/logout` | đã đăng nhập | Đăng xuất |
| GET | `/api/auth/google/start` | công khai | Chuyển hướng sang Google |
| GET | `/api/auth/google/callback` | công khai | Nhận kết quả từ Google |
| POST | `/api/auth/verify-email` | công khai | Xác thực email |
| POST | `/api/auth/forgot-password` | công khai | Gửi link đặt lại mật khẩu |
| POST | `/api/auth/reset-password` | công khai | Đặt mật khẩu mới bằng token (dùng cả cho lời mời nhân viên) |
| GET | `/api/me` | công khai | Actor hiện tại, gồm role và danh sách quyền |
| PATCH | `/api/me` | đã đăng nhập | Sửa tên, SĐT |
| POST | `/api/me/password` | đã đăng nhập | Đổi mật khẩu |
| PUT | `/api/me/consent` | công khai | Lưu lựa chọn đồng ý |
| DELETE | `/api/me` | khách đã đăng nhập | Xóa tài khoản |

**Back-office**

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| POST | `/api/admin/auth/login` | công khai | Đăng nhập nhân viên |
| GET | `/api/admin/staff` | `staff:read` | Danh sách nhân viên |
| POST | `/api/admin/staff/invitations` | `staff:manage` | Mời nhân viên |
| PUT | `/api/admin/staff/:id/roles` | `staff:manage` | Gán role |
| POST | `/api/admin/staff/:id/lock` · `/unlock` | `staff:manage` | Khóa, mở khóa |
| GET | `/api/admin/roles` | `roles:manage` | Danh sách role kèm quyền |
| POST · PATCH · DELETE | `/api/admin/roles[/:id]` | `roles:manage` | Tạo, sửa, xóa role |
| GET | `/api/admin/permissions` | `roles:manage` | Danh mục quyền (từ code) |
| GET | `/api/admin/customers[/:id]` | `customers:read` | Tra cứu khách |
| POST | `/api/admin/customers/:id/lock` · `/unlock` | `customers:manage` | Khóa, mở khóa khách |
| GET | `/api/admin/audit-logs` | `audit-logs:read` | Nhật ký, lọc theo người, hành động, thời gian |

## Data Model (Prisma, rút gọn)

```prisma
model User {
  id            String    @id @default(uuid())
  email         String    @unique
  emailVerified Boolean   @default(false)
  passwordHash  String?
  name          String?
  phone         String?
  kind          UserKind  // customer | staff
  lockedAt      DateTime?
  deletedAt     DateTime?
  createdAt     DateTime  @default(now())
  roles         UserRole[]
  @@map("users")
}

model Role {
  id          String           @id @default(uuid())
  code        String           @unique   // 'admin', 'sales', 'support', ...
  name        String                     // 'Nhân viên bán hàng'
  isSystem    Boolean          @default(false)
  permissions RolePermission[]
  @@map("roles")
}

model RolePermission {
  roleId     String
  permission String   // phải thuộc danh mục quyền trong code
  @@id([roleId, permission])
  @@map("role_permissions")
}

model UserRole {
  userId String
  roleId String
  @@id([userId, roleId])
  @@map("user_roles")
}

model AuditLog {
  id         String   @id @default(uuid())
  actorId    String
  action     String   // 'role.assign', 'order.status_change', ...
  targetType String
  targetId   String
  before     Json?
  after      Json?
  createdAt  DateTime @default(now())
  @@map("audit_logs")
}

model Session       { /* tokenHash, userId, expiresAt */ }
model OAuthAccount  { /* provider, providerUserId, userId */ }
model EmailToken    { /* tokenHash, userId, purpose(verify|reset|invite), expiresAt, usedAt */ }
model ConsentRecord { /* anonymousId, userId?, personalization, marketing, policyVersion, createdAt */ }
```

- Seed tạo sẵn 4 role kèm quyền mặc định ở bảng trên.
- Khi khởi động, server kiểm tra mọi `role_permissions.permission` đều có trong danh mục code; quyền không còn tồn tại thì bỏ qua và ghi cảnh báo.

## Frontend

- `features/identity/` (khách): đăng nhập, đăng ký, quên và đặt lại mật khẩu, trang tài khoản (thông tin, đổi mật khẩu, quyền riêng tư, xóa tài khoản), banner đồng ý.
- `features/identity/admin/` (back-office): đăng nhập nhân viên, danh sách và mời nhân viên, quản lý role (bảng tick quyền theo nhóm), tra cứu khách, xem audit log.
- Hook `useActor()` và `useCan(permission)` để các feature ẩn hoặc hiện menu, nút.
- Layout khách dùng chung Header trên mọi route, kể cả `/`: trạng thái phiên, đăng xuất và link quyền riêng tư phản ánh cùng cache actor.
- Verify hoàn tất đúng trong StrictMode, chỉ gửi một request cho token đã đọc. Verify và reset giữ token trong component nhưng xóa token khỏi URL bằng navigation `replace`.
- Reset thành công phải reset cache actor và nạp lại trạng thái guest trước khi hiện link đăng nhập; khách ở `/login`, nhân viên ở `/admin/login`.
- Query không retry lỗi HTTP 4xx; phiên nhân viên bị thu hồi phải chuyển về `/admin/login` trong 2 giây khi điều hướng.
- Form identity dùng `noValidate` và kiểm tra email, mật khẩu bằng thông báo tiếng Việt của ứng dụng. Reset thiếu token hiện lỗi ngay, không hiện form.
- Login và register không tràn ngang ở viewport 375px. Dialog mời, gán role, tạo và sửa role đưa focus vào trong, giữ Tab trong dialog, Escape đóng và trả focus đúng opener.
- Hai ô ngày lọc audit gắn label với input để có accessible name.

## Testing

- Unit: băm mật khẩu, sinh và hash token, khóa đăng nhập, hợp quyền nhiều role, các quy tắc an toàn (admin cuối cùng, tự sửa role, xóa role đang dùng, trộn customer với role nhân viên), chọn consent mới nhất khi gộp.
- Integration: mọi endpoint ở bảng API, gồm 401/403, token hết hạn hoặc dùng lại, Origin sai; đổi quyền của role thì request tiếp theo của nhân viên bị từ chối hoặc được phép ngay.
- Test ma trận quyền: với mỗi role tạo sẵn, gọi mọi endpoint back-office và so với bảng quyền mặc định.
- Google OAuth test bằng phản hồi giả lập, không gọi Google thật.
- Component trong StrictMode: verify thành công hoặc lỗi không treo và không dùng token hai lần; reset bỏ actor cũ rồi giữ đúng route login cho khách và nhân viên; dialog giữ focus qua rerender, Escape đóng và trả opener.
- E2E: khách đăng ký → đăng xuất → đăng nhập; admin mời nhân viên `sales` → nhân viên đặt mật khẩu → đăng nhập chỉ thấy menu đơn hàng.
- Rate limit: unit test kiểm tra bộ đếm vượt ngưỡng, hết cửa sổ thì mở lại, các khóa độc lập; integration test đăng ký lần 6 cùng IP nhận 429, forgot-password lần 4 cùng email không gửi email nhưng phản hồi giống hệt; honeypot không tạo user; kịch bản pre-hijacking tài khoản chưa xác thực đăng nhập Google thì mật khẩu cũ không đăng nhập được và phiên cũ bị xóa.

## Boundaries (riêng module)

- **Always:** kiểm tra quyền ở backend cho mọi endpoint back-office; so sánh token bằng hàm constant-time; ghi audit log cho thao tác phân quyền.
- **Always:** kiểm tra rate limit cho register và forgot-password; xóa credential và phiên cũ khi liên kết Google với tài khoản chưa xác thực email.
- **Ask first:** thêm quyền vào danh mục; đổi quyền mặc định của role tạo sẵn; thêm phương thức đăng nhập; đổi thời hạn phiên; đổi các nhóm đồng ý.
- **Never:** dựa vào việc ẩn nút ở frontend thay cho kiểm tra quyền; lưu token gốc trong DB; trả `passwordHash` ra API; cho phép sửa hoặc xóa audit log qua API.

## Success Criteria

- [ ] Có đủ 4 role `customer`, `admin`, `sales`, `support` sau khi seed, với quyền đúng bảng mặc định.
- [ ] Admin tạo được role mới, tick quyền, gán cho nhân viên; nhân viên có ngay quyền đó ở request tiếp theo.
- [ ] Nhân viên giữ cả `sales` và `support` có hợp quyền của hai role.
- [ ] Test ma trận quyền pass: mỗi role chỉ gọi được đúng các endpoint back-office được phép, còn lại nhận 403.
- [ ] Không xóa hoặc khóa được admin cuối cùng; không tự sửa role của mình; không xóa được role đang có người giữ.
- [ ] Nhân viên bị khóa hoặc bị gỡ hết role thì phiên hiện tại mất hiệu lực ngay.
- [ ] Mỗi thao tác phân quyền tạo đúng 1 dòng audit log có người làm, hành động, đối tượng, trước và sau.
- [ ] Đăng ký, đăng nhập email và Google cho khách hoạt động; mật khẩu lưu dạng Argon2id.
- [ ] Sai mật khẩu 5 lần trong 15 phút thì lần thứ 6 bị từ chối 429 kể cả mật khẩu đúng.
- [ ] Link đặt lại mật khẩu hết hạn sau 30 phút, link mời nhân viên hết hạn sau 72 giờ, cả hai không dùng lại được.
- [ ] Khách đăng nhập phát đúng một `UserLinkedEvent`; khách chưa chọn đồng ý thì `consent.personalization = false`.
- [ ] Request thay đổi dữ liệu với `Origin` lạ bị từ chối 403.
- [ ] Xóa tài khoản khách xong thì không đăng nhập lại được, email gốc không còn trong DB.
- [ ] Coverage `service.ts` ≥ 80%.
- [ ] Đăng ký quá 5 lần / giờ / IP nhận 429; forgot-password quá 3 lần / 15 phút / email hoặc 10 lần / giờ / IP không gửi email; honeypot chặn bot không tạo user.
- [ ] Tài khoản chưa xác thực email khi liên kết Google bị xóa mật khẩu cũ và phiên cũ, kẻ xấu không thể đăng nhập lại bằng mật khẩu đặt trước.

## Open Questions

1. Dịch vụ gửi email nào? (cần cho xác thực email, quên mật khẩu, mời nhân viên)
2. Có bắt buộc khách xác thực email trước khi đặt đơn không? Mặc định: không.
3. Nhân viên (ít nhất là admin) có cần xác thực 2 lớp TOTP trong MVP không? Mặc định: chưa, nhưng nên làm sớm vì có nhiều tài khoản back-office.
4. Có cần chức năng tải về dữ liệu cá nhân (export) trong MVP không? Mặc định: chưa.
5. Bảng quyền mặc định của `sales` và `support` ở trên có đúng cách shop vận hành không?
