# Design system tham chiếu

Tài liệu này chốt hướng giao diện của Pickory trước khi làm UI (UI-05 trở đi). Token cài sẵn ở
`apps/web/src/shared/styles/tokens.css`. Mọi component dùng token, không viết màu hoặc cỡ chữ cứng.
Prototype bấm được dùng đúng hệ thống này: `prototype/index.html`.

## Hướng đã chốt: cam ấm, nền kem và cát

Phiên bản đầu dùng trắng đen (tham chiếu Klarna, Apple) bị đánh giá là quá đơn sắc. Hướng mới lấy
màu thương hiệu cam ấm và các thẻ ưu đãi pastel của Klook, gần với thói quen mua sắm của khách Việt
(Shopee, FPT Shop) mà vẫn giữ bố cục sạch.

| Khu vực                     | Tham chiếu                                                                                  | Học được gì                                                                                         |
| --------------------------- | ------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| Màu và thẻ ưu đãi           | [Klook, trang chủ](https://mobbin.com/screens/077fd163-99db-436d-8141-62218721a3a1)         | Một màu cam cho nút và điểm nhấn, thẻ ưu đãi nền cát và kem, minh họa ấm                            |
| Lưới sản phẩm, bộ lọc       | [Klarna, lưới sản phẩm](https://mobbin.com/screens/ab04c0f2-28f4-4b54-9a72-76cdd148428a)    | Ô ảnh nền nhạt, bộ lọc trái có số đếm, giá đậm                                                      |
| Chat trợ lý                 | [Shop, chat có thẻ sản phẩm](https://mobbin.com/screens/587a9d1f-4092-4167-9bea-7eb3ebad9df4) | Thẻ sản phẩm trong hội thoại, chip gợi ý, ô nhập bo tròn                                            |
| Đăng ký, đăng nhập          | [AirOps](https://mobbin.com/screens/3120091e-a27f-4333-a4ed-4a7577ca018d), [Clerk](https://mobbin.com/screens/bc7e98b3-dc24-4229-a284-58f96737937a) | Bố cục chia đôi (form và panel thương hiệu), Google trước, lỗi hiện ngay dưới ô nhập               |
| Back-office                 | [Square, Permissions](https://mobbin.com/screens/19aeddb8-59bd-4151-b07a-69823251d7e6), [Vanta, Roles](https://mobbin.com/screens/6dd83a18-ede6-44a5-adc5-2ebff239c7de) | Sidebar sáng có icon, bảng đường kẻ mảnh, ma trận quyền theo role                                   |

## Nguyên tắc

- Cam chỉ dùng cho hành động và điểm nhấn: nút chính, mục đang chọn, nhãn giảm giá, icon nổi bật. Chữ vẫn là màu nâu đen.
- Nền kem cho ô ảnh sản phẩm, nền cát cho hero và panel thương hiệu, pastel xoay vòng cho ô danh mục.
- Viền mảnh thay cho đổ bóng. Chỉ panel nổi (chat, modal, menu thả) có bóng nhẹ.
- Không gradient trang trí, không emoji làm icon, dùng bộ icon nét mảnh thống nhất.
- Màu đỏ dành cho lỗi và trạng thái nguy hiểm, không dùng cho khuyến mãi để khỏi lẫn với cam.
- Back-office dùng chung token, dày đặc hơn (nút 36px, bảng gọn).

## Token màu

Giá trị đánh dấu **đo** được lấy mẫu điểm ảnh từ ảnh chụp Mobbin; còn lại là suy ra để hài hòa.

| Token                   | Giá trị   | Nguồn             | Dùng cho                                     |
| ----------------------- | --------- | ----------------- | -------------------------------------------- |
| `--color-brand`         | `#ec6436` | đo (nút Klook)    | Nút chính, mục đang chọn, nhãn giảm giá      |
| `--color-brand-hover`   | `#d4521f` | suy ra            | Trạng thái hover của nút chính               |
| `--color-brand-soft`    | `#fdece4` | suy ra            | Nền icon, ô danh mục, badge thương hiệu      |
| `--color-sand`          | `#f6dda8` | đo (thẻ ưu đãi)   | Khối khuyến mãi đậm                          |
| `--color-sand-soft`     | `#fbeed3` | suy ra            | Hero, panel đăng nhập                        |
| `--color-surface-muted` | `#fcf8e7` | đo (nền kem)      | Ô ảnh sản phẩm, sidebar back-office          |
| `--color-ink`           | `#1f1a17` | suy ra            | Chữ chính                                    |
| `--color-ink-muted`     | `#7a6e65` | suy ra            | Chữ phụ                                      |
| `--color-border`        | `#ece4d8` | suy ra            | Viền thẻ, đường kẻ bảng                      |
| `--color-star`          | `#f5a524` | suy ra            | Sao đánh giá                                 |
| `--color-info`          | `#1d6fe0` | suy ra            | Trạng thái thông tin (đang giao, đã xác nhận) |
| `--color-success`       | `#127a43` | suy ra            | Thành công, đã thanh toán                    |
| `--color-danger`        | `#b42318` | suy ra            | Lỗi, khóa, quá hạn                           |

## Chữ, bo góc, khoảng cách

- Font: **Be Vietnam Pro** (Google Fonts), dự phòng `system-ui`. Hiển thị tiếng Việt có dấu tốt.
- Cỡ chữ 12, 13, 14, 16, 20, 28, 40 px. Tiêu đề dùng `text-wrap: balance` để không mồ côi chữ.
- Bo góc: 6px (badge), 10px (ô nhập), 14px (thẻ), tròn hẳn cho nút và chip.
- Khoảng cách theo thang 4px: 4, 8, 12, 16, 24, 32, 48, 64.

## Component chính

- **Nút chính:** nền cam, chữ trắng, bo tròn, cao 44px (cửa hàng) hoặc 36px (back-office).
- **Thẻ sản phẩm:** ô ảnh nền kem, nhãn giảm giá nền cam góc trên trái, thương hiệu, tên 2 dòng, dòng thông số ngắn, sao màu hổ phách, giá đậm.
- **Ô danh mục:** ảnh trên nền pastel xoay vòng (kem, cát, cam nhạt, xanh nhạt, xanh lá nhạt).
- **Đăng ký, đăng nhập:** chia đôi, công tắc "Đăng nhập / Tạo tài khoản", Google trước, ô mật khẩu có nút hiện/ẩn và thước độ mạnh, lỗi hiện ngay dưới ô nhập.
- **Chat:** tin của khách nền cam, tin của trợ lý không nền, thẻ sản phẩm trong luồng, trích dẫn số nhỏ dẫn tới đúng mục tài liệu.
- **Bảng back-office:** không viền dọc, mục menu đang chọn có vạch cam bên trái.
