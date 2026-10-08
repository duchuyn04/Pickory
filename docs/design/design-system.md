# Design system tham chiếu

Tài liệu này chốt hướng giao diện của Pickory. Token nằm ở `apps/frontend/src/shared/styles/tokens.css`. Lớp component dùng chung nằm ở `apps/frontend/src/shared/styles/components.css`.

Component chỉ dùng token. Component không viết mã màu hoặc cỡ chữ cứng. Test `test/unit/shared/styles/css-colors.test.ts` chặn mã màu trong file CSS ngoài `tokens.css`.

## Hướng đã chốt: đỏ bán lẻ điện tử, nền xám (2026-10-06)

Người dùng chốt hướng này ngày 2026-10-06. Hướng mới thay hướng cam và kem trước đó.

- Nguồn là bản mẫu HTML tĩnh `shop-retail/`. Bản mẫu này thay cho `prototype/`.
- Pickory giữ tên và logo riêng. Tên `Hải Đăng Digital` của bản mẫu không dùng trong `apps/`.
- Màu đỏ `#d70018` và nền xám `#f4f5f7` gần với thói quen mua đồ điện tử của khách Việt.

## Xem bản mẫu

1. Mở terminal ở thư mục `shop-retail/`.
2. Chạy `python -m http.server 8765 --bind 127.0.0.1`.
3. Mở `http://127.0.0.1:8765/index.html` để xem cửa hàng.
4. Mở `http://127.0.0.1:8765/admin/login.html` để xem trang quản trị.
5. Nhập mật khẩu `demo`.

## Nguyên tắc

- Đỏ dùng cho nút chính, mục đang chọn, giá và khuyến mãi. Mỗi vùng chỉ có một nút chính.
- Lỗi dùng màu lỗi `--color-danger`, khác với đỏ thương hiệu. Lỗi luôn có chữ hoặc icon, không chỉ dựa vào màu.
- Viền focus và link dùng màu xanh `--color-focus`. Màu này tách thao tác bàn phím khỏi nút đỏ và viền lỗi.
- Nền trang xám, thẻ trắng có viền mảnh. Chỉ panel nổi (hộp thoại, menu thả) có bóng rõ.
- Không gradient trang trí. Không emoji làm icon. Icon dùng bộ SVG nét mảnh trong `apps/frontend/src/shared/icons.tsx`.
- Mỗi cặp chữ và nền đạt tương phản WCAG AA (4.5:1). Test `tokens.test.ts` kiểm các cặp chính.

## Token màu

| Token | Giá trị | Dùng cho |
|---|---|---|
| `--color-brand` | `#d70018` | Nút chính, mục đang chọn, nhãn khuyến mãi |
| `--color-brand-hover` | `#bd0015` | Nút chính khi rê chuột |
| `--color-brand-soft` | `#fbe6e8` | Nền nhạt của mục đang chọn |
| `--color-on-brand` | `#ffffff` | Chữ trên nền đỏ |
| `--color-header-soft` | `#dd2438` | Nền khi rê chuột trên header đỏ |
| `--color-price` | `#d70018` | Giá bán |
| `--color-page` | `#f4f5f7` | Nền trang |
| `--color-surface` | `#ffffff` | Nền thẻ, ô nhập |
| `--color-surface-muted` | `#f6f7f9` | Nền sidebar, khối phụ |
| `--color-surface-hover` | `#f7f9fc` | Dòng bảng khi rê chuột |
| `--color-table-head` | `#fafbfc` | Nền tiêu đề cột của bảng ngoài thẻ |
| `--color-dark` | `#14171c` | Nút chọn đang bật, thanh thông báo nền tối |
| `--color-on-dark` | `#ffffff` | Chữ trên nền tối, ví dụ ảnh đại diện ở khung admin |
| `--color-ink` | `#1b1f24` | Chữ chính |
| `--color-ink-muted` | `#5b6470` | Chữ phụ, tiêu đề cột |
| `--color-ink-secondary` | `#3f4650` | Chữ của badge trung tính |
| `--color-border` | `#e3e6ea` | Viền thẻ, đường kẻ dưới tiêu đề cột |
| `--color-border-strong` | `#d5d9df` | Viền ô nhập và nút phụ |
| `--color-border-subtle` | `#f0f1f3` | Đường kẻ giữa các dòng bảng |
| `--color-neutral-soft` | `#eef0f3` | Nền badge trung tính, nền khi rê chuột |
| `--color-focus` | `#2a6fd6` | Viền focus |
| `--color-focus-ring` | `rgba(42, 111, 214, 0.18)` | Vòng sáng quanh ô đang nhập |
| `--color-link` | `#2a6fd6` | Link trong nội dung |
| `--color-success` | `#0f6b36` | Chữ trạng thái thành công |
| `--color-success-soft` | `#e3f5ea` | Nền badge thành công |
| `--color-warning` | `#8a5300` | Chữ trạng thái cảnh báo |
| `--color-warning-soft` | `#fff1d6` | Nền badge cảnh báo |
| `--color-danger` | `#b42318` | Chữ lỗi, viền ô lỗi |
| `--color-danger-soft` | `#fde7e4` | Nền badge lỗi |
| `--color-info` | `#1d4fae` | Chữ trạng thái thông tin |
| `--color-info-soft` | `#e5efff` | Nền badge thông tin |
| `--color-star` | `#f5a524` | Sao đánh giá |
| `--color-overlay` | `rgba(10, 14, 20, 0.45)` | Lớp phủ sau hộp thoại |

## Tương phản đo được

| Chữ | Nền | Tỉ lệ |
|---|---|---|
| `--color-ink` | `--color-page` | 15.18 |
| `--color-ink-muted` | `--color-page` | 5.50 |
| `--color-ink-muted` | `--color-surface-muted` | 5.59 |
| `--color-on-brand` | `--color-brand` | 5.38 |
| `--color-link` | `--color-surface` | 4.84 |
| `--color-danger` | `--color-danger-soft` | 5.55 |
| `--color-warning` | `--color-warning-soft` | 5.67 |
| `--color-success` | `--color-success-soft` | 5.83 |
| `--color-info` | `--color-info-soft` | 6.51 |

## Chữ, bo góc, khoảng cách, bóng

- Font: Be Vietnam Pro, tải từ Google Fonts trong `apps/frontend/index.html`. Font hệ thống dùng khi font chưa tải xong.
- Cỡ chữ: 12, 13, 14, 16, 20, 22, 28 px (`--text-xs`, `--text-label`, `--text-sm`, `--text-md`, `--text-lg`, `--text-title`, `--text-xl`).
- Tiêu đề trang (`h1` trong `.page-head`) dùng `--text-title`.
- Màn quản trị dùng chữ thân 14 px. Nhãn ô nhập, nút nhỏ và chữ trong bảng dùng 13 px.
- Bo góc: 8 px cho nút và ô nhập, 12 px cho thẻ, 14 px cho hộp thoại, tròn hẳn cho badge.
- Khoảng cách theo thang 4 px: 4, 8, 12, 16, 24, 32, 48, 64.
- Bóng: `--shadow-card` rất nhẹ cho thẻ nổi, `--shadow-popover` cho hộp thoại và menu thả.
- Khung admin dùng `--shadow-nav-active` cho mục menu đang chọn và `--shadow-drawer` cho ngăn kéo menu trên điện thoại.

## Component chung

| Lớp | Dùng cho | Màn mẫu |
|---|---|---|
| `.btn`, `.btn-primary`, `.btn-outline`, `.btn-ghost`, `.btn-danger`, `.btn-sm`, `.btn-block` | Nút | `shop-retail/admin/products.html` |
| `.field`, `.input-sm`, `.form-grid`, `.form-grid-2`, `.form-grid-3`, `.field-full` | Ô nhập và lưới form | `shop-retail/admin/product.html` |
| `.form-error`, `.hint`, `[aria-invalid='true']` | Lỗi và gợi ý dưới ô nhập | `shop-retail/admin/product.html` |
| `.badge`, `.badge-ok`, `.badge-warn`, `.badge-bad`, `.badge-info` | Trạng thái | `shop-retail/admin/orders.html` |
| `.card`, `.card-head`, `.card-flush` | Thẻ nội dung | `shop-retail/admin/index.html` |
| `.table-wrap`, `.tbl`, `.num` | Bảng dữ liệu | `shop-retail/admin/products.html` |
| `.sr`, `.muted`, `.small`, `.mono` | Tiện ích chữ | Mọi màn |
| `.icon` | Icon 18 px của component `Icon` | Mọi màn |
| `.page`, `.page-head`, `.page-head-text`, `.head-actions` | Khung trang và đầu trang | `shop-retail/admin/products.html` |
| `.stack`, `.stack-sm`, `.row-actions` | Xếp khối dọc và nhóm nút | Mọi màn |
| `.alert`, `.empty-state`, `.empty-state-dashed` | Lỗi, đang tải, danh sách rỗng | `shop-retail/admin/products.html` |
| `.alert-list`, `.alert-list-icon`, `.alert-list-body`, `.alert-list-title`, `.alert-list-heading`, `.alert-list-items` | Mọi thông báo lỗi dùng component `ErrorAlert` trong `apps/frontend/src/shared/error-alert.tsx`. Có icon, nội dung chính và danh sách lý do không chấm tròn. Dòng chính chỉ đậm khi có danh sách. Class bố cục riêng truyền qua prop `className`. Test `error-alert-usage.test.ts` chặn `role="alert"` kèm class `alert` ngoài `ErrorAlert` | Mẫu riêng của Pickory |
| `.filters-row`, `.pager`, `.cell-sub`, `.wrap-anywhere`, `.check` | Bộ lọc, phân trang và chữ phụ trong bảng | `shop-retail/admin/products.html` |
| `.icon-btn` | Nút chỉ có icon | `shop-retail/admin/index.html` |
| `.dialog-backdrop`, `.dialog`, `.dialog-lg`, `.dialog-head`, `.dialog-body`, `.dialog-foot` | Hộp thoại form | `shop-retail/admin/product.html` |
| `.alert-ok`, `.alert-warn` | Thông báo thành công và cảnh báo | `shop-retail/admin/settings.html` |
| `.kpis`, `.kpi`, `.kpi-value`, `.kpi-ok`, `.kpi-bad` | Thẻ số liệu | `shop-retail/admin/index.html` |
| `.link-btn` | Nút dạng link màu xanh | `shop-retail/admin/index.html` |
| `.role-picker` | Nhóm checkbox chọn vai trò, dùng với `fieldset` và `legend` | `shop-retail/admin/settings.html` |
| `.logo-mark` | Ô logo chữ P ở khung admin và màn đăng nhập | `shop-retail/admin/login.html` |
| `.forbidden-title` | Tiêu đề màn thiếu quyền của component `ForbiddenNotice` | Mọi màn admin |

Lưới form `.form-grid-2` và `.form-grid-3` đổi về một cột khi màn rộng dưới 768 px.

Màn thiếu quyền dùng component `ForbiddenNotice` trong `apps/frontend/src/shared/forbidden-notice.tsx`.

Lỗi nhập liệu hiện ngay dưới ô sai bằng `FieldError` và `fieldErrorProps` trong `apps/frontend/src/shared/field-error.tsx`. Ô sai nhận `aria-invalid` và `aria-describedby`. Lỗi trả về từ API hiện bằng `ErrorAlert` đầu form.

Màn tài khoản của khách dùng `AuthCard`, `PasswordInput`, `AuthDivider` và `GoogleSignInLink` trong `apps/frontend/src/features/identity/customer/auth-ui.tsx`.

Header của khách nằm ở `apps/frontend/src/shared/header.tsx`. Header dùng nền `--color-brand`, chữ `--color-on-brand` và nền rê chuột `--color-header-soft`.

Bảng `.tbl` chỉ tô kiểu tiêu đề cột cho `thead th`. Ô `th scope="row"` trong `tbody` hiện như ô dữ liệu in đậm.

`main.tsx` nạp `tokens.css`, `base.css` và `components.css` trước `App`. CSS riêng của từng màn nạp sau, nên ghi đè được lớp chung có cùng độ ưu tiên.

Màn đã chuyển sang design system không viết `style={{`. Test `test/unit/shared/styles/inline-styles.test.ts` giữ danh sách các thư mục đã chuyển.

## Kế hoạch áp dụng

Kế hoạch chi tiết ở `tasks/plan-design.md` và `tasks/todo-design.md`.

1. DS-01: token, lớp component chung và tài liệu này.
2. DS-02: khung admin theo `shop-retail/admin/index.html`. Khung nằm ở `features/identity/admin/admin-layout.tsx`.
3. DS-03: màn admin catalog.
4. DS-04: màn admin identity và AI.
5. DS-05: header và màn tài khoản của khách.
