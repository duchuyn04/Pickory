# Task list: storefront-redesign

> Status: draft v1, soạn ngày 2026-11-24, chờ duyệt.
> Theo `spec/SPEC-storefront-redesign.md` và `tasks/storefront-redesign/plan.md`.

## Tóm tắt

Mười ba task đưa thiết kế mới vào code, chia theo bốn Phase. Làm Phase 0 trước. Sau Checkpoint 0, ba nhánh trang chủ, trang sản phẩm và đặt hàng thành công làm độc lập được.

Mỗi task có tiêu chí nghiệm thu dạng ô tick. Task chỉ xong khi mọi ô tick và lệnh kiểm tra đều đạt.

## Quy ước

- Bố cục theo ảnh trong `design/screens/`, giá trị theo `design/token.css`. Mỗi task ghi ảnh cần mở ở dòng "Màn hình". Mở ảnh đó trước khi viết code và so lại sau khi xong.
- Ảnh PNG rộng đúng cỡ màn hình (1440, 768 hoặc 375px), dài theo nội dung. Muốn xem chi tiết một khối thì mở ảnh ở kích thước thật và cắt riêng khối có tên ghi trong task.
- Mọi màu, cỡ chữ, bo góc, khoảng cách đi qua biến trong `tokens.css`. Không viết mã cứng.
- Không đổi API, route, DB hay quy tắc nghiệp vụ. Không thêm package.
- Không bịa giảm giá, đánh giá hay số lượt mua khi API không có.
- Comment tiếng Việt, giải thích lý do.
- Task frontend gọi skill `evon:ui-ux`. Reviewer cũng dùng skill đó khi xem ảnh.
- Task có luồng nhiều bước (SFR-11) phải kèm video luồng và ảnh.
- Tick task trong `plan.md` và `todo.md` ngay khi đạt. Cập nhật `tasks/README.md` khi đổi Phase.
- Không commit nếu người dùng chưa yêu cầu.

## SFR-01: Token mới

**Mô tả:** Thêm các token đánh dấu "MỚI" trong `design/token.css` vào `apps/frontend/src/shared/styles/tokens.css`.

**Màn hình:** task này không có ảnh riêng, chỉ có bảng token. File chuẩn:

design/token.css

**Acceptance criteria:**

- [x] Có `--color-success-solid`, `--color-scrim-light`, nhóm `--color-cat-*`, nhóm `--color-small-banner-*`.
- [x] Có các bậc chữ `--text-2xs`, `--text-3xs`, `--text-price`, `--text-2xl`, `--text-3xl`, `--text-hero`.
- [x] Có `--space-1-5`, `--space-2-5`, `--space-3-5` và nhóm `--layout-*`.
- [x] Token đã có giữ nguyên giá trị. Mỗi token mới có chú thích tiếng Việt nêu dùng ở đâu.
- [x] Test token kiểm các cặp chữ và nền mới đạt AA (4.5:1).

**Verification:**

- [x] `pnpm test` pass, gồm `css-colors.test.ts` và test token.
- [x] `pnpm typecheck` pass.

**Dependencies:** Không.

**Files:** `shared/styles/tokens.css`, test token.

**Scope:** S.

## SFR-02: Header hai tầng

**Mô tả:** Đổi `shared/header.tsx` theo thiết kế: thanh tiện ích và thanh chính. Bản dưới 768px chỉ còn logo, ô tìm kiếm và giỏ hàng.

**Màn hình:** task này chỉ làm phần header ở đầu mỗi ảnh (thanh tiện ích và thanh chính). Header trên trang sản phẩm và đặt hàng thành công giống hệt. Ảnh chuẩn:

design/screens/trang-chu-may-tinh-1440.png

design/screens/trang-chu-may-tinh-bang-768.png

design/screens/trang-chu-dien-thoai-375.png

**Acceptance criteria:**

- [x] Thanh tiện ích có hai cam kết bên trái và bốn liên kết bên phải, ẩn dưới 768px.
- [x] Thanh chính có logo, nút Danh mục mở `CategoryMenu`, ô tìm kiếm, Đăng nhập, Đăng ký, Giỏ hàng. Khách đã đăng nhập thấy tên tài khoản và Đăng xuất như hiện tại.
- [x] Dưới 768px: logo, ô tìm kiếm và giỏ hàng. Mục còn lại nằm trong ngăn kéo menu có sẵn.
- [x] Giỏ hàng hiện số lượng như hiện tại, mini cart vẫn mở được.
- [x] Focus bàn phím, `Escape` đóng menu, trả focus về nút mở. Các hành vi này giữ nguyên.

**Verification:**

- [x] `pnpm test` và `customer-header-mobile.spec.ts` pass.
- [x] Ảnh chụp 1440, 768, 375px khớp phần header của ba ảnh thiết kế.

**Dependencies:** SFR-01.

**Files:** `shared/header.tsx`, `shared/header.css`, `shared/category-menu.tsx`, test header.

**Scope:** M.

## SFR-03: Footer bốn cột

**Mô tả:** Đổi `shared/footer.tsx` thành footer có bốn cột và dòng bản quyền.

**Màn hình:** task này chỉ làm phần footer ở cuối mỗi ảnh (bốn cột và dòng bản quyền). Ảnh chuẩn:

design/screens/trang-chu-may-tinh-1440.png

design/screens/trang-chu-may-tinh-bang-768.png

design/screens/trang-chu-dien-thoai-375.png

**Acceptance criteria:**

- [x] Cột giới thiệu, hỗ trợ khách hàng (Tra cứu đơn hàng, Phí giao hàng, Liên hệ CSKH), chính sách (Quyền riêng tư, Chính sách bảo hành, Điều khoản sử dụng), danh mục (năm danh mục gốc).
- [x] Link chính sách nào chưa có route thì trỏ về trang gần nhất đang có, ghi trong chú thích. Không tạo route mới.
- [x] Dưới 768px xếp một cột. Ở 768px hai cột.
- [x] Dòng bản quyền có "© 2026 Pickory" và dòng bảo hành.

**Verification:**

- [x] `pnpm test` pass. Ảnh chụp ba cỡ khớp footer trong ảnh thiết kế.

**Dependencies:** SFR-01.

**Files:** `shared/footer.tsx`, `shared/footer.css`, test footer.

**Scope:** S.

### Checkpoint 0

- [ ] `pnpm typecheck`, `pnpm lint`, `pnpm test` pass.
- [ ] Chụp danh mục, giỏ hàng, thanh toán, tài khoản, tra cứu đơn ở ba cỡ, không vỡ bố cục.
- [ ] Tick SFR-01 đến SFR-03 trong `plan.md`.

## SFR-04: Khối đầu trang chủ

**Mô tả:** Dựng menu danh mục bên trái, banner nổi bật có tab sản phẩm và ba banner nhỏ.

**Màn hình:** task này làm khối đầu trang chủ: menu danh mục, banner nổi bật có tab và ba banner nhỏ. Ảnh chuẩn:

design/screens/trang-chu-may-tinh-1440.png

design/screens/trang-chu-may-tinh-bang-768.png

design/screens/trang-chu-dien-thoai-375.png

**Acceptance criteria:**

- [ ] Menu danh mục 224px liệt kê danh mục gốc từ `useCategoryTree`, kèm các mục Tra cứu đơn hàng, Phí giao hàng, So sánh sản phẩm. Ẩn dưới 1200px.
- [ ] Banner nổi bật có một tab cho mỗi danh mục gốc, hiện sản phẩm mới nhất kèm hãng, giá từ và nút Xem chi tiết. Ảnh dùng nền trong suốt khi có.
- [ ] Tab chuyển bằng bàn phím (mũi tên) như `carousel-index.ts` hiện có. Dưới 768px ẩn tab và mũi tên, giữ chấm trang.
- [ ] Ba banner nhỏ: một hàng ở 768px, xếp dọc ở 375px. Mỗi banner có nhãn, tiêu đề tối đa hai dòng, mô tả một dòng và ảnh 76px.
- [ ] Danh mục rỗng không có tab. Lỗi tải hiện `ErrorAlert` với nút Thử lại như hiện tại.

**Verification:**

- [ ] `pnpm test` pass. Ảnh chụp ba cỡ khớp phần khối đầu của ba ảnh trang chủ.

**Dependencies:** Checkpoint 0.

**Files:** `home-page.tsx`, `banner-carousel.tsx`, `promo-banners.tsx`, `home-page.css`.

**Scope:** M.

## SFR-05: Cột phải và dải thông báo

**Mô tả:** Dựng cột phải (Mới lên kệ, Mua theo thương hiệu, gợi ý so sánh) và dải thông báo phí giao hàng.

**Màn hình:** task này làm cột phải (Mới lên kệ, Mua theo thương hiệu, gợi ý so sánh, chỉ có ở ảnh 1440) và dải thông báo phí giao hàng. Ảnh chuẩn:

design/screens/trang-chu-may-tinh-1440.png

design/screens/trang-chu-may-tinh-bang-768.png

design/screens/trang-chu-dien-thoai-375.png

**Acceptance criteria:**

- [ ] Mới lên kệ hiện ba sản phẩm mới nhất của cửa hàng, mỗi dòng có ảnh, tên hai dòng và giá. Mỗi dòng là link tới `/p/:slug`.
- [ ] Mua theo thương hiệu hiện tên hãng dạng chữ, mỗi hãng là link tới danh mục lọc theo hãng.
- [ ] Gợi ý so sánh nói tối đa 4 sản phẩm và link tới `/compare`.
- [ ] Cột phải ẩn dưới 1200px.
- [ ] Dải thông báo có tiêu đề, một dòng mô tả và nút tới `/policy/giao-hang`. Ở 375px nút xuống dưới chữ.

**Verification:**

- [ ] `pnpm test` pass. Ảnh chụp khớp.

**Dependencies:** SFR-04.

**Files:** `home-page.tsx`, `home-queries.ts`, `home-page.css`, file component cột phải mới.

**Scope:** M.

## SFR-06: Hàng mới về và thẻ sản phẩm

**Mô tả:** Dựng khối Hàng mới về với chip danh mục và thẻ sản phẩm mới dùng chung cho cả trang chủ.

**Màn hình:** task này làm khối Hàng mới về và thẻ sản phẩm. Thẻ sản phẩm cũng xuất hiện ở khối "Sản phẩm cùng danh mục" của trang sản phẩm. Ảnh chuẩn:

design/screens/trang-chu-may-tinh-1440.png

design/screens/trang-chu-may-tinh-bang-768.png

design/screens/trang-chu-dien-thoai-375.png

design/screens/san-pham-may-tinh-1440.png

**Acceptance criteria:**

- [ ] Chip danh mục lọc dải sản phẩm. Chip đang chọn dùng `--color-brand-soft` và viền `--color-brand`.
- [ ] Dải sản phẩm cuộn ngang, nút trái phải ẩn dưới 768px.
- [ ] Thẻ sản phẩm có ảnh vuông, hãng, tên hai dòng rồi dấu ba chấm, giá thấp nhất, chip tình trạng và nút so sánh. Không có phần trăm giảm giá hay số sao.
- [ ] Sản phẩm hết hàng hiện chip "Hết hàng". Không có giá thì hiện "Liên hệ" như hiện tại.
- [ ] Nút so sánh dùng `compare-control.tsx`, giữ giới hạn `COMPARE_LIST_LIMIT`.

**Verification:**

- [ ] `pnpm test` pass. Ảnh chụp khớp.

**Dependencies:** SFR-04.

**Files:** `product-card.tsx`, `product-card.css`, `product-grid.tsx`, `home-page.css`.

**Scope:** M.

## SFR-07: Khối danh mục, thương hiệu, responsive

**Mô tả:** Dựng năm khối danh mục, khối thương hiệu nổi bật và hoàn thiện responsive cho cả trang chủ.

**Màn hình:** task này làm năm khối danh mục (Điện thoại, Laptop, Màn hình, Tai nghe, Phụ kiện) và khối Thương hiệu nổi bật. Ảnh chuẩn:

design/screens/trang-chu-may-tinh-1440.png

design/screens/trang-chu-may-tinh-bang-768.png

design/screens/trang-chu-dien-thoai-375.png

**Acceptance criteria:**

- [ ] Mỗi khối có banner trái 224px (nền chuyển từ màu danh mục sang trắng, ảnh sản phẩm ở đáy, tên, giá từ, nút Xem ngay) và lưới bốn thẻ mỗi hàng, hai hàng ở 1440px.
- [ ] Banner trái cao tối đa 480px, không kéo dài theo lưới.
- [ ] Màu nền và màu nhấn theo nhóm `--color-cat-*`. Chữ trên banner đạt AA.
- [ ] Chip hãng và liên kết Xem tất cả mở `/c/:slug` với bộ lọc hãng tương ứng.
- [ ] Ở 768px lưới giữ bốn thẻ nhỏ hơn. Ở 375px banner nằm trên, lưới hai thẻ mỗi hàng và chỉ một hàng.
- [ ] Khối thương hiệu nổi bật cuộn ngang ở màn nhỏ.
- [ ] CSS cũ của băng chuyền và lưới trang chủ không còn dùng thì xóa. Ảnh chụp trước và sau mỗi lần xóa.
- [ ] Không thanh cuộn ngang ở 1440, 768, 375px.
- [ ] Cập nhật `home-page.test.tsx` và e2e trang chủ theo cấu trúc mới.

**Verification:**

- [ ] `pnpm typecheck`, `pnpm lint`, `pnpm test` pass. E2E trang chủ pass.
- [ ] Ảnh chụp ba cỡ khớp ba ảnh `trang-chu-*`.

**Dependencies:** SFR-05, SFR-06.

**Files:** `home-page.tsx`, `home-page.css`, `home-page.test.tsx`, e2e trang chủ.

**Scope:** L.

### Checkpoint 1

- [ ] Trang chủ khớp ba ảnh. Không cuộn ngang.
- [ ] Test và e2e pass. Review với người dùng.
- [ ] Tick SFR-04 đến SFR-07.

## SFR-08: Trang sản phẩm, khung và thư viện ảnh

**Mô tả:** Dựng khung hai cột 592px, tiêu đề, đường dẫn và thư viện ảnh.

**Màn hình:** task này làm phần trên của trang sản phẩm: đường dẫn, tiêu đề và thư viện ảnh. Ảnh chuẩn:

design/screens/san-pham-may-tinh-1440.png

design/screens/san-pham-may-tinh-bang-768.png

design/screens/san-pham-dien-thoai-375.png

**Acceptance criteria:**

- [ ] Đường dẫn "Trang chủ > Danh mục > Tên sản phẩm" như thiết kế.
- [ ] Tiêu đề và liên kết nhanh (Mô tả, Thông số, So sánh, Bảo hành) ở màn 1440px. Liên kết nhanh ẩn dưới 1200px.
- [ ] Ảnh chính cao 340px ở 1440px, 280px ở 375px, có nút trái phải ở 1440px. Ảnh nhỏ dưới ảnh chính, chọn ảnh nhỏ đổi ảnh chính.
- [ ] Dưới 1200px cột phải nằm ngay sau thư viện ảnh.
- [ ] Sản phẩm không tìm thấy hiện thông báo và nút Về trang chủ như hiện tại.

**Verification:**

- [ ] `pnpm test` pass. Ảnh chụp khớp phần trên của ba ảnh sản phẩm.

**Dependencies:** Checkpoint 0.

**Files:** `product-view.tsx`, `product-gallery.tsx`, `product-page.css`.

**Scope:** M.

## SFR-09: Cột mua

**Mô tả:** Dựng khối giá, chọn bộ nhớ, màu, số lượng, nút mua và chính sách bán hàng.

**Màn hình:** task này làm cột mua: giá, bộ nhớ, màu, số lượng, nút mua, chính sách bán hàng và điểm nổi bật. Ảnh chuẩn:

design/screens/san-pham-may-tinh-1440.png

design/screens/san-pham-may-tinh-bang-768.png

design/screens/san-pham-dien-thoai-375.png

**Acceptance criteria:**

- [ ] Giá lớn màu `--color-price`, chip còn hàng và dòng "Còn N sản phẩm", mã phiên bản dưới đường đứt.
- [ ] Mỗi tùy chọn là thẻ chọn, thẻ đang chọn có viền `--color-brand` và dấu tích. Ở 375px hai thẻ mỗi hàng.
- [ ] Bộ chỉnh số lượng có nút trừ, ô số và nút cộng, giới hạn theo tồn kho.
- [ ] Nút MUA NGAY là nút chính màu đỏ, nút Thêm vào giỏ là nút phụ viền. Ở 375px xếp dọc.
- [ ] Biến thể hết hàng khóa cả hai nút. Sản phẩm ngừng kinh doanh hiện thông báo thay cho nút. Hai hành vi này giữ nguyên.
- [ ] Khối chính sách bán hàng và Điểm nổi bật hiện như thiết kế.

**Verification:**

- [ ] `pnpm test` pass. E2E chọn biến thể và thêm vào giỏ pass.
- [ ] Ảnh chụp khớp phần cột phải của ba ảnh.

**Dependencies:** SFR-08.

**Files:** `variant-picker.tsx`, `product-view.tsx`, `add-to-cart-button.tsx`, `product-page.css`.

**Scope:** M.

## SFR-10: Thông số, mô tả, sản phẩm cùng danh mục

**Mô tả:** Dựng cam kết sản phẩm, bảng thông số, nút so sánh, mô tả và dải sản phẩm cùng danh mục, rồi hoàn thiện responsive.

**Màn hình:** task này làm phần dưới của trang sản phẩm: cam kết, bảng thông số, nút so sánh, mô tả và sản phẩm cùng danh mục. Ảnh chuẩn:

design/screens/san-pham-may-tinh-1440.png

design/screens/san-pham-may-tinh-bang-768.png

design/screens/san-pham-dien-thoai-375.png

**Acceptance criteria:**

- [ ] Cam kết sản phẩm hai thẻ, xếp dọc ở 375px.
- [ ] Bảng thông số: nhãn bên trái, giá trị bên phải, dòng xen kẽ nền. Ở 375px nhãn rộng 120px và xuống dòng được.
- [ ] Mô tả dùng `description-view.tsx`, tab Mô tả và Thông số.
- [ ] Sản phẩm cùng danh mục dùng thẻ sản phẩm của SFR-06, cuộn ngang.
- [ ] Không thanh cuộn ngang ở 1440, 768, 375px.
- [ ] Cập nhật `catalog-product-page.spec.ts` theo cấu trúc mới.

**Verification:**

- [ ] `pnpm typecheck`, `pnpm lint`, `pnpm test` pass. E2E trang sản phẩm pass.
- [ ] Ảnh chụp ba cỡ khớp ba ảnh `san-pham-*`.

**Dependencies:** SFR-09, SFR-06.

**Files:** `spec-table.tsx`, `description-view.tsx`, `product-page.css`, `catalog-product-page.spec.ts`.

**Scope:** M.

### Checkpoint 2

- [ ] Trang sản phẩm khớp ba ảnh. Hành vi cũ còn nguyên.
- [ ] E2E pass. Review với người dùng.
- [ ] Tick SFR-08 đến SFR-10.

## SFR-11: Trang đặt hàng thành công

**Mô tả:** Dựng thanh bước, thẻ xác nhận, sản phẩm đã đặt, các bước tiếp theo, tóm tắt thanh toán và địa chỉ giao.

**Màn hình:** task này làm cả trang đặt hàng thành công. Chưa có ảnh 768px, dùng bố cục máy tính co lại. Ảnh chuẩn:

design/screens/dat-hang-thanh-cong-may-tinh-1440.png

design/screens/dat-hang-thanh-cong-dien-thoai-375.png

**Acceptance criteria:**

- [ ] Thanh ba bước đặt hàng, cả ba đã hoàn tất, dấu tích dùng `--color-success-solid`.
- [ ] Thẻ xác nhận có dấu tích, tiêu đề, email, mã đơn trong ô riêng kèm nút sao chép (thông báo "Đã sao chép" hoặc "Không sao chép được" như hiện tại), Xem đơn hàng và Tiếp tục mua sắm.
- [ ] Sản phẩm đã đặt dùng `OrderItemsList`, mỗi dòng có ảnh thật, tên, số lượng, giá. Chỉ một dòng giá.
- [ ] Các bước tiếp theo là timeline dọc theo `buildNextSteps`. Bước xong, đang diễn ra, sắp tới có chữ đọc thêm cho trình đọc màn hình như hiện tại.
- [ ] Cột phải 392px có tóm tắt thanh toán (`OrderPaymentRows`) và địa chỉ giao (`formatShippingAddress`).
- [ ] Dưới 768px cột phải nằm dưới cột chính. Khung giữ chỗ khi tải cùng bố cục với trang thật.
- [ ] Giữ quy tắc `trackingCode`, `paymentConfirmed` và liên kết từ `buildOrderSuccessLink`.
- [ ] Cập nhật `commerce-checkout.spec.ts` theo cấu trúc mới.

**Verification:**

- [ ] `pnpm typecheck`, `pnpm lint`, `pnpm test` pass. `commerce-checkout.spec.ts` pass.
- [ ] Video luồng đặt hàng tới trang thành công và ảnh 1440, 375px khớp hai ảnh `dat-hang-thanh-cong-*`.

**Dependencies:** Checkpoint 0.

**Files:** `order-success-page.tsx`, `order-sections.tsx`, `checkout-page.css`, `commerce-checkout.spec.ts`.

**Scope:** M.

### Checkpoint 3

- [ ] Khớp hai ảnh. Video và ảnh đã nộp.
- [ ] Review với người dùng. Tick SFR-11.

## SFR-12: Nghiệm thu so hình

**Mô tả:** Thêm spec Playwright chụp ba màn ở ba cỡ để so với `design/screens/` và kiểm tra không có thanh cuộn ngang.

**Màn hình:** task này so cả ba màn với thiết kế. Bảng màn hình và cỡ nằm ở design/README.md. Ảnh chuẩn:

design/screens/trang-chu-may-tinh-1440.png

design/screens/trang-chu-may-tinh-bang-768.png

design/screens/trang-chu-dien-thoai-375.png

design/screens/san-pham-may-tinh-1440.png

design/screens/san-pham-may-tinh-bang-768.png

design/screens/san-pham-dien-thoai-375.png

design/screens/dat-hang-thanh-cong-may-tinh-1440.png

design/screens/dat-hang-thanh-cong-dien-thoai-375.png

**Acceptance criteria:**

- [ ] Spec đặt trong thư mục mới `e2e/design-compare/`, chụp trang chủ, trang sản phẩm ở 1440, 768, 375px và trang đặt hàng thành công ở 1440, 375px.
- [ ] Mỗi lần chụp kiểm `document.documentElement.scrollWidth` không vượt chiều rộng cửa sổ.
- [ ] Ảnh chụp lưu cạnh ảnh thiết kế để người dùng xem đối chiếu.

**Verification:**

- [ ] `pnpm test:e2e` pass.

**Dependencies:** Checkpoint 1, 2, 3.

**Files:** spec so hình mới, helper chụp.

**Scope:** S.

## SFR-13: Cập nhật tài liệu

**Mô tả:** Ghi kết quả vào tài liệu.

**Màn hình:** task này chỉ sửa tài liệu, mục "Chỗ thiết kế còn lệch". File chuẩn:

design/README.md

**Acceptance criteria:**

- [ ] `design/README.md` ghi ngày xong và mục "Chỗ thiết kế còn lệch" đã xử lý hoặc còn lại.
- [ ] `tasks/README.md` có dòng `storefront-redesign/` với trạng thái đúng.
- [ ] Các câu hỏi mở của spec có ghi kết quả chốt.

**Verification:**

- [ ] Đọc lại ba file, không còn trạng thái cũ.

**Dependencies:** SFR-12.

**Files:** `design/README.md`, `tasks/README.md`, `spec/SPEC-storefront-redesign.md`.

**Scope:** S.

### Checkpoint cuối

- [ ] Các tiêu chí của spec có bằng chứng.
- [ ] `pnpm typecheck`, `pnpm lint`, `pnpm test`, `pnpm test:e2e` pass.
- [ ] Review với người dùng.
