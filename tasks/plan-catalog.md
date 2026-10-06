# Implementation Plan: catalog

## Overview

Module `catalog` quản lý danh mục, thương hiệu, bộ thông số, sản phẩm, biến thể (SKU), ảnh và tồn kho. Đây là nguồn dữ liệu sản phẩm duy nhất của Pickory. Bảy module sau cần `catalog`, nên module này đi trước `commerce`.

`catalog` chỉ phụ thuộc `identity`. Module dùng `requirePermission`, `recordAudit` và event bus đã có. Plan chia công việc thành lát cắt dọc: mỗi task backend đi trọn schema, service, route và integration test. Task UI đi ngay sau task backend của nó.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-catalog.md`. Open Questions của spec đã chốt ngày 2026-10-03.
Danh sách task chi tiết: `tasks/todo-catalog.md`.
Plan cũ của `identity` và `ai-gateway` vẫn ở `tasks/plan.md`, vì còn live test, checkpoint và `SEC-01` chưa xong.

## Đồ thị phụ thuộc

```
CAT-01 bộ thuộc tính + validate thông số (packages/shared)
 ├── CAT-13 tài liệu mô tả: schema, trích text (packages/shared) ──┐
 └── CAT-02 slug, tổ hợp biến thể, giá hiển thị, chuyển trạng thái (logic thuần)
      └── CAT-03 schema Prisma + migration + khung module + sự kiện
           ├── CAT-04 danh mục + thương hiệu ──────────────── UI-C4
           │    └── CAT-05 tạo và sửa sản phẩm, biến thể (cần CAT-13) ─── UI-C5 (cần UI-C8)
           │         │                                              UI-C8 trình soạn thảo mô tả (cần CAT-13)
           │         ├── CAT-06 ảnh + Cloudinary ──────────── UI-C6
           │         │    └── CAT-07 đổi trạng thái, xóa cứng (cần ảnh để active)
           │         └── CAT-08 tồn kho + reserve/release ─── UI-C7
           │              └── CAT-09 GET /api/products: lọc, facets, sắp xếp ── UI-C1
           │                   └── CAT-10 chi tiết, slug 301, so sánh, catalogReader ── UI-C2, UI-C3
           │                        ├── CAT-11 thẻ meta SEO
           │                        └── CAT-12 dữ liệu seed 30 sản phẩm + đo p95
           └────────────────────────────────────────────────── E2E-C1
```

`CAT-13` chỉ cần `CAT-01` nên làm song song với `CAT-02` và `CAT-03`. `CAT-05` cần `CAT-13` vì API kiểm tra mô tả bằng schema này. `UI-C5` cần `UI-C8` vì form dùng trình soạn thảo.

`CAT-07` cần `CAT-06`, vì sản phẩm chỉ chuyển sang `active` khi có ít nhất 1 ảnh. `CAT-09` cần `CAT-08`, vì bộ lọc `inStock` đọc cột tính sẵn từ tồn kho.

## Architecture Decisions

1. **Cấu trúc thư mục theo mẫu `identity`.** Backend: `modules/catalog/{index.ts, domain/, data/, services/, routes/}`. `domain/` chứa logic thuần, không gọi DB. Frontend: `features/catalog/` cho khách và `features/catalog/admin/` cho back-office.
2. **Kiểu và hàm validate thông số nằm ở `packages/shared/src/catalog/`.** Backend dùng hàm này để chặn dữ liệu sai. Form admin dùng cùng hàm để báo lỗi trước khi gửi. Một nguồn quy tắc tránh lệch giữa hai phía.
3. **Quyền đã có trong danh mục của `identity`.** `products:read`, `products:write`, `inventory:write` có sẵn từ ID-01. `catalog/index.ts` re-export `catalogPermissions` từ danh mục này, theo Decision 1 của `tasks/plan.md`.
4. **Sự kiện phát sau khi transaction commit.** Thêm `ProductChangedEvent` và `StockChangedEvent` vào `DomainEvents` trong `shared/events.ts`. Có hai trường hợp:
   - **Catalog sở hữu transaction:** `emitAfterCommit(db, (tx, events) => ...)` tự gọi `db.$transaction`. Hàm chỉ phát sự kiện sau khi `$transaction` trả kết quả. Transaction lỗi thì bỏ toàn bộ sự kiện.
   - **Bên gọi truyền `tx` vào** (ví dụ `commerce` gọi `stock.reserve(orderId, items, tx)`): catalog không biết lúc nào transaction đó commit. Hàm nhận thêm một collector do bên gọi tạo bằng `createEventCollector()`. Bên gọi gọi `flush()` sau khi commit.
   - Sửa sau review Phase 1 ngày 2026-10-05: bản đầu của `emitAfterCommit` không gắn với transaction nào.
5. **Chỉ một hàm được đổi cột `stock`.** Hàm `applyStockMovement(tx, movement)` ghi movement và cập nhật `stock`, `minPrice`, `inStock` trong cùng transaction. Code khác không được ghi `stock` trực tiếp. Integration test kiểm tra bất biến `stock = tổng delta của movement` sau mỗi file test.
6. **`reserve` dùng UPDATE có điều kiện.** Mỗi SKU chạy `UPDATE skus SET stock = stock - q WHERE id = $1 AND stock >= q`. Số dòng bằng 0 nghĩa là thiếu hàng, transaction rollback toàn bộ. SKU được sắp theo `id` trước khi cập nhật để hai đơn không khóa chéo nhau. Ràng buộc `CHECK (stock >= 0)` là lớp chặn cuối ở DB.
7. **Idempotent theo `orderId`.** Ràng buộc `@@unique([orderId, skuId, type])` trên `inventory_movements` chặn ghi trùng. `reserve` gọi lần hai thấy đã có movement `order_reserve` thì trả `{ ok: true }` mà không trừ thêm. `release` chỉ trả hàng khi có `order_reserve` và chưa có `order_release`.
8. **"Sản phẩm đã từng được đặt" tạm xác định bằng movement.** `commerce` chưa có. Sản phẩm có SKU từng có movement `order_reserve` thì không xóa cứng được. Khi `commerce` xong, bổ sung kiểm tra bảng đơn hàng nếu cần.
9. **Cloudinary không dùng SDK.** Chữ ký upload là SHA-1 của tham số và secret, tính bằng `node:crypto`. Xóa ảnh gọi REST API bằng `fetch`. Cách này không thêm dependency. Khi thiếu `CLOUDINARY_*` ở dev và test, module dùng provider giả, giống fake provider của `ai-gateway`. Production thiếu khóa thì config báo lỗi khi khởi động.
10. **Xóa ảnh qua bảng job.** Bỏ ảnh khỏi sản phẩm thì ghi một dòng `image_deletion_jobs` trong cùng transaction. Worker trong process chạy theo chu kỳ, thử lại với backoff tăng dần, tối đa 8 lần. Test gọi trực tiếp `processImageDeletionJobs()`, không chờ timer.
11. **Quy ước tham số lọc thông số.** `enum` và `text` nhận danh sách cách nhau bằng dấu phẩy. `boolean` nhận `true` hoặc `false`. `number` nhận `attr.<key>_min` và `attr.<key>_max`. Khóa không có `filterable: true` trả 400.
12. **Facets theo kiểu loại trừ chính nó.** Số đếm của thuộc tính X áp mọi bộ lọc khác, trừ bộ lọc của X. Người dùng chọn `RAM 16GB` vẫn thấy số đếm của `RAM 32GB`. Facets chỉ đếm sản phẩm khách thấy được.
13. **Thẻ meta SEO theo hướng (b).** Backend có hàm `renderProductMeta` và route trả `index.html` đã chèn thẻ `title`, `description`, Open Graph cho `/products/:slug` và `/categories/:slug`. Hạ tầng deploy chưa chốt (Open Question 1 của `SPEC.md`). Task chỉ làm hàm, route và test. Bước gắn vào server production chờ quyết định deploy.
14. **Slug bỏ dấu không cần thư viện.** Dùng `normalize('NFD')`, xóa dấu kết hợp, đổi `đ` thành `d`. Trùng slug thì thêm hậu tố `-2`, `-3`.
15. **Mã lỗi nghiệp vụ.** Dùng `AppError` với mã cố định: `SPECS_INVALID`, `ATTRIBUTE_IN_USE`, `PRODUCT_NOT_PUBLISHABLE`, `PRODUCT_HAS_ORDERS`, `COMPARE_CATEGORY_MISMATCH`, `UNKNOWN_FILTER`. Message viết tiếng Việt.
16. **Audit log.** Ghi `recordAudit` trong cùng transaction cho đổi giá (`catalog.sku.price_change`), đổi trạng thái (`catalog.product.status_change`) và điều chỉnh tồn kho (`catalog.sku.stock_adjust`). Mỗi thao tác đúng 1 dòng.

17. **Mô tả chi tiết là tài liệu JSON, không phải Markdown hay HTML.** Ngày 2026-10-05 người dùng chọn trình soạn thảo trực quan. Trình soạn thảo dùng Tiptap, chỉ bật đoạn văn, tiêu đề 3 cấp, chữ đậm, chữ nghiêng. Schema Zod đặt ở `packages/shared/src/catalog/` làm whitelist. Backend và trình soạn thảo dùng chung schema này. Frontend đổi từng nút thành thẻ React, không dùng `dangerouslySetInnerHTML`. Tiêu đề cấp 1, 2, 3 hiện thành H2, H3, H4 vì trang đã có H1. Hàm `descriptionToPlainText` phục vụ `smart-search`. Ngày 2026-10-05 người dùng duyệt Tiptap cho Ask first. Builder chọn các extension tối thiểu và ghim phiên bản sau khi đối chiếu tài liệu chính thức.

18. **Xóa cứng sản phẩm xóa movement không thuộc đơn hàng.** FK từ `inventory_movements` sang `skus` giữ `onDelete: Restrict`. Khi xóa cứng, service kiểm tra không có movement `order_reserve`. Service xóa các movement `manual` và `return` của sản phẩm, rồi xóa sản phẩm, trong cùng transaction. Movement đơn hàng còn sót thì FK chặn lại, là lớp bảo vệ thứ hai. Khi admin bỏ một giá trị tùy chọn, CAT-05 tắt SKU (`enabled = false`), không xóa SKU, để giữ lịch sử tồn kho. Quyết định ngày 2026-10-05 sau review Phase 1.

## Task List

Chi tiết từng task ở `tasks/todo-catalog.md`.

### Phase 1: Nền tảng logic và dữ liệu
- [x] CAT-01 · [x] CAT-02 · [x] CAT-03

### Checkpoint C1
- [x] `pnpm lint && pnpm typecheck && pnpm test && pnpm build` sạch
- [x] Migration chạy trên DB sạch, `CHECK (stock >= 0)` chặn giá trị âm

### Phase 2: Back-office backend và UI
- [x] CAT-13 · [x] UI-C8 · [x] CAT-04 · [x] UI-C4 · [x] CAT-05 · [x] UI-C5 · [ ] CAT-06 · [ ] UI-C6 · [ ] CAT-07 · [ ] CAT-08 · [ ] UI-C7

### Checkpoint C2
- [ ] Admin tạo danh mục, sản phẩm 2 tùy chọn, tải ảnh, đăng bán trên `pnpm dev`
- [ ] 20 `reserve` đồng thời cho SKU còn 5 cái: đúng 5 thành công
- [ ] Ma trận quyền có mọi endpoint back-office của `catalog`

### Phase 3: Đọc công khai và giao diện khách
- [ ] CAT-09 · [ ] UI-C1 · [ ] CAT-10 · [ ] UI-C2 · [ ] UI-C3 · [ ] CAT-11 · [ ] CAT-12

### Checkpoint C3
- [ ] Khách lọc, xem chi tiết, so sánh trên dữ liệu seed
- [ ] `GET /api/products` p95 dưới 300 ms với 500 sản phẩm

### Phase 4: E2E và nghiệm thu
- [ ] E2E-C1

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage `services/*.ts` và `domain/*.ts` của `catalog` ít nhất 80% dòng
- [ ] `pnpm test:e2e` pass cả luồng identity cũ và luồng catalog mới
- [ ] Review với người dùng, rồi mới bắt đầu `shipping` hoặc `commerce`

## Traceability: Success Criteria → Task

| Success Criteria trong `SPEC-catalog.md` | Task |
|---|---|
| Lọc theo danh mục, thương hiệu, giá, còn hàng, ít nhất 3 thuộc tính; kết quả và `facets` đúng với seed | CAT-09, CAT-12, UI-C1 |
| Trang sản phẩm đổi giá, tồn kho, ảnh theo biến thể | CAT-10, UI-C2 |
| So sánh 4 sản phẩm cùng danh mục; khác danh mục trả 400 | CAT-10, UI-C3 |
| Thiếu thông số bắt buộc, sai kiểu, không có ảnh, SKU bật thiếu cân nặng và kích thước thì không `active` được | CAT-01, CAT-02, CAT-07 |
| 20 request giữ hàng đồng thời không bán vượt tồn kho | CAT-08 |
| `sales` xem được nháp nhưng sửa nhận 403 | CAT-05, ma trận quyền ở mọi task back-office |
| Mô tả có tiêu đề 1 đến 3, chữ đậm, chữ nghiêng: soạn, lưu, hiện đúng; nút lạ bị API từ chối | CAT-13, UI-C8, CAT-05, UI-C2 |
| Đổi slug thì link cũ trả 301 | CAT-05, CAT-10 |
| Đổi giá, đổi trạng thái, điều chỉnh tồn kho có đúng 1 dòng audit | CAT-05, CAT-07, CAT-08 |
| `onProductChanged` phát đúng 1 lần sau khi lưu thành công, không phát khi lỗi | CAT-03, CAT-05, CAT-07 |
| `GET /api/products` p95 dưới 300 ms với 500 sản phẩm | CAT-09, CAT-12 |
| Coverage `service.ts` ít nhất 80% | mọi task CAT-*, kiểm ở Checkpoint cuối |
| E2E: tạo danh mục, tạo sản phẩm 2 tùy chọn, đăng bán, khách lọc thấy, chọn biến thể thấy đúng giá | E2E-C1 |

## Ước lượng

| Phase | Task | Quy mô |
|---|---|---|
| 1 | CAT-01, CAT-02, CAT-03 | S, S, M |
| 2 | CAT-13, UI-C8, CAT-04 đến CAT-08, UI-C4 đến UI-C7 | 11 task, phần lớn M |
| 3 | CAT-09 đến CAT-12, UI-C1 đến UI-C3 | 7 task, phần lớn M |
| 4 | E2E-C1 | M |

Tổng 22 task. Với một người, ước lượng 3 sprint 1 tuần, có thể thêm vài ngày cho trình soạn thảo: Sprint 1 là Phase 1 và nửa đầu Phase 2. Sprint 2 là phần còn lại của Phase 2 và CAT-09, CAT-10. Sprint 3 là phần còn lại của Phase 3 và Phase 4.

Nếu có hai người, người thứ hai làm UI ngay sau khi task backend tương ứng xong. Người thứ hai cũng có thể bắt đầu `shipping` sau Checkpoint C1, vì `shipping` chỉ phụ thuộc `identity`.

## Risks and Mitigations

| Rủi ro | Mức | Cách giảm |
|---|---|---|
| Lọc JSON thông số chậm khi dữ liệu tăng | Med | Chỉ mục GIN trên `specs`; CAT-12 đo p95 với 500 sản phẩm trước nghiệm thu |
| Hai đơn giữ cùng SKU gây bán vượt | High | UPDATE có điều kiện, sắp SKU theo `id`, `CHECK (stock >= 0)`, test 20 request đồng thời |
| Code mới ghi thẳng cột `stock` | Med | Chỉ `applyStockMovement` được ghi; test bất biến tổng movement sau mỗi file test |
| Chưa có tài khoản Cloudinary | Med | Provider giả ở dev và test; test chữ ký bằng secret giả; tải ảnh thật kiểm thủ công khi có khóa |
| Mô tả chứa nội dung độc hại (XSS) | High | Lưu JSON theo schema whitelist, không lưu HTML; frontend đổi nút thành thẻ React, không dùng `dangerouslySetInnerHTML`; ESLint chặn thuộc tính này; test với payload `script` và `onerror` |
| Trình soạn thảo khó dùng với bàn phím và trình đọc màn hình | Med | Nút có nhãn và `aria-pressed`; test thao tác bằng bàn phím; kiểm thủ công bằng Playwright |
| Dán nội dung từ web làm sai định dạng | Med | Trình soạn thảo bỏ mọi nút và mark ngoài danh sách khi dán; test dán HTML có link, ảnh, script |
| Thẻ meta SEO chưa gắn được vì chưa chốt deploy | Low | Làm hàm và route có test; ghi bước gắn vào Open Question của `SPEC.md` |
| Đổi bộ thuộc tính làm hỏng sản phẩm cũ | Med | Khóa `key` cố định; chặn xóa hoặc đổi `type` khi có sản phẩm dùng |

## Open Questions

1. ~~Dependency cho mô tả~~ Đã chốt ngày 2026-10-05: Tiptap, lưu JSON (Decision 17). Không dùng `marked` hay `dompurify`.
2. **Tài khoản Cloudinary.** Cần `CLOUDINARY_CLOUD_NAME`, `CLOUDINARY_API_KEY`, `CLOUDINARY_API_SECRET` để kiểm thủ công tải ảnh thật. Không có khóa thì mọi test vẫn chạy bằng provider giả.
3. **Ảnh cho dữ liệu seed.** Seed cần `publicId` của ảnh. Đề xuất: có tài khoản Cloudinary thì tải bộ ảnh mẫu một lần, không có thì dùng ảnh giữ chỗ của provider giả.
4. **Nút thêm giỏ.** `commerce` chưa có. Đề xuất: UI-C2 hiện nút ở trạng thái tắt, kèm chú thích "Sắp có". `commerce` gắn chức năng sau.
