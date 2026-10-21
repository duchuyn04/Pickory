# Task List: admin-search

Plan và quyết định thiết kế: `tasks/admin-search/plan.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Task UI theo `docs/design/design-system.md`. Task UI có ảnh chụp 1280 px và 375 px, không tràn ngang. Playwright kiểm console có 0 lỗi và 0 cảnh báo.
- Kích thước: XS 1 file, S 1-2, M 3-5. Số file không tính file test.

---

## Phase 1: Backend

### FLT-01: `SearchTerm` dùng chung
**Mô tả:** Hàm thuần trong `Pickory.Shared` đọc tham số `search`, chuẩn hóa từ khóa và dựng mẫu `ILIKE`, theo quyết định 5 của plan. Module gọi `EF.Functions.ILike` và `EF.Functions.Unaccent` với mẫu này ở FLT-02 đến FLT-05.
**Tiêu chí nghiệm thu:**
- [x] Bỏ khoảng trắng ở hai đầu và gộp khoảng trắng liền nhau. Chuỗi rỗng sau chuẩn hóa trả `null`
- [x] Từ khóa dài hơn 100 ký tự ghi lỗi `VALIDATION_ERROR` cho trường `search`
- [x] Tham số `search` lặp lại ghi lỗi `VALIDATION_ERROR`
- [x] Mẫu `ILIKE` escape `\`, `%` và `_`
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S

### FLT-02: Khách hàng: tìm không dấu và lọc trạng thái
**Mô tả:** Migration `CREATE EXTENSION IF NOT EXISTS unaccent` cho `identity`. `GET /api/admin/customers` dùng `SearchTerm` và nhận thêm `status=active|locked`, `emailVerified=true|false`.
**Tiêu chí nghiệm thu:**
- [x] Gõ `nguyen` tìm được khách tên `Nguyễn`. Gõ `duc` tìm được khách tên `Đức`
- [x] Tìm theo email, tên và số điện thoại. `%` và `_` trong từ khóa khớp đúng ký tự đó
- [x] `status=locked` chỉ trả khách có `LockedAt`. `status=active` chỉ trả khách chưa khóa
- [x] `emailVerified=false` chỉ trả khách chưa xác minh email
- [x] Giá trị `status` hoặc `emailVerified` lạ trả 400 `VALIDATION_ERROR`
- [x] Danh sách sắp theo `CreatedAt` giảm dần, rồi theo `Id`
- [x] Migration chạy được trên DB sạch và trên DB đã có extension `unaccent`
**Kiểm tra:** test tích hợp
**Phụ thuộc:** FLT-01 · **Quy mô:** S

### FLT-03: Nhân viên: phân trang, tìm kiếm, lọc trạng thái và vai trò
**Mô tả:** `GET /api/admin/staff` nhận `PageQuery`, `search`, `status=active|locked|invited` và `role=<mã vai trò>`. Response đổi sang `PagedResponse`, theo quyết định 7 và 8 của plan.
**Tiêu chí nghiệm thu:**
- [x] Response có `items`, `total`, `page`, `limit`. `total` đếm cùng điều kiện lọc
- [x] Tìm không dấu theo tên và email
- [x] `status=invited` chỉ trả nhân viên chưa khóa và chưa đặt mật khẩu. `status=locked` chỉ trả nhân viên đã khóa
- [x] `role=sales` chỉ trả nhân viên có vai trò `sales`. Mã vai trò không tồn tại trả danh sách rỗng
- [x] Giá trị `status` lạ trả 400 `VALIDATION_ERROR`
- [x] Response không có hash mật khẩu
- [x] Mỗi dòng có `status` là `active`, `invited` hoặc `locked`, theo cùng luật với bộ lọc `status`
**Kiểm tra:** test tích hợp
**Phụ thuộc:** FLT-02 · **Quy mô:** S

### FLT-04: Sản phẩm, tồn kho và biến động kho
**Mô tả:** Migration `unaccent` cho `catalog`. `GET /api/admin/products` đổi sang tìm không dấu. `GET /api/admin/inventory/low-stock` nhận `search`. `GET /api/admin/skus/{id}/movements` nhận `type`.
**Tiêu chí nghiệm thu:**
- [x] Gõ `ao thun` tìm được sản phẩm `Áo thun`. Tìm theo mã SKU vẫn đạt như trước
- [x] Test lọc sản phẩm hiện có vẫn đạt
- [x] Danh sách sắp hết hàng tìm được theo mã SKU và tên sản phẩm
- [x] Biến động kho lọc được theo `type=manual|order_reserve|order_release|return`. Giá trị lạ trả 400 `VALIDATION_ERROR`
- [x] Danh sách sắp hết hàng sắp theo `Stock`, rồi theo `Code`. Hai trang liền nhau không lặp dòng
**Kiểm tra:** test tích hợp
**Phụ thuộc:** FLT-01 · **Quy mô:** M

### FLT-05: Đơn hàng: tìm theo mã, tên, số điện thoại, email
**Mô tả:** Migration `unaccent` cho `commerce`. `GET /api/admin/orders` nhận `search`. Tên, số điện thoại và email đọc từ bản chụp `Contact` của đơn.
**Tiêu chí nghiệm thu:**
- [x] Tìm được đơn theo một phần mã đơn
- [x] Tìm không dấu theo tên người nhận. Tìm theo số điện thoại và email
- [x] `search` kết hợp được với `status`, `paymentStatus`, `flag` và khoảng ngày
- [x] `total` đếm cùng điều kiện lọc
**Kiểm tra:** test tích hợp
**Phụ thuộc:** FLT-01 · **Quy mô:** S

## Phase 2: Frontend

### FLT-UI1: Hook và component dùng chung cho bộ lọc
**Mô tả:** Hook đọc và ghi bộ lọc trên URL. Component ô tìm kiếm chờ 300 ms và thanh phân trang. Chuyển `normalizeForSearch` sang `apps/frontend/src/shared/`.
**Tiêu chí nghiệm thu:**
- [x] Hook đọc bộ lọc từ query string. Đổi bộ lọc thì ghi lại query string và về trang 1
- [x] Ô tìm kiếm gọi cập nhật một lần sau 300 ms kể từ lần gõ cuối
- [x] Thanh phân trang tắt nút trang trước ở trang 1 và nút trang sau ở trang cuối
- [x] Test hiện có của `address-search` vẫn đạt sau khi chuyển `normalizeForSearch`
- [x] Ô tìm kiếm và nút phân trang có nhãn cho trình đọc màn hình
**Kiểm tra:** Vitest
**Phụ thuộc:** không · **Quy mô:** M

### FLT-UI2: Màn khách hàng và nhân viên
**Mô tả:** Màn `/admin/customers` thêm lọc trạng thái, lọc xác minh email và phân trang. Màn `/admin/staff` thêm tìm kiếm, lọc trạng thái, lọc vai trò và phân trang.
**Tiêu chí nghiệm thu:**
- [x] Hai màn dùng hook và component của FLT-UI1
- [x] Mở lại link có bộ lọc cho cùng danh sách
- [x] Danh sách rỗng hiện thông báo không có kết quả phù hợp
- [x] Màn nhân viên đọc response `PagedResponse` mới
- [x] Cột trạng thái của màn nhân viên hiện nhãn riêng cho nhân viên `invited`, đọc từ trường `status`
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** FLT-UI1, FLT-02, FLT-03 · **Quy mô:** M

### FLT-UI3: Màn sản phẩm, tồn kho và biến động kho
**Mô tả:** Màn `/admin/products` giữ bộ lọc trên URL. Màn `/admin/inventory` thêm ô tìm kiếm. Màn biến động kho của SKU thêm lọc loại biến động.
**Tiêu chí nghiệm thu:**
- [x] Ba màn dùng hook và component của FLT-UI1
- [x] Mở lại link có bộ lọc cho cùng danh sách
- [x] Ô tìm kiếm sản phẩm không gửi request ở mỗi phím gõ
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** FLT-UI1, FLT-04 · **Quy mô:** M

### FLT-UI4: Màn danh mục, thương hiệu và vai trò
**Mô tả:** Ba màn thêm ô tìm kiếm. Màn lọc trên dữ liệu đã tải bằng `normalizeForSearch`, theo quyết định 12 của plan.
**Tiêu chí nghiệm thu:**
- [x] Gõ `dien thoai` tìm được danh mục `Điện thoại`
- [x] Màn danh mục giữ danh mục cha khi danh mục con khớp từ khóa
- [x] Danh sách rỗng hiện thông báo không có kết quả phù hợp
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** FLT-UI1 · **Quy mô:** M

---

## Phase 3: Trải nghiệm danh sách

Người dùng yêu cầu ngày 2026-10-20. Phase này chỉ sửa frontend. Quyết định thiết kế: mục 14 đến 19 của `tasks/admin-search/plan.md`.

### FLT-UI5: Ô tìm kiếm có nút xóa, phím Enter và phím Esc
**Mô tả:** `SearchInput` thêm nút xóa từ khóa. Phím Enter gửi từ khóa ngay. Phím Esc xóa ô. Cả 8 màn đang dùng `SearchInput` nhận thay đổi này.
**Tiêu chí nghiệm thu:**
- [x] Nút xóa chỉ hiện khi ô có chữ. Bấm nút thì ô rỗng, `onSearch('')` chạy ngay một lần, và con trỏ quay về ô
- [x] Phím Enter gọi `onSearch` ngay với từ khóa hiện tại. Lần gọi đang hẹn giờ bị hủy, nên API không nhận hai request
- [x] Phím Esc xóa ô có chữ và gọi `onSearch('')` ngay. Ô đã rỗng thì Esc không gọi `onSearch`
- [x] Nút xóa có tên truy cập `Xóa từ khóa` và dùng được bằng bàn phím
- [x] Trình duyệt không hiện thêm nút xóa riêng của ô `type="search"`
- [x] Test hiện có của `SearchInput` và 8 màn dùng nó vẫn đạt
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** FLT-UI1 · **Quy mô:** S

### FLT-UI6: Màn sản phẩm giữ bảng cũ, tô đậm chữ khớp, hiện bộ lọc đang áp dụng
**Mô tả:** Màn `/admin/products` giữ bảng cũ trong lúc tải kết quả mới. Màn tô đậm phần tên và mã SKU khớp từ khóa. Màn có thanh bộ lọc đang áp dụng.
**Tiêu chí nghiệm thu:**
- [x] Đổi từ khóa hoặc bộ lọc thì bảng cũ vẫn hiện, mờ đi, và có `aria-busy="true"` cho tới khi có kết quả mới
- [x] Ô tìm hiện dấu hiệu đang tải trong lúc chờ kết quả. Lần tải đầu tiên vẫn hiện `Đang tải sản phẩm...`
- [x] Gõ `dien thoai` thì chữ `Điện thoại` trong tên được bọc trong `<mark>`. Phép so khớp không phân biệt dấu và hoa thường
- [x] Mã SKU khớp từ khóa cũng được tô đậm. Từ khóa rỗng thì bảng không có `<mark>`
- [x] Thanh bộ lọc hiện một nhãn cho mỗi bộ lọc đang bật: từ khóa, trạng thái, danh mục, sắp hết hàng
- [x] Bấm nút X trên một nhãn thì chỉ bộ lọc đó bị xóa khỏi URL. Danh sách quay về trang 1
- [x] Nút `Xóa bộ lọc` xóa mọi bộ lọc. Thanh bộ lọc ẩn khi không có bộ lọc nào
- [x] Thanh bộ lọc hiện số kết quả, ví dụ `12 sản phẩm khớp "iphone"`
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** FLT-UI5 · **Quy mô:** M

### FLT-UI7: Màn sản phẩm hiện ảnh đầu tiên
**Mô tả:** Bảng ở `/admin/products` thêm ảnh nhỏ của từng sản phẩm, đặt trước tên. Ảnh lấy từ trường `images` mà `GET /api/admin/products` đã trả. Backend không đổi.
**Tiêu chí nghiệm thu:**
- [x] Mỗi dòng hiện ảnh có `position` nhỏ nhất trong `images` của sản phẩm đó
- [x] Ảnh Cloudinary đi qua `withCloudinaryTransform` với chiều rộng 200. Trình duyệt không tải ảnh gốc
- [x] Ảnh có `alt` lấy từ `alt` của ảnh. `alt` rỗng thì dùng tên sản phẩm
- [x] Ảnh có kích thước cố định và `loading="lazy"`. Bảng không bị xô dòng khi ảnh tải xong
- [x] Sản phẩm không có ảnh hiện khung giữ chỗ cùng kích thước, có biểu tượng ảnh và tên truy cập `Chưa có ảnh`
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** không · **Quy mô:** S

### FLT-UI8: Hàm STT dùng chung và màn sản phẩm, tồn kho, biến động kho
**Mô tả:** Thêm hàm thuần tính số thứ tự (STT) của một dòng. Màn `/admin/products`, `/admin/inventory` và màn biến động kho của SKU thêm cột `STT` ở đầu bảng.
**Tiêu chí nghiệm thu:**
- [x] Hàm trả `(page - 1) * limit + index + 1`. Trang 1 bắt đầu từ 1. Trang 2 với `limit` 20 bắt đầu từ 21
- [x] Ba màn đọc `page` và `limit` từ response, không từ hằng số trong màn
- [x] Cột `STT` là cột đầu tiên, có tiêu đề `STT`, căn phải
- [x] Test E2E hiện có của ba màn vẫn đạt sau khi bảng thêm cột
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** FLT-UI7 · **Quy mô:** M

### FLT-UI9: STT cho màn danh mục, thương hiệu và vai trò
**Mô tả:** Ba màn lọc trên dữ liệu đã tải, nên STT đánh theo danh sách đang hiện. Màn danh mục đánh STT theo cây.
**Tiêu chí nghiệm thu:**
- [ ] Màn thương hiệu và vai trò đánh STT 1, 2, 3 theo thứ tự đang hiện. Lọc xong thì STT đánh lại từ 1
- [ ] Màn danh mục đánh danh mục gốc 1, 2, 3. Danh mục con đánh theo cha: 1.1, 1.2
- [ ] Lọc danh mục xong thì STT đánh lại theo cây đang hiện
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** FLT-UI8 · **Quy mô:** M

### FLT-UI10: STT cho màn khách hàng, nhân viên và nhật ký thao tác
**Mô tả:** Màn `/admin/customers`, `/admin/staff` và `/admin/audit-logs` thêm cột `STT` bằng hàm của FLT-UI8.
**Tiêu chí nghiệm thu:**
- [ ] Ba màn dùng hàm STT của FLT-UI8 với `page` và `limit` từ response
- [ ] Cột `STT` là cột đầu tiên, có tiêu đề `STT`, căn phải
- [ ] Test E2E hiện có của ba màn vẫn đạt sau khi bảng thêm cột
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** FLT-UI8 · **Quy mô:** M

### FLT-UI11: STT cho màn đơn hàng, đối soát và đánh giá
**Mô tả:** Màn `/admin/orders`, `/admin/payments` và `/admin/reviews` thêm cột `STT` bằng hàm của FLT-UI8.
**Tiêu chí nghiệm thu:**
- [ ] Ba màn dùng hàm STT của FLT-UI8 với `page` và `limit` từ response
- [ ] Cột `STT` là cột đầu tiên, có tiêu đề `STT`, căn phải
- [ ] Test E2E hiện có của ba màn vẫn đạt sau khi bảng thêm cột
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** FLT-UI8 · **Quy mô:** M
