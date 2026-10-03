# Design system tham chiếu

Tài liệu này chốt hướng giao diện của Pickory trước khi làm UI (UI-05 trở đi). Token cài sẵn ở
`apps/web/src/shared/styles/tokens.css`. Mọi component dùng token, không viết màu hoặc cỡ chữ cứng.

## Cách lấy tham chiếu

Tìm trên Mobbin (web) theo 3 khu vực của Pickory. Giá trị trong bảng màu dưới đây được đo bằng cách
lấy mẫu điểm ảnh từ ảnh chụp màn hình (đánh dấu **đo**), hoặc ước lượng bằng mắt (đánh dấu **ước lượng**).
Đây không phải token gốc của các hãng, nên đừng coi là bản sao chính xác. Khi có thiết kế riêng thì
chỉ cần sửa `tokens.css`.

| Khu vực                           | Tham chiếu chính                                                                                                | Học được gì                                                                                                                                           |
| --------------------------------- | --------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| Cửa hàng (danh sách sản phẩm)     | [Klarna, lưới sản phẩm điện tử](https://mobbin.com/screens/ab04c0f2-28f4-4b54-9a72-76cdd148428a)                | Nền trắng, ảnh sản phẩm nằm trên ô xám rất nhạt, thanh lọc bên trái (giá, hãng) có số lượng, chữ giá đậm, huy hiệu dạng viên thuốc, nút đen bo tròn   |
| Cửa hàng (thẻ sản phẩm)          | [Apple, Shop Mac](https://mobbin.com/screens/f8972264-00a5-4254-b9ec-f630000a7176)                              | Thẻ trắng bo góc lớn, nhiều khoảng thở, giá ở chân thẻ, một nút hành động chính                                                                     |
| Chat trợ lý                       | [Shop, chat có thẻ sản phẩm](https://mobbin.com/screens/587a9d1f-4092-4167-9bea-7eb3ebad9df4)                   | Thẻ sản phẩm nằm trong dòng hội thoại, gạch đầu dòng lý do mua, chip gợi ý câu hỏi, ô nhập bo tròn nổi                                               |
| Chat trợ lý (dạng panel nổi)      | [Amazon, Rufus](https://mobbin.com/screens/ace833cd-b503-42e6-a317-16c4a2d3a126)                                | Panel ghim góc trái, bong bóng người dùng màu xám, thẻ sản phẩm nhỏ gọn có giá và nút xem chi tiết                                                    |
| Back-office (khung và bảng)       | [Square, Permissions](https://mobbin.com/screens/19aeddb8-59bd-4151-b07a-69823251d7e6)                          | Sidebar sáng, mục đang chọn tô xám, nút chính màu đen, bảng chỉ có đường kẻ ngang mảnh, ô lọc bo tròn                                                |
| Back-office (ma trận quyền)       | [Vanta, Roles](https://mobbin.com/screens/6dd83a18-ede6-44a5-adc5-2ebff239c7de)                                 | Bảng quyền theo nhóm, mỗi cột một role, biểu tượng thay cho chữ (sửa, chỉ xem, không có quyền). Dùng cho màn quản lý role (UI-06)                    |

## Nguyên tắc

- Trung tính và nhiều khoảng trắng: nền trắng, ảnh sản phẩm là điểm nhấn màu duy nhất.
- Một màu nhấn duy nhất là gần đen (nút chính, mục đang chọn). Màu khác chỉ dùng cho trạng thái.
- Viền mảnh 1px thay cho đổ bóng. Chỉ panel nổi (chat) và menu thả xuống mới có bóng nhẹ.
- Cửa hàng và back-office dùng chung token. Back-office dày đặc hơn (cỡ chữ nhỏ hơn, hàng bảng thấp hơn).
- Giao diện tiếng Việt: chừa chỗ cho chữ dài hơn, không đặt chiều rộng cố định cho nút và nhãn.

## Token

| Token                     | Giá trị                      | Nguồn                          | Dùng cho                                     |
| ------------------------- | ---------------------------- | ------------------------------ | -------------------------------------------- |
| `--color-ink`             | `#080518`                    | đo (nút Klarna)                | Chữ chính, nút chính                         |
| `--color-ink-muted`       | `#6b6b76`                    | ước lượng                      | Chữ phụ, mô tả                               |
| `--color-surface`         | `#ffffff`                    | đo                             | Nền trang, thẻ                               |
| `--color-surface-muted`   | `#f8f8f8`                    | đo (ô ảnh Klarna)              | Ô ảnh sản phẩm, bong bóng chat, nền ô nhập   |
| `--color-sidebar`         | `#f7f7f7`                    | đo (Square)                    | Nền sidebar back-office                      |
| `--color-nav-active`      | `#e9e9e9`                    | đo (Square)                    | Mục menu đang chọn                           |
| `--color-border`          | `#ececec`                    | ước lượng (quanh `#f2f2f2`)    | Viền thẻ, đường kẻ bảng                      |
| `--color-badge`           | `#e6e3f4`                    | ước lượng (huy hiệu Klarna)    | Huy hiệu "Phổ biến"                          |
| `--color-sale`            | `#eed5d4`                    | đo (huy hiệu giảm giá)         | Nền huy hiệu giảm giá                        |
| `--color-sale-ink`        | `#b3261e`                    | ước lượng                      | Chữ huy hiệu giảm giá, lỗi                   |
| `--color-success`         | `#1a7f4b`                    | ước lượng                      | Thành công, quyền được cấp                   |
| `--color-warning`         | `#a15c07`                    | ước lượng                      | Cảnh báo, ngân sách AI sắp hết               |
| `--radius-card`           | `12px`                       | ước lượng                      | Thẻ sản phẩm, ô ảnh, panel                   |
| `--radius-control`        | `8px`                        | ước lượng                      | Ô nhập, bảng                                 |
| `--radius-pill`           | `999px`                      | ước lượng                      | Nút, huy hiệu, chip, ô tìm kiếm              |
| `--space-1` đến `--space-8` | 4, 8, 12, 16, 24, 32, 48, 64 px | thang 4px                   | Khoảng cách                                  |
| `--font-sans`             | `system-ui` + dự phòng       | quyết định                     | Toàn bộ chữ                                  |
| `--text-xs` đến `--text-xl` | 12, 14, 16, 20, 28 px      | ước lượng                      | Cỡ chữ                                       |

Font dùng stack hệ thống để khỏi thêm dependency và hiển thị tiếng Việt có dấu đúng. Nếu sau này muốn
đổi font thương hiệu thì chỉ sửa `--font-sans`.

## Component chính (làm khi tới UI tương ứng)

- **Nút chính:** nền `--color-ink`, chữ trắng, bo `--radius-pill`, cao 40px. Nút phụ: viền `--color-border`, nền trắng.
- **Thẻ sản phẩm:** ô ảnh nền `--color-surface-muted` bo `--radius-card`, tên 2 dòng, giá đậm, huy hiệu ở góc trên trái.
- **Thanh lọc:** nhóm có tiêu đề, danh sách checkbox kèm số lượng, thanh trượt giá.
- **Chat:** bong bóng người dùng nền `--color-surface-muted`, trả lời của AI không nền, thẻ sản phẩm nằm trong luồng, chip gợi ý, ô nhập bo `--radius-pill`.
- **Bảng back-office:** không viền dọc, đường kẻ ngang `--color-border`, hàng cao 44px, tiêu đề cột chữ nhỏ viết hoa.
- **Ma trận quyền:** cột là role, hàng là quyền gom theo nhóm module, ô hiển thị dấu tick hoặc gạch, role hệ thống hiển thị khóa.
