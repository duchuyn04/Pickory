# Implementation Plan: admin-search

## Overview

Plan này thêm tìm kiếm và lọc trạng thái cho các danh sách back-office có thao tác CRUD. Người dùng yêu cầu ngày 2026-10-13.

Hiện tại chỉ màn sản phẩm có cả ô tìm kiếm và bộ lọc trạng thái. Danh sách nhân viên trả toàn bộ, không phân trang. Danh sách đơn hàng không tìm được theo mã đơn hoặc số điện thoại. Không màn admin nào giữ bộ lọc trên URL.

Tìm kiếm ở backend hiện so sánh không phân biệt hoa thường, nhưng phân biệt dấu. Nhân viên gõ `nguyen` không tìm được khách `Nguyễn`. Plan này đổi sang tìm không dấu bằng extension `unaccent` của PostgreSQL.

Danh sách task chi tiết: `tasks/admin-search/todo.md`.

## Phạm vi

Người dùng chọn bốn nhóm màn:

1. Khách hàng và nhân viên (`identity`).
2. Đơn hàng (`commerce`), chỉ phần backend. Giao diện đơn hàng thuộc COM-UI5.
3. Sản phẩm và tồn kho (`catalog`).
4. Danh mục, thương hiệu và vai trò. Ba danh sách này nhỏ và không có trạng thái, nên chỉ thêm ô tìm kiếm ở trình duyệt.

Ngoài phạm vi: nhật ký audit, cấu hình AI, cấu hình commerce, cấu hình shipping. Nhật ký audit đã có bộ lọc riêng. Ba màn cấu hình không phải danh sách.

## Điều kiện trước

1. COM-16 đã commit trên nhánh `feat/commerce`.
2. Plan chạy trên nhánh `feat/commerce`, vì danh sách đơn hàng chỉ có trên nhánh này.

## Đồ thị phụ thuộc

```
FLT-01 `SearchTerm` dùng chung (logic thuần)
 ├── FLT-02 khách hàng: tìm không dấu, lọc trạng thái
 │    └── FLT-03 nhân viên: phân trang, tìm kiếm, lọc trạng thái và vai trò
 ├── FLT-04 sản phẩm, tồn kho, biến động kho
 └── FLT-05 đơn hàng: tìm theo mã, tên, số điện thoại, email

FLT-UI1 hook và component dùng chung cho bộ lọc
 ├── FLT-UI2 màn khách hàng và nhân viên (cần FLT-02, FLT-03)
 ├── FLT-UI3 màn sản phẩm, tồn kho, biến động kho (cần FLT-04)
 └── FLT-UI4 màn danh mục, thương hiệu, vai trò (chỉ frontend)
```

## Architecture Decisions

1. **Tìm không dấu bằng `unaccent`.** Điều kiện tìm là `unaccent(cột) ILIKE unaccent('%từ khóa%')`. Npgsql dịch `EF.Functions.Unaccent` và `EF.Functions.ILike` sang SQL. Dữ liệu và từ khóa cùng qua `unaccent`, nên `Nguyễn`, `nguyen` và `NGUYEN` khớp nhau.
2. **Mỗi module tự tạo extension.** Module `identity`, `catalog` và `commerce` có schema lịch sử riêng. Mỗi module thêm migration `CREATE EXTENSION IF NOT EXISTS unaccent`. Lệnh này không lỗi khi module khác đã tạo extension. Phần `Down` không xóa extension, vì module khác có thể còn dùng.
3. **Supabase cho tạo extension trong `public`.** Extension `vector` đã tạo được theo cách này (`docs/deployment-walkthrough.md`). Chạy `db:migrate` từ máy dev trước khi deploy code của plan này.
4. **Không thêm index cho tìm kiếm.** Hàm `unaccent` không có thuộc tính `IMMUTABLE`, nên không dùng trực tiếp trong index. Số dòng của giai đoạn demo nhỏ. Plan sẽ xét index `pg_trgm` khi danh sách vượt khoảng 50.000 dòng.
5. **`SearchTerm` trong `Pickory.Shared`.** Hàm thuần đọc tham số `search` và trả từ khóa đã chuẩn hóa:
   - Bỏ khoảng trắng ở hai đầu. Gộp nhiều khoảng trắng liền nhau thành một.
   - Chuỗi rỗng sau khi chuẩn hóa nghĩa là không lọc.
   - Từ khóa dài hơn 100 ký tự trả 400 `VALIDATION_ERROR` cho trường `search`.
   - Tham số `search` lặp lại trả 400 `VALIDATION_ERROR`.
   - Hàm escape `\`, `%` và `_`. Ký tự này trong từ khóa giữ nghĩa ký tự thường.
6. **Giá trị lọc trạng thái là chữ thường, nối bằng `_`.** Giá trị lạ trả 400 `VALIDATION_ERROR`. Quy ước này giống `OrderLabels.TryParse` của `commerce`.
7. **Trạng thái của tài khoản suy ra từ cột có sẵn.** Không thêm cột mới.
   - Khách hàng: `status=active|locked` đọc từ `LockedAt`. `emailVerified=true|false` đọc từ `EmailVerified`.
   - Nhân viên: `status=locked` khi có `LockedAt`. `status=invited` khi chưa khóa và chưa có `PasswordHash`. `status=active` cho trường hợp còn lại.
8. **Danh sách nhân viên đổi sang `PagedResponse`.** Response đổi từ mảng sang `{ items, total, page, limit }`. Màn nhân viên đổi trong FLT-UI2. Không có client nào khác đọc route này.
9. **Thứ tự ổn định.** Mọi danh sách có phân trang sắp theo cột chính, rồi theo `Id`. Hai dòng cùng `CreatedAt` không đổi chỗ giữa hai trang.
10. **Bộ lọc nằm trên URL.** Màn admin đọc và ghi bộ lọc bằng `useSearchParams`. Mở lại link cho cùng danh sách. Đổi bộ lọc thì về trang 1.
11. **Ô tìm kiếm chờ 300 ms.** Ô tìm kiếm gửi request sau 300 ms kể từ lần gõ cuối. Mỗi phím gõ không tạo một request.
12. **Tìm ở trình duyệt cho danh sách nhỏ.** Danh mục, thương hiệu và vai trò lọc ngay trên dữ liệu đã tải. Hàm `normalizeForSearch` chuyển từ `features/shipping/address-search.ts` sang `apps/frontend/src/shared/`.
13. **Route theo REST mức tài nguyên.** Plan chỉ thêm tham số query vào route `GET` có sẵn. Plan không thêm route mới.

## Task List

Chi tiết từng task ở `tasks/admin-search/todo.md`.

### Phase 1: Backend
- [x] FLT-01 · [x] FLT-02 · [x] FLT-03 · [x] FLT-04 · [x] FLT-05

### Checkpoint S1
- [x] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [x] Gõ `nguyen` tìm được khách `Nguyễn` qua `GET /api/admin/customers`
- [ ] Review với người dùng

### Phase 2: Frontend
- [ ] FLT-UI1 · [ ] FLT-UI2 · [ ] FLT-UI3 · [ ] FLT-UI4

### Checkpoint S2
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test`) sạch
- [ ] Mở lại link có bộ lọc cho cùng danh sách
- [ ] Review với người dùng
