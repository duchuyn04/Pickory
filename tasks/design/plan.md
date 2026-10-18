# Implementation Plan: đổi design system theo `shop-retail`

## Overview

Pickory đổi giao diện từ hướng cam và kem sang hệ thiết kế của `shop-retail/`. Người dùng chốt hướng này ngày 2026-10-06, trước khi làm CAT-06.

Phạm vi gồm mọi màn hiện có: khung admin, các màn admin và các màn của khách. Pickory giữ tên và logo riêng. Màu, bo góc, nút, bảng, badge và khung sidebar lấy theo `shop-retail`.

`shop-retail/` là bản mẫu HTML tĩnh. Bản mẫu này thay cho `prototype/`. Danh sách task chi tiết ở `tasks/design/todo.md`.

## Quyết định (2026-10-06)

- Màu thương hiệu là đỏ `#d70018`. Nền trang là xám `#f4f5f7`.
- Tên hiển thị vẫn là Pickory. Không dùng tên `Hải Đăng Digital` của bản mẫu.
- Khung admin chỉ hiện mục có route thật trong `apps/frontend/src/app.tsx`. Mục chưa có màn (Khuyến mãi, Báo cáo, Tồn kho) không hiện.
- Component dùng token trong `apps/frontend/src/shared/styles/tokens.css`. Component không viết mã màu hoặc cỡ chữ cứng.
- Font là Be Vietnam Pro, tải từ Google Fonts như bản mẫu.

## Đồ thị phụ thuộc

```
DS-01 token, CSS component chung, tài liệu design system
 ├── DS-02 khung admin (sidebar, thanh trên, menu di động)
 │    ├── DS-03 màn admin catalog (sản phẩm, form sản phẩm, danh mục, thương hiệu)
 │    └── DS-04 màn admin identity và AI (tổng quan, khách hàng, nhân viên, role, nhật ký, AI, đăng nhập)
 └── DS-05 màn khách (header, đăng nhập, đăng ký, tài khoản, mật khẩu, xác minh email)
      └── DS-07 header của khách trên mobile (nút menu, ngăn menu)
```

## Task List

- [x] DS-01 · [x] DS-02 · [x] DS-03 · [x] DS-04 · [x] DS-05 · [x] DS-06 · [ ] DS-07

### Checkpoint DS

- [x] Mọi màn hiện có dùng token mới. Ảnh 1280 px và 375 px gần với màn tương ứng của `shop-retail`.
- [x] Bộ E2E của repo đạt ở chế độ có giao diện và chế độ ẩn.
- [ ] Review với người dùng

## Risks

| Rủi ro | Cách giảm |
|---|---|
| Đổi tên hiển thị làm locator E2E hỏng | Giữ tên truy cập của nút, nhãn và tiêu đề. Ghi mọi locator phải đổi vào báo cáo. |
| CSS của bản mẫu viết mã màu cứng | Chuyển mỗi mã màu thành token trước khi dùng trong component. |
| Đỏ thương hiệu lẫn với màu lỗi | Lỗi luôn có chữ và icon, không chỉ dựa vào màu. |
