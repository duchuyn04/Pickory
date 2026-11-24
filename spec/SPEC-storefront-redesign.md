# Spec: storefront-redesign

> Status: v1, chờ duyệt.
> Phụ thuộc: `catalog`, `commerce`, `identity`. Quy ước chung: `SPEC.md`.
> Thiết kế nguồn: `design/README.md`, `design/screens/`, `design/token.css`.

## Tóm tắt

Pickory có bản thiết kế mới cho ba nhóm màn hình phía khách: trang chủ, trang sản phẩm và trang đặt hàng thành công. Bố cục lấy theo cellphones.com.vn, màu và font giữ nguyên của Pickory. Mỗi màn có ba cỡ: máy tính 1440px, máy tính bảng 768px và điện thoại 375px (trang đặt hàng thành công chỉ có máy tính và điện thoại).

Spec này nói cách đưa thiết kế đó vào code. Việc làm gồm đổi header và footer dùng chung, dựng lại trang chủ, trang sản phẩm và trang đặt hàng thành công, rồi thêm token mới vào `tokens.css`. Các màn khác (danh mục, giỏ hàng, thanh toán, tài khoản, quản trị) chưa có thiết kế mới nên giữ nguyên.

Ví dụ: hôm nay trang chủ có một băng chuyền banner theo từng sản phẩm, rồi tới các hàng sản phẩm. Sau thay đổi, trang chủ có menu danh mục bên trái, banner nổi bật ở giữa, cột "Mới lên kệ" bên phải, rồi năm khối danh mục mỗi khối có banner riêng. Dữ liệu vẫn lấy từ API sản phẩm hiện có. Thiết kế không hiện giảm giá hay số sao, vì API chưa có hai dữ liệu đó.

Công việc chia thành bốn Phase: nền chung (token, header, footer, breakpoint), trang chủ, trang sản phẩm, trang đặt hàng thành công. Sau mỗi Phase, ứng dụng vẫn build và test được, và các màn chưa làm vẫn chạy như cũ.

## Assumptions

1. Thiết kế trong `design/screens/` là nguồn sự thật cho bố cục. Khi ảnh và code khác nhau, ảnh thắng, trừ các mục ở "Chỗ thiết kế còn lệch" của `design/README.md`.
2. Chỉ đổi giao diện. Không đổi API, DB, route, luồng đặt hàng hay quy tắc nghiệp vụ.
3. Không thêm dependency. React, React Router, TanStack Query và CSS thường đủ cho thiết kế này.
4. Mọi màu, cỡ chữ, bo góc và khoảng cách đi qua biến trong `tokens.css`. Test `css-colors.test.ts` đang chặn mã màu cứng ngoài file đó và vẫn giữ.
5. Dữ liệu hiển thị chỉ gồm trường API đã trả. Không bịa phần trăm giảm giá, giá gạch, số sao hay số lượt mua.
6. Ba cỡ màn hình chốt như sau: điện thoại dưới 768px, máy tính bảng từ 768px đến dưới 1200px, máy tính từ 1200px. Nội dung rộng tối đa 1200px, căn giữa.
7. Ảnh sản phẩm trên banner nổi bật và ba banner nhỏ có nền trong suốt. Ảnh trong lưới sản phẩm vẫn có nền trắng vì nằm trên thẻ trắng.
8. Trang đặt hàng thành công không có bản máy tính bảng. Ở 768px dùng bố cục máy tính co lại một cột.

## Objective

Khách thấy giao diện mới, quen với bố cục của các cửa hàng điện máy, và dùng được trên điện thoại, máy tính bảng và máy tính.

User stories:

- Là khách trên điện thoại, tôi mở trang chủ và thấy banner, danh mục và sản phẩm xếp một cột, không phải kéo ngang cả trang.
- Là khách trên máy tính, tôi chọn danh mục từ menu bên trái trang chủ mà không phải mở menu thả xuống.
- Là khách xem một sản phẩm, tôi thấy giá và nút mua ngay sau ảnh, không cuộn qua bảng thông số mới thấy.
- Là khách vừa đặt hàng, tôi thấy mã đơn, cách sao chép mã, các bước tiếp theo và địa chỉ nhận hàng trên một màn.
- Là người phát triển, tôi có một bảng token và một danh sách màn hình để đối chiếu, không phải đo lại từ ảnh.

## Tech Stack

| Phần | Lựa chọn |
|---|---|
| Frontend | React 19, Vite, TypeScript (có sẵn) |
| Route | React Router 7 (có sẵn), không đổi route |
| Dữ liệu | TanStack Query (có sẵn), dùng lại hook của trang chủ và trang sản phẩm |
| Style | CSS thường, token trong `apps/frontend/src/shared/styles/tokens.css` |
| Font | Inter (có sẵn) |
| Test | Vitest, Testing Library, Playwright (có sẵn) |

Không thêm package.

## Commands

Chạy từ `apps/frontend/`:

```powershell
pnpm dev
pnpm typecheck
pnpm lint
pnpm test
pnpm test:e2e
```

Mở `http://localhost:5173` để xem. Đổi kích thước cửa sổ về 1440, 768 và 375px để so với ảnh trong `design/screens/`.

## Project Structure

```text
design/
  screens/                         -> 8 ảnh PNG, nguồn thiết kế duy nhất
  token.css                        -> Token đo từ thiết kế
  README.md                        -> Bảng màn hình và ghi chú
apps/frontend/src/
  shared/
    styles/tokens.css              -> Thêm token mới (Phase 0)
    header.tsx, header.css         -> Đổi sang header hai tầng
    footer.tsx, footer.css         -> Đổi sang footer bốn cột
    category-menu.tsx              -> Dùng lại cho menu danh mục
  features/home/
    home-page.tsx, home-page.css   -> Dựng lại theo bố cục mới
    banner-carousel.tsx            -> Giữ banner nổi bật hiện có, chỉnh kích thước cho vừa khung giữa
    promo-banners.tsx              -> Đổi thành ba banner nhỏ
    product-grid.tsx               -> Lưới bốn cột, hai cột ở 375px
  features/catalog/customer/
    product-view.tsx, product-page.css -> Dựng lại hai cột, xếp một cột ở màn nhỏ
    product-gallery.tsx, variant-picker.tsx, spec-table.tsx
  features/commerce/customer/checkout/
    order-success-page.tsx, checkout-page.css -> Dựng lại thẻ thành công, timeline, cột tóm tắt
```

Component mới đặt cạnh component cùng tính năng, không tạo thư mục `redesign/`.

## Chuyển từng màn

### Nền chung (Phase 0)

- Thêm vào `tokens.css` các token đánh dấu "MỚI" trong `design/token.css`. Token đã có giữ nguyên.
- Thêm các mốc bố cục thành hằng trong CSS dùng chung. Media query ghi thẳng số 768px và 1200px, kèm chú thích trỏ về `--layout-breakpoint-*`. Code hiện dùng nhiều mốc khác (767, 1023, 1199...), nên chỉ đổi mốc ở các file thuộc ba màn này.
- Header: thanh tiện ích (cam kết, liên kết nhanh) và thanh chính (logo, nút Danh mục, ô tìm kiếm, Đăng nhập, Đăng ký, Giỏ hàng). Ở dưới 768px chỉ còn logo, ô tìm kiếm và giỏ hàng. Các mục còn lại vào ngăn kéo menu có sẵn.
- Footer: bốn cột (giới thiệu, hỗ trợ khách hàng, chính sách, danh mục) và dòng bản quyền. Ở dưới 768px xếp một cột.
- Header và footer là phần của `CustomerLayout`, nên đổi một lần là cả các màn chưa thiết kế lại cũng đổi theo. Phase 0 phải kiểm các màn đó không vỡ bố cục.

### Trang chủ (Phase 1)

| Khối | Dữ liệu | Ghi chú |
|---|---|---|
| Menu danh mục (trái, 224px) | `GET /api/categories` | Ẩn dưới 1200px |
| Banner nổi bật (giữa) | Các banner hiện có trong `home-banners.ts` | Giữ nội dung và ảnh cũ, chỉ chỉnh kích thước cho vừa khung 736px và hai cỡ nhỏ |
| Ba banner nhỏ | Cấu hình tĩnh trong `home-content.ts` | Một hàng ở 768px, xếp dọc ở 375px |
| Cột phải (224px) | Mới lên kệ: 3 sản phẩm mới nhất. Mua theo thương hiệu: danh sách hãng | Ẩn dưới 1200px |
| Dải thông báo phí giao hàng | Tĩnh, link tới `/policy/giao-hang` | |
| Hàng mới về | `/api/products?sort=newest` theo chip danh mục | Cuộn ngang |
| Năm khối danh mục | `/api/products?category=<slug>&sort=newest&pageSize=...` | Banner trái 224px, lưới bốn thẻ. Ở 375px: banner trên, hai thẻ mỗi hàng, một hàng |
| Thương hiệu nổi bật | Facet hãng từ API | Cuộn ngang ở màn nhỏ |

Thẻ sản phẩm có ảnh, hãng, tên (tối đa hai dòng), giá thấp nhất, chip tình trạng và nút so sánh. Nút so sánh dùng lại `compare-control.tsx`.

### Trang sản phẩm (Phase 2)

- Màn 1440px: hai cột 592px. Cột trái có tên, thư viện ảnh, cam kết, thông số kỹ thuật, nút so sánh. Cột phải có giá và tình trạng kho, bộ nhớ, màu, số lượng, MUA NGAY, Thêm vào giỏ, chính sách bán hàng.
- Màn 768px và 375px: một cột theo thứ tự tên, ảnh, giá, tùy chọn, nút mua, chính sách, cam kết, thông số.
- Dùng lại `variant-selection.ts`, `gallery-images.ts`, `spec-format.ts` và các hook hiện có. Chỉ đổi phần hiển thị.
- Giữ nguyên hành vi: chọn biến thể đổi giá và tồn kho, biến thể hết hàng khóa nút mua, sản phẩm ngừng kinh doanh hiện thông báo thay cho nút mua.

### Trang đặt hàng thành công (Phase 3)

- Thanh ba bước đặt hàng ở trên, cả ba đã hoàn tất.
- Cột chính: thẻ xác nhận (dấu tích, email, mã đơn kèm nút sao chép, Xem đơn hàng, Tiếp tục mua sắm), sản phẩm đã đặt, các bước tiếp theo.
- Cột phải 392px: tóm tắt thanh toán (tạm tính, phí vận chuyển, miễn phí vận chuyển, tổng cộng, phương thức) và địa chỉ giao.
- Giữ nguyên `buildNextSteps`, `buildOrderSuccessLink`, quy tắc `trackingCode` và chữ đọc thêm cho trình đọc màn hình của từng trạng thái bước.
- Dưới 768px cột phải nằm dưới cột chính.

## Code Style

- Tên class theo tên tính năng như code hiện tại (`home-`, `product-`, `order-`). Không đổi cấu trúc BEM sẵn có.
- Chú thích tiếng Việt, giải thích lý do thay vì chép lại code.
- Component chỉ nhận dữ liệu qua props hoặc hook hiện có. Không gọi API mới.
- Hình dùng thẻ `img` với `alt` thật. Icon dùng `shared/icons.tsx`. Không emoji.
- Mỗi vùng chỉ có một nút chính màu đỏ.

## Testing Strategy

- Unit (Vitest): cập nhật `test/unit/features/home/home-page.test.tsx` theo cấu trúc mới. Thêm test cho các hàm chọn sản phẩm của "Mới lên kệ" và banner nổi bật nếu có logic mới.
- E2E (Playwright): cập nhật `catalog-product-page.spec.ts`, `commerce-checkout.spec.ts` và `customer-header-mobile.spec.ts`. Thêm kiểm tra không có thanh cuộn ngang ở 375px và 768px cho ba màn.
- So hình: thêm spec trong thư mục mới `e2e/design-compare/`, chụp ba màn ở 1440, 768 và 375px rồi so với ảnh trong `design/screens/`. Sai lệch chấp nhận được tới mức người dùng duyệt ở Open Questions.
- Video và ảnh: task có luồng nhiều bước (đặt hàng thành công) phải quay video luồng và chụp ảnh ba cỡ khi nộp review.
- Trước khi coi một Phase xong: `pnpm typecheck`, `pnpm lint`, `pnpm test` và các e2e liên quan đều đạt.

## Boundaries

Luôn làm:

- Dùng token, không viết màu hoặc cỡ chữ cứng.
- Chạy kiểm tra ba cỡ màn hình sau mỗi màn.
- Giữ nguyên test hành vi hiện có. Test nào đổi vì cấu trúc DOM đổi thì ghi lý do trong commit.

Hỏi trước:

- Thêm trường dữ liệu mới vào API hoặc DB (giảm giá, số sao).
- Đổi route hoặc tên query.
- Đổi header, footer của các màn chưa thiết kế lại nếu thấy chúng vỡ.

Không làm:

- Thêm package.
- Bịa dữ liệu hiển thị khi API không có.
- Đổi màn danh mục, so sánh, giỏ hàng, thanh toán, tài khoản và quản trị.
- Đổi quy tắc nghiệp vụ của đặt hàng và thanh toán.

## Success Criteria

- Ở 1440, 768 và 375px, trang chủ, trang sản phẩm và trang đặt hàng thành công có bố cục khớp ảnh trong `design/screens/`: cùng thứ tự khối, cột và số thẻ mỗi hàng.
- Không có thanh cuộn ngang ở ba màn, ba cỡ.
- Không còn mã màu cứng ngoài `tokens.css`. Test `css-colors.test.ts` đạt.
- Hành vi cũ còn nguyên: chọn biến thể, khóa nút mua khi hết hàng, so sánh, sao chép mã đơn, các bước tiếp theo theo trạng thái đơn.
- Các màn chưa thiết kế lại hiển thị đúng với header và footer mới.
- `pnpm typecheck`, `pnpm lint`, `pnpm test` đạt. E2E của ba màn và header mobile đạt.

## Open Questions

1. Banner nổi bật chọn sản phẩm nào? Đã chốt ngày 2026-11-24: giữ các banner hiện có, chỉ chỉnh kích thước.
2. Cột "Mới lên kệ" lấy ba sản phẩm mới nhất toàn cửa hàng hay luân phiên theo danh mục?
3. Logo hãng ở "Mua theo thương hiệu" và "Thương hiệu nổi bật": API trả `logoUrl` null cho mọi hãng. Dùng tên hãng dạng chữ như thiết kế, hay tải logo lên qua trang quản trị trước?
4. Giảm giá và đánh giá: thêm vào backend rồi cập nhật thiết kế, hay giữ như hiện tại?
5. Thanh mua cố định ở chân trang sản phẩm: thêm vào bản này hay để sau? Thiết kế chưa có ảnh xuất.
6. Trang sản phẩm 768px có chia hai cột không? Hiện thiết kế là một cột.
7. Mức sai lệch chấp nhận khi so hình với ảnh thiết kế, theo pixel hay theo kiểm tra mắt?
