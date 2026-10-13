# Todo: đổi design system theo `shop-retail`

Kế hoạch và quyết định: `tasks/design/plan.md`. Bản mẫu: `shop-retail/`. Mật khẩu trang admin mẫu là `demo`.

Mỗi task UI chụp ảnh ở 1280 px và 375 px. Mỗi task so ảnh với màn tương ứng của bản mẫu.

### DS-01: Token, CSS component chung và tài liệu design system
**Mô tả:** Thay giá trị token bằng bảng màu của `shop-retail/assets/css/admin.css` và `store.css`. Tạo CSS component chung cho nút, ô nhập, badge, thẻ và bảng. Viết lại `docs/design/design-system.md` theo hướng mới.
**Tiêu chí nghiệm thu:**
- [x] `tokens.css` có màu thương hiệu `#d70018`, nền `#f4f5f7` và các màu trạng thái của bản mẫu
- [x] Mỗi mã màu cứng của bản mẫu dùng trong Pickory có một token; CSS component không chứa mã màu
- [x] Lớp chung cho nút, ô nhập, badge, thẻ và bảng nằm trong `shared/styles/components.css`, có test hoặc ảnh chứng minh
- [x] Be Vietnam Pro tải từ Google Fonts; ảnh chụp cho thấy font đúng
- [x] Cặp chữ và nền chính đạt tương phản WCAG AA (4.5:1); báo cáo ghi tỉ lệ đo được
- [x] `docs/design/design-system.md` mô tả hướng mới, bảng token và nguyên tắc dùng màu đỏ; tài liệu trỏ tới `shop-retail/`, không trỏ tới `prototype/`
- [x] Bộ E2E của repo đạt
**Kiểm tra:** Vitest; Playwright có giao diện; script đo tương phản
**Phụ thuộc:** không · **File:** `apps/frontend/src/shared/styles/{tokens,base,components}.css`, `apps/frontend/index.html`, `apps/frontend/src/main.tsx`, `docs/design/design-system.md` · **Quy mô:** S
**Bằng chứng (2026-10-06):** Vitest đạt 225/225, gồm 56 test mới trong `test/unit/shared/styles/`.
Bốn đột biến đều làm test đỏ: mã hex trong `admin-layout.css`, chữ phụ `#9aa1ab`, đổi màu thương hiệu, bỏ lớp `.badge-info`.
Playwright đạt 5/5 ở chế độ có giao diện và chế độ ẩn. `document.fonts.check` xác nhận Be Vietnam Pro đã tải.
Cặp tương phản thấp nhất là `--color-link` trên nền thẻ, đạt 4.84:1. Bảng đầy đủ nằm trong `docs/design/design-system.md`.

### DS-02: Khung admin
**Mô tả:** Làm lại khung admin theo `shop-retail/admin/index.html`: sidebar sáng có icon, nhóm menu, nút thu gọn, khối người dùng ở đáy, thanh trên và menu trên điện thoại.
**Tiêu chí nghiệm thu:**
- [x] Sidebar chỉ có mục có route thật; mục đang mở có trạng thái chọn và `aria-current="page"`
- [x] Mục menu chỉ hiện khi nhân viên có quyền tương ứng, như hiện tại
- [x] Nút thu gọn đổi sidebar thành dải icon ở màn rộng; trạng thái giữ khi chuyển trang
- [x] Ở 375 px, nút Menu mở và đóng menu bằng chuột và bàn phím; trang không cuộn ngang
- [x] Icon dùng một bộ SVG nét mảnh thống nhất, có `aria-hidden="true"`
- [x] Playwright kiểm console 0 lỗi, 0 cảnh báo; bộ E2E của repo đạt
**Kiểm tra:** test component; Playwright có giao diện
**Phụ thuộc:** DS-01 · **File:** `features/identity/admin/{admin-layout.tsx,admin-layout.css,admin-navigation.ts}`, `shared/icons.tsx`, `e2e/admin-layout-mobile.spec.ts` · **Quy mô:** M
**Bằng chứng (2026-10-07):** Vitest đạt 238/238, gồm 10 test component mới trong `admin-layout.test.tsx`.
Playwright đạt 5/5 ở chế độ có giao diện và chế độ ẩn. Spec `admin-layout-mobile.spec.ts` thay số đo padding của khung cũ bằng kiểm tra thu gọn, Escape và cuộn ngang.
Ba đột biến đều làm test đỏ: bỏ `end` của Tổng quan, bỏ lọc quyền, bỏ `visibility: hidden` của ngăn kéo.
Màu chữ của mục đang chọn đo trong trình duyệt là `rgb(20, 23, 28)`, khớp `--color-dark`.

### DS-03: Màn admin catalog
**Mô tả:** Làm lại danh sách sản phẩm, form sản phẩm, danh mục và thương hiệu theo `shop-retail/admin/products.html` và `product.html`.
**Tiêu chí nghiệm thu:**
- [x] Bốn màn dùng lớp chung của DS-01; không còn `style={{` chứa màu, cỡ chữ hoặc khoảng cách cố định
- [x] Danh sách sản phẩm có thanh lọc trong thẻ, bảng đường kẻ mảnh và badge trạng thái theo bản mẫu
- [x] Form sản phẩm giữ mọi hành vi của UI-C5; test component và E2E của UI-C5 vẫn đạt
- [x] Ảnh 1280 px và 375 px không tràn trang; bảng rộng cuộn trong khung
**Kiểm tra:** Vitest; Playwright có giao diện
**Phụ thuộc:** DS-02 · **File:** `features/catalog/admin/*.tsx`, `features/catalog/admin/*.css` · **Quy mô:** M
**Bằng chứng (2026-10-07):** Vitest đạt 261/261. Test `inline-styles.test.ts` xác nhận `features/catalog/admin` không còn `style={`.
Playwright đạt 5/5 ở chế độ có giao diện và chế độ ẩn. Các spec catalog không phải đổi locator.
Hai đột biến làm test đỏ: thêm `style={{` vào màn thương hiệu, cho trạng thái focus thắng trạng thái lỗi của trình soạn mô tả.
Test focus của trình soạn mô tả trước đó không chờ sự kiện focus, nên đột biến thứ hai từng xanh. Test đã thêm bước chờ.
Ảnh 1280 px và 375 px không tràn trang. Bảng sản phẩm và bảng biến thể cuộn trong khung riêng.

### DS-04: Màn admin identity và AI
**Mô tả:** Làm lại tổng quan, khách hàng, nhân viên, role, nhật ký thao tác, cấu hình AI và đăng nhập admin theo `shop-retail/admin/`.
**Tiêu chí nghiệm thu:**
- [x] Bảy màn dùng lớp chung của DS-01; không còn `style={{` chứa màu, cỡ chữ hoặc khoảng cách cố định
- [x] Đăng nhập admin theo bố cục `shop-retail/admin/login.html`, giữ thông báo lỗi và luồng hiện tại
- [x] Hộp thoại giữ focus trap và phím Escape như hiện tại
- [x] Ảnh 1280 px và 375 px không tràn trang
- [x] Playwright kiểm console 0 lỗi, 0 cảnh báo; bộ E2E của repo đạt
**Kiểm tra:** Vitest; Playwright có giao diện
**Phụ thuộc:** DS-02 · **File:** `features/identity/admin/*`, `features/ai-gateway/admin/*`, `features/identity/components/{invite-dialog,assign-roles-dialog,role-form,permission-grid}.*`, `shared/forbidden-notice.tsx`, `e2e/admin-screens.spec.ts` · **Quy mô:** L
**Bằng chứng (2026-10-07):** Vitest đạt 277/277. Test `inline-styles.test.ts` xác nhận 7 màn và 4 component hộp thoại không còn `style={`.
Playwright đạt 6/6 ở chế độ có giao diện và chế độ ẩn. Spec mới `admin-screens.spec.ts` mở 7 màn ở 1280 px và 375 px.
Spec mới kiểm console sạch, không tràn trang, lỗi đăng nhập có `role="alert"` và hộp thoại không tràn ở 375 px.
Hai đột biến làm spec đỏ: bỏ `role="alert"` của lỗi đăng nhập, bỏ `legend` của nhóm vai trò.
Spec kiểm focus nằm trong hộp thoại mời khi mở. Escape đóng hộp thoại và trả focus về nút mở.

### DS-05: Màn khách
**Mô tả:** Làm lại header của khách theo `shop-retail/index.html` và các màn tài khoản theo `shop-retail/account/`.
**Tiêu chí nghiệm thu:**
- [x] Header nền đỏ có logo Pickory, liên kết đăng nhập hoặc tài khoản; không có mục chưa có màn
- [x] Đăng nhập, đăng ký, quên mật khẩu, đặt lại mật khẩu, xác minh email, tài khoản và quyền riêng tư dùng lớp chung của DS-01
- [x] Lỗi validate vẫn hiện ngay dưới ô sai, có nhãn truy cập được
- [x] Ảnh 1280 px và 375 px không tràn trang
- [x] Playwright kiểm console 0 lỗi, 0 cảnh báo; bộ E2E của repo đạt
**Kiểm tra:** Vitest; Playwright có giao diện
**Phụ thuộc:** DS-01 · **File:** `shared/{header,field-error}.tsx`, `shared/header.css`, `features/identity/customer/*`, `features/identity/components/{change-password-form,delete-account-dialog,consent-banner}.*`, `e2e/customer-screens.spec.ts` · **Quy mô:** M
**Bằng chứng (2026-10-07):** Vitest đạt 281/281, gồm 5 test lỗi theo ô trong `field-errors.test.tsx`.
Test `inline-styles.test.ts` giờ quét toàn bộ `src` và không còn `style={`.
Playwright đạt 7/7 ở chế độ có giao diện và chế độ ẩn. Spec mới `customer-screens.spec.ts` mở 6 màn công khai và màn tài khoản ở 1280 px và 375 px.
Spec mới kiểm header nền `rgb(215, 0, 24)`, hai nút banner cookie cùng kích thước, lỗi đăng nhập dưới ô email và không tràn trang.
Hai đột biến làm test đỏ: bỏ `aria-invalid` của ô email đăng nhập, đổi nền header sang màu tối.
Trước DS-05, lỗi nhập liệu chỉ hiện ở thông báo chung đầu form. DS-05 chuyển lỗi xuống dưới từng ô, lỗi API vẫn ở đầu form.

### DS-06: Ô nhập tiền theo VND
**Mô tả:** Các ô nhập số tiền hiện là ô số trần, không có dấu phân cách nghìn và không ghi đơn vị. Admin dễ gõ thừa hoặc thiếu một số 0. Task thêm một component ô nhập tiền dùng chung và thay vào mọi ô nhập số tiền.
**Tiêu chí nghiệm thu:**
- [x] Ô hiện dấu chấm phân cách nghìn khi gõ và hiện đơn vị `đ` cạnh ô. Giá trị gửi API vẫn là số nguyên đồng
- [x] Dán `1.250.000 đ` hoặc `1250000` đều ra 1.250.000. Ký tự khác số bị bỏ, con trỏ không nhảy về cuối khi sửa giữa số
- [x] Mọi ô số tiền của admin và bộ lọc giá của khách dùng component chung. Ô không phải tiền (tồn kho, cân nặng, bảo hành) giữ nguyên
- [x] Nhãn truy cập được có đơn vị; lỗi validate vẫn hiện dưới ô; điện thoại mở bàn phím số
- [x] Ảnh 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo; bộ E2E của repo đạt
**Kiểm tra:** Vitest; Playwright có giao diện
**Phụ thuộc:** DS-01 · **Quy mô:** M
