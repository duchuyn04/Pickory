# Task List: commerce

Plan và quyết định thiết kế: `tasks/commerce/plan.md`. Spec: `spec/SPEC-commerce.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Test tích hợp dùng `shipping` và `catalog` thật. GHN dùng `FakeGhnClient`. SePay dùng webhook giả lập.
- Task UI theo `docs/design/design-system.md` và `tasks/design/plan.md`. Task UI có ảnh chụp 1280 px và 375 px, không tràn ngang. Playwright kiểm console có 0 lỗi và 0 cảnh báo.
- Route trong task dùng bảng đề xuất ở quyết định 17 của plan. Người dùng đổi bảng thì cập nhật task trước khi làm.
- Kích thước: XS 1 file, S 1-2, M 3-5. Số file không tính file test.

---

## Phase 0: Xác minh SePay

### COM-00: Xác minh SePay
**Mô tả:** Chốt các điểm Open Question 1 của spec trước COM-11. Ghi kết quả vào `docs/adr/0004-sepay-payment-integration.md`. Phần tài liệu làm ngay. Phần webhook thật cần khóa SePay do người dùng cung cấp.
**Trạng thái:** phần cần khóa chờ người dùng. Các task khác không chờ task này, trừ COM-11 chờ phần tài liệu.
**Tiêu chí nghiệm thu:**
- [x] Theo tài liệu: tên trường và kiểu dữ liệu của payload webhook, kèm ví dụ
- [x] Theo tài liệu: cách SePay gửi API key (tên header, định dạng giá trị)
- [x] Theo tài liệu: response SePay mong đợi, mã HTTP thành công, chính sách thử lại
- [x] Theo tài liệu: trường phân biệt tiền vào và tiền ra, trường mã giao dịch dùng để chống trùng
- [x] Theo tài liệu: định dạng URL ảnh VietQR theo số tài khoản, mã ngân hàng, số tiền, nội dung
- [ ] Khi có khóa: một webhook thật đã che khóa, đối chiếu với giả định ở trên
  - Chờ người dùng cấp khóa SePay để đối chiếu webhook thật trong COM-00.
- [x] Không có khóa: ADR ghi rõ phần chưa đối chiếu và lý do
**Kiểm tra:** đọc tài liệu SePay; webhook thật khi có khóa
**Phụ thuộc:** không · **Quy mô:** S
**File:** `docs/adr/0004-sepay-payment-integration.md`

---

## Phase 1: Nền tảng

### COM-01: Logic thuần về tiền và mã
**Mô tả:** Bốn nhóm hàm thuần, không gọi DB và không gọi HTTP.
- `OrderCodeGenerator`: mã `PK` cộng 8 ký tự theo quyết định 7 của plan.
- `OrderPricing`: tạm tính, phí ship khách trả, phần shop chịu, tổng tiền, kiểm hạn mức COD.
- `TaxCodeRule`: mã số thuế 10 hoặc 13 chữ số, cho phép dấu gạch.
- `TransferContentParser`: tìm mã đơn trong nội dung chuyển khoản bằng `PK[A-Z2-9]{8}`.
**Tiêu chí nghiệm thu:**
- [x] Mã đơn không chứa `0`, `O`, `1`, `I` và luôn dài 10 ký tự
- [x] Tạm tính bằng tổng giá nhân số lượng. Tạm tính bằng hoặc trên ngưỡng thì phí khách trả bằng 0 và phần shop chịu bằng phí GHN
- [x] Tổng tiền trên hạn mức COD thì COD bị từ chối, chuyển khoản vẫn hợp lệ
- [x] Mã số thuế đúng 10 hoặc 13 chữ số sau khi bỏ dấu gạch thì hợp lệ. Chuỗi khác bị từ chối
- [x] Parser tìm được mã có khoảng trắng xung quanh, chữ thường, hoặc chữ ngân hàng chèn trước và sau
- [x] Nội dung không có mã trả "không thấy", không ném lỗi
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.Commerce/{Orders/OrderCodeGenerator.cs, Pricing/OrderPricing.cs, Invoices/TaxCodeRule.cs, Payments/TransferContentParser.cs}`, test trong `apps/backend/tests/Pickory.UnitTests/Modules/Commerce/`

### COM-02: Máy trạng thái đơn hàng
**Mô tả:** `OrderTransitions` kiểm cặp trạng thái đơn và người chuyển theo bảng của spec. `PaymentTransitions` kiểm trạng thái thanh toán. Hai lớp là hàm thuần.
**Tiêu chí nghiệm thu:**
- [x] Mọi chuyển đổi hợp lệ trong bảng của spec trả "được phép"
- [x] Mọi cặp còn lại trả "không hợp lệ" (test sinh đủ mọi cặp)
- [x] Khách chỉ hủy được ở `pending_payment` hoặc `pending_confirmation`
- [x] Nhân viên hủy được tới trước `delivered`. Đơn `shipping` cần vận đơn đã hủy
- [x] Hủy đơn đã `paid` thì thanh toán chuyển `refund_pending`
- [x] `refunded` chỉ đi từ `refund_pending`
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.Commerce/Orders/{OrderTransitions.cs, PaymentTransitions.cs}`

### COM-03: Khung module, Contracts, schema và cấu hình
**Mô tả:** Tạo `Pickory.Modules.Commerce` và `Pickory.Modules.Commerce.Contracts`. Thêm `CommerceDbContext`, migration đầu tiên, `CommerceModule.cs`. Contracts có năm sự kiện của quyết định 2 và khung `ICommerceReader`, `ICommerceReports`. Thêm `SEPAY_WEBHOOK_API_KEY` vào `AppConfig` và `.env.example`.
**Tiêu chí nghiệm thu:**
- [x] Bảng `carts`, `cart_items`, `orders`, `order_items`, `order_status_history`, `payment_transactions`, `saved_addresses`, `reviews`, `commerce_settings` theo Data Model của spec
- [x] `orders.code`, `orders.idempotency_key`, `payment_transactions.provider_tx_id` có ràng buộc unique. `reviews` unique theo `(product_id, user_id)`
- [x] Tiền là cột `integer`. Thời gian lưu UTC theo quy ước của `tasks/dotnet-migration/plan.md`
- [x] Migration chạy trên DB sạch. Lệnh `e2e:start` và fixture test tích hợp dựng được schema mới
- [x] `Pickory.ArchitectureTests` chặn `Commerce` tham chiếu module khác ngoài project Contracts
- [x] Production thiếu `SEPAY_WEBHOOK_API_KEY` thì dừng khi khởi động. Dev và test thiếu khóa vẫn chạy
- [x] `Program.cs` chỉ gọi `AddCommerceModule` và `MapCommerceEndpoints`
**Kiểm tra:** build, test kiến trúc, test tích hợp schema
**Phụ thuộc:** COM-01, COM-02 · **Quy mô:** M

### Checkpoint M1
- [x] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [x] Test kiến trúc chặn `Commerce` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

---

## Phase 2: Giỏ hàng và đặt đơn

### COM-04: Giỏ hàng
**Mô tả:** `GET /api/cart`, `POST /api/cart/items`, `PATCH /api/cart/items/{skuId}`, `DELETE /api/cart/items/{skuId}`. Giỏ gắn với `AnonymousId` hoặc `UserId` của `Actor`. Giỏ không lưu giá. Thêm và xóa phát `CartItemAddedEvent`, `CartItemRemovedEvent`.
**Tiêu chí nghiệm thu:**
- [x] Mỗi lần xem giỏ, giá lấy mới từ `ICatalogReader.GetSkusAsync`
- [x] SKU tắt, hết hàng hoặc sản phẩm ngừng bán vẫn hiện trong giỏ, kèm nhãn lý do
- [x] Vượt số lượng mỗi SKU hoặc số dòng mỗi giỏ trả 422 `CART_LIMIT_EXCEEDED`
- [x] SKU không tồn tại trả 404. Số lượng nhỏ hơn 1 trả 400 `VALIDATION_ERROR`
- [x] `source` tùy chọn đi vào `CartItemAddedEvent`. Chuỗi `source` dài hơn 64 ký tự trả 400
- [x] Mỗi lần thêm hoặc xóa thành công phát đúng 1 sự kiện sau commit
- [x] Thêm vào giỏ không giữ hàng: tồn kho không đổi
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-03 · **Quy mô:** M

### COM-05: Gộp giỏ và dọn dữ liệu khi xóa tài khoản
**Mô tả:** Handler cho `UserLinkedEvent` gộp giỏ vãng lai vào giỏ tài khoản. Handler cho `UserDeletedEvent` xóa giỏ và sổ địa chỉ. Phần đánh giá và đơn hàng của `UserDeletedEvent` thuộc COM-21.
**Tiêu chí nghiệm thu:**
- [x] Cùng SKU thì cộng số lượng, nhưng không vượt giới hạn mỗi SKU
- [x] Gộp xong thì giỏ vãng lai bị xóa
- [x] Nhận cùng `UserLinkedEvent` 2 lần không cộng số lượng 2 lần
- [x] `UserDeletedEvent` xóa giỏ và mọi dòng `saved_addresses` của người dùng
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-04 · **Quy mô:** S

### COM-UI1: Giỏ hàng và nút thêm giỏ
**Mô tả:** Mini cart trên header và trang `/cart` trong `apps/frontend/src/features/commerce/customer/`. Bật nút "Thêm vào giỏ" ở trang sản phẩm, thay cho nút tắt "Sắp có" (Open Question 4 của `tasks/catalog/plan.md`).
**Tiêu chí nghiệm thu:**
- [x] Thêm, đổi số lượng, xóa cập nhật mini cart và trang giỏ không cần tải lại trang
- [x] Dòng không mua được hiện nhãn lý do. Nút sang checkout tắt khi giỏ có dòng như vậy
- [x] Lỗi `CART_LIMIT_EXCEEDED` hiện thông báo tiếng Việt cạnh dòng hàng
- [x] Giá hiện theo định dạng VND
- [x] Dùng được bằng bàn phím. Ô số lượng có `label`
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** COM-04 · **Quy mô:** M

### COM-06: Cấu hình commerce
**Mô tả:** `GET` và `PUT /api/admin/commerce/settings` với quyền `commerce-settings:manage`. Cấu hình gồm ngưỡng miễn phí ship, hạn mức COD, giới hạn giỏ, số tài khoản nhận và mã ngân hàng.
**Tiêu chí nghiệm thu:**
- [x] `sales` và `support` nhận 403. Endpoint mới có trong ma trận quyền
- [x] Giá trị mặc định: ngưỡng 500.000 đ, hạn mức COD 20.000.000 đ, 5 sản phẩm mỗi SKU, 20 dòng mỗi giỏ
- [x] Số âm hoặc số tài khoản sai định dạng trả 400 `VALIDATION_ERROR`
- [x] Mỗi lần sửa ghi đúng 1 dòng audit `commerce.settings.update` trong cùng transaction
- [x] Response không chứa `SEPAY_WEBHOOK_API_KEY`
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-03 · **Quy mô:** S

### COM-UI7: Màn cấu hình commerce
**Mô tả:** Màn `/admin/commerce/settings` trong back-office. Mục menu chỉ hiện với người có `commerce-settings:manage`.
**Tiêu chí nghiệm thu:**
- [x] Lưu thành công hiện thông báo. Lỗi hiện dưới đúng ô bằng `FieldError`
- [x] Người thiếu quyền không thấy menu. Mở thẳng URL thấy trang không đủ quyền
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** COM-06 · **Quy mô:** S

### COM-07: Checkout preview
**Mô tả:** `POST /api/checkout/previews` nhận địa chỉ, phương thức thanh toán và các dòng của giỏ hiện tại. Route trả tạm tính, phí ship, phần shop chịu, tổng tiền. Route này là route báo phí cho giỏ theo quyết định 19 của `tasks/shipping/plan.md`.
**Tiêu chí nghiệm thu:**
- [x] Phí lấy từ `IShippingService.QuoteAsync`. Kiện hàng dựng bằng `BuildParcel` từ cân nặng và kích thước của SKU
- [x] Tạm tính bằng hoặc trên ngưỡng thì phí khách trả bằng 0
- [x] Chọn COD cho tổng trên hạn mức trả 422 `COD_NOT_ALLOWED`
- [x] `shipping` lỗi trả 503 `SHIPPING_UNAVAILABLE`, không trả phí ước đoán
- [x] Mã địa chỉ không có trong danh mục trả 400 (sau khi Câu hỏi mở 4 được chốt)
- [x] Quá 20 lần mỗi phút cho mỗi khách trả 429
- [x] Giỏ có dòng không mua được trả 422 `SKU_UNAVAILABLE`
**Kiểm tra:** test tích hợp với `FakeGhnClient`
**Phụ thuộc:** COM-06, SHIP-08 (`RateLimiter` trong `Pickory.Shared`), Câu hỏi mở 4 · **Quy mô:** M

### COM-08: Đặt đơn
**Mô tả:** `POST /api/orders` với header `Idempotency-Key`. Server tính lại tiền, giữ hàng, tạo đơn có snapshot, xóa dòng đã đặt khỏi giỏ, trong một transaction. Đơn COD vào `pending_confirmation`. Đơn SePay vào `pending_payment` với hạn 30 phút. Phát `OrderPlacedEvent` sau commit.
**Tiêu chí nghiệm thu:**
- [x] Thiếu `Idempotency-Key` trả 400 `IDEMPOTENCY_KEY_REQUIRED`
- [x] Gửi cùng khóa 2 lần trả cùng mã đơn. Lần đầu 201, lần sau 200. DB có đúng 1 đơn
- [x] Số tiền do frontend gửi bị bỏ qua. Tổng khác preview trả 409 `ORDER_TOTAL_CHANGED`
- [x] Thiếu hàng trả 409 `INSUFFICIENT_STOCK` kèm danh sách SKU thiếu. Không có đơn và tồn kho không đổi
- [x] Lỗi sau bước giữ hàng làm rollback cả đơn lẫn tồn kho (test ép lỗi)
- [x] Snapshot lưu tên sản phẩm, mã SKU, tùy chọn, giá, ảnh, địa chỉ gồm mã và tên, phí ship
- [x] Liên hệ thiếu tên, SĐT hoặc email trả 400. SĐT kiểm bằng `ContactFieldRules`. Mã số thuế kiểm bằng `TaxCodeRule`
- [x] Response trả token xem đơn một lần. DB chỉ lưu hash của token
- [x] Phát đúng 1 `OrderPlacedEvent` sau commit, không phát khi lỗi
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-07 · **Quy mô:** M

### COM-UI2: Trang checkout
**Mô tả:** Checkout một trang: liên hệ, `AddressPicker` của `shipping`, ghi chú, phương thức thanh toán, thông tin hóa đơn VAT tùy chọn. Trang gọi preview khi địa chỉ hoặc phương thức đổi.
**Tiêu chí nghiệm thu:**
- [x] Hiện tạm tính, phí ship, phần miễn phí và tổng tiền từ preview
- [x] Đơn trên hạn mức thì lựa chọn COD tắt, kèm lý do
- [x] `SHIPPING_UNAVAILABLE` hiện "Chưa tính được phí vận chuyển, thử lại" và tắt nút đặt hàng
- [x] Mỗi lần mở trang sinh một `Idempotency-Key`. Bấm đặt 2 lần nhanh chỉ tạo 1 đơn
- [x] `ORDER_TOTAL_CHANGED` hiện tổng mới và yêu cầu khách xác nhận lại
- [x] Form kiểm bằng Zod. Lỗi hiện dưới đúng ô bằng `FieldError`
- [x] Dùng được bằng bàn phím. Mọi ô có `label`
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** COM-08, UI-S1 · **Quy mô:** M

### COM-09: Email xác nhận đơn
**Mô tả:** Gửi email xác nhận sau khi đặt đơn. Email có mã đơn, danh sách sản phẩm, tổng tiền, link `/orders/{code}?t={token}`.
**Trạng thái:** Câu hỏi mở 2 của plan đã chốt ngày 2026-10-11. `IEmailSender` chuyển sang `Pickory.Shared/Email/`.
**Tiêu chí nghiệm thu:**
- [x] Email gửi sau commit. Email lỗi chỉ ghi log, đơn vẫn tạo thành công
- [x] Môi trường dev ghi email vào outbox dev, để Playwright đọc được link
- [x] Log không chứa token xem đơn
**Kiểm tra:** test tích hợp với cổng email giả có chế độ lỗi
**Phụ thuộc:** COM-08, Câu hỏi mở 2 · **Quy mô:** S

### COM-10: Theo dõi đơn và khách tự hủy
**Mô tả:** `GET /api/orders` cho khách đã đăng nhập. `GET /api/orders/{code}`, `GET /api/orders/{code}/payment`, `POST /api/orders/{code}/cancellation` cho chủ đơn, hoặc người có token.
**Tiêu chí nghiệm thu:**
- [x] Chủ đơn là người có `UserId` của đơn. Đơn vãng lai cần token đúng trong query `t`
- [x] Không phải chủ đơn và không có token đúng trả 404 `ORDER_NOT_FOUND`, không tiết lộ đơn có tồn tại
- [x] Token sai và token của đơn khác đều trả 404
- [x] Tự hủy ở `pending_payment` hoặc `pending_confirmation` thành công, gọi `ReleaseAsync`, phát `OrderStatusChangedEvent`
- [x] Tự hủy ở trạng thái khác trả 409 `INVALID_ORDER_TRANSITION`
- [x] Route `payment` trả trạng thái thanh toán và hạn thanh toán, không trả dữ liệu cá nhân
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-08 · **Quy mô:** M

### COM-UI3: Đặt hàng thành công và đơn hàng của tôi
**Mô tả:** Trang đặt hàng thành công, trang "Đơn hàng của tôi", trang chi tiết đơn có tiến trình giao hàng và nút tự hủy.
**Tiêu chí nghiệm thu:**
- [x] Trang thành công hiện mã đơn và link xem đơn, kể cả khi email lỗi
- [x] Chi tiết đơn hiện trạng thái đơn, trạng thái thanh toán, mã vận đơn khi có
- [x] Nút hủy chỉ hiện ở trạng thái khách được hủy. Bấm hủy cần xác nhận
- [x] Khách vãng lai mở link có token xem được đơn mà không đăng nhập
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** COM-10 · **Quy mô:** M

### Checkpoint M2
- [ ] Khách vãng lai thêm giỏ, chọn địa chỉ, thấy phí ship và đặt được đơn COD trên `pnpm dev`
  - Chưa đạt (2026-10-12): GHN sandbox trả 404 cho mọi đường dẫn, nên preview trả `503 SHIPPING_UNAVAILABLE`. Chạy lại M2 khi sandbox hoạt động.
- [ ] Gửi cùng `Idempotency-Key` 2 lần chỉ tạo 1 đơn
  - Chưa kiểm: luồng COD dừng ở bước tính phí ship. Kiểm sau khi GHN sandbox hoạt động.
- [ ] Review với người dùng

---

## Phase 3: Thanh toán SePay

### COM-11: Webhook SePay
**Mô tả:** `POST /api/webhooks/sepay` nhận giao dịch. Module xác thực API key, kiểm payload, lưu giao dịch thô, rồi khớp đơn theo bảng 5 trường hợp của spec. Định dạng payload lấy từ COM-00.
**Tiêu chí nghiệm thu:**
- [x] Thiếu hoặc sai API key trả 401, không ghi DB
- [x] Body trên 16 KB trả 413. Body không phải JSON trả 415
- [x] Route không cần `Origin` của web. Các route khác vẫn giữ luật kiểm `Origin`
- [x] Giao dịch tiền ra hoặc sai số tài khoản được lưu và bỏ qua, không khớp đơn
- [x] Giao dịch được lưu vào `payment_transactions` trước bước khớp đơn
- [x] Đúng mã, đơn `pending_payment`, đủ tiền: thanh toán `paid`, đơn `pending_confirmation`, phát `OrderPaidEvent` 1 lần
- [x] Đúng mã, thiếu tiền: đơn giữ `pending_payment`, gắn cờ `underpaid`
- [x] Đúng mã, thừa tiền: `paid` như trường hợp đủ tiền, gắn cờ `overpaid`
- [x] Không thấy mã đơn: giao dịch có `match_status = unmatched`
- [x] Gửi cùng giao dịch 3 lần: DB có 1 dòng `payment_transactions` và đơn ghi nhận thanh toán 1 lần
- [x] Cả 3 lần gửi đều nhận response thành công
- [x] Response đúng định dạng SePay yêu cầu. Thời gian phản hồi dưới 2 giây trong test
- [x] Log và response không chứa API key
- [x] Quá giới hạn tần suất theo IP trả 429
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-08, COM-00 (phần tài liệu), SHIP-08 · **Quy mô:** M

### COM-12: Job hết hạn thanh toán và tiền về muộn
**Mô tả:** Job chạy mỗi phút hủy đơn `pending_payment` quá 30 phút và trả hàng. Bổ sung trường hợp thứ năm của webhook: tiền về cho đơn đã hủy vì quá hạn.
**Tiêu chí nghiệm thu:**
- [x] Đơn quá 30 phút chuyển `cancelled` với lý do quá hạn, gọi `ReleaseAsync`, phát `OrderStatusChangedEvent`
- [x] Đơn chưa tới hạn không đổi. Đơn COD không bị job chạm tới
- [x] Tiền về sau khi hủy, còn đủ hàng: giữ hàng lại, đơn về `pending_confirmation`, thanh toán `paid`
- [x] Tiền về sau khi hủy, thiếu hàng: đơn giữ `cancelled`, thanh toán `refund_pending`
- [x] Chu kỳ job là 1 phút
**Kiểm tra:** test tích hợp gọi processor với `TimeProvider` giả
**Phụ thuộc:** COM-11 · **Quy mô:** M

### COM-UI4: Trang chờ thanh toán VietQR
**Mô tả:** Trang hiện ảnh VietQR, số tài khoản, ngân hàng, số tiền và nội dung chuyển khoản. Nội dung chuyển khoản là mã đơn. Trang có đồng hồ đếm ngược 30 phút. Trang hỏi `GET /api/orders/{code}/payment` mỗi 3 giây.
**Tiêu chí nghiệm thu:**
- [x] URL ảnh QR theo định dạng chốt ở COM-00
- [x] Đã thanh toán thì trang chuyển sang trang thành công trong 5 giây
- [x] Hết giờ thì hiện thông báo đơn đã hủy và nút đặt lại
- [x] Nút sao chép số tài khoản và nội dung chuyển khoản. Có thông báo cho trình đọc màn hình
- [x] Rời trang thì dừng hỏi định kỳ
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest với đồng hồ giả; Playwright `--headed`
**Phụ thuộc:** COM-11 · **Quy mô:** M

### COM-13: Đối soát SePay tay
**Mô tả:** `GET /api/admin/payments/transactions` (`payments:read`) lọc theo trạng thái khớp. `PATCH /api/admin/payments/transactions/{id}` (`payments:reconcile`) gắn giao dịch vào một đơn, hoặc đánh dấu không thuộc đơn nào.
**Tiêu chí nghiệm thu:**
- [ ] Danh sách lọc được giao dịch chưa khớp, đơn có cờ `underpaid` và `overpaid`
- [ ] Gắn tay vào đơn `pending_payment` đủ tiền chuyển đơn như webhook khớp tự động, phát `OrderPaidEvent` 1 lần
- [ ] Gắn vào đơn không tồn tại trả 404. Gắn giao dịch đã khớp trả 409
- [ ] Đánh dấu không thuộc đơn nào đặt `match_status = ignored`
- [ ] Mỗi thao tác ghi đúng 1 dòng audit
- [ ] `support` nhận 403. Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-11 · **Quy mô:** M

### COM-UI6: Màn đối soát SePay và COD
**Mô tả:** Màn `/admin/payments` có hai tab. Tab SePay liệt kê giao dịch chưa khớp và đơn gắn cờ. Tab COD liệt kê đơn COD đã giao chưa nhận tiền, dùng COM-17.
**Tiêu chí nghiệm thu:**
- [ ] Gắn tay một giao dịch vào đơn bằng ô tìm mã đơn
- [ ] Nút thao tác chỉ hiện khi người dùng có `payments:reconcile`
- [ ] Mọi thao tác cần xác nhận và hiện kết quả
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** COM-13, COM-17 · **Quy mô:** M

### Checkpoint M3
- [ ] Webhook giả lập đủ 5 trường hợp của spec. Gửi cùng giao dịch 3 lần chỉ ghi nhận 1 lần
- [ ] Đơn SePay quá 30 phút bị hủy và trả hàng
- [ ] Review với người dùng

---

## Phase 4: Back-office đơn hàng và vận chuyển

### COM-14: Danh sách và chi tiết đơn cho nhân viên
**Mô tả:** `GET /api/admin/orders` và `GET /api/admin/orders/{code}` với quyền `orders:read`. Danh sách lọc theo trạng thái đơn, trạng thái thanh toán, ngày tạo, cờ.
**Tiêu chí nghiệm thu:**
- [x] Danh sách phân trang theo `PageQuery` của `Pickory.Shared`
- [x] Chi tiết có liên hệ, địa chỉ, sản phẩm, thông tin hóa đơn VAT, lịch sử trạng thái, vận đơn
- [x] Ngày lọc tính theo giờ `Asia/Ho_Chi_Minh`
- [x] `sales` và `support` đọc được. Người không có `orders:read` nhận 403. Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-08 · **Quy mô:** M

### COM-15: Xác nhận, hủy và đánh dấu đã xuất hóa đơn
**Mô tả:** `POST /api/admin/orders/{code}/confirmation` (`orders:update-status`). `POST /api/admin/orders/{code}/cancellation` (`orders:cancel`) bắt buộc lý do. `PUT /api/admin/orders/{code}/invoice-issuance` (`orders:update-status`).
**Tiêu chí nghiệm thu:**
- [x] Xác nhận chỉ được từ `pending_confirmation`, nếu không trả 409 `INVALID_ORDER_TRANSITION`
- [x] Hủy thiếu lý do trả 400. Hủy đơn đang `shipping` khi vận đơn còn hiệu lực trả 409
- [x] Hủy gọi `ReleaseAsync`. Đơn đã `paid` chuyển thanh toán sang `refund_pending`
- [x] Đánh dấu hóa đơn chỉ được khi đơn có thông tin hóa đơn, nếu không trả 409
- [x] Mỗi thao tác ghi 1 dòng `order_status_history` và 1 dòng audit, phát `OrderStatusChangedEvent` khi trạng thái đổi
- [x] `support` nhận 403. Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-14 · **Quy mô:** M

### COM-16: Vận đơn GHN và trạng thái giao hàng
**Mô tả:** `POST /api/admin/orders/{code}/shipment` tạo vận đơn, cho sửa kích thước kiện. `DELETE /api/admin/orders/{code}/shipment` hủy vận đơn. Cả hai cần `orders:fulfill`. Handler `ShipmentStatusChangedEvent` cập nhật đơn theo quyết định 9 của plan.
**Tiêu chí nghiệm thu:**
- [x] Tạo vận đơn chỉ được khi đơn `confirmed`. Thành công thì đơn sang `shipping` và lưu `ShipmentId`, mã vận đơn
- [x] Đơn COD gửi `CodAmount` bằng tổng tiền. Đơn SePay gửi `CodAmount = 0`
- [x] `Ref` là mã đơn. Bấm lại sau `SHIPPING_UNAVAILABLE` không tạo vận đơn thứ hai
- [x] `SHIPPING_REJECTED` trả 422 với thông báo của `shipping`. Đơn giữ `confirmed`
- [x] Hủy vận đơn thành công thì đơn về `confirmed`. Sau đó nhân viên hủy đơn được
- [x] Sự kiện `Delivered` chuyển đơn sang `delivered`. Sự kiện `Returned` chuyển đơn sang `returned`. Hàng không tự nhập lại kho
- [x] Sự kiện có `ShipmentId` khác vận đơn hiện tại bị bỏ qua
- [x] Nhận cùng sự kiện 2 lần không ghi lịch sử 2 lần
- [x] `support` nhận 403. Endpoint mới có trong ma trận quyền. Mỗi thao tác ghi 1 dòng audit
- [x] GHN tự hủy vận đơn khi đơn `shipping` thì đơn về `confirmed` và có cờ `shipment_cancelled`. Nhân viên hủy vận đơn không gắn cờ này (Câu hỏi mở 8 của plan, đã chốt 2026-10-11)
**Kiểm tra:** test tích hợp với `FakeGhnClient` và sự kiện phát trực tiếp
**Phụ thuộc:** COM-15, SHIP-08 · **Quy mô:** M

### COM-17: Nhận tiền COD và đã hoàn tiền
**Mô tả:** `PUT /api/admin/orders/{code}/cod-receipt` (`payments:reconcile`). `PUT /api/admin/orders/{code}/refund` (`refunds:manage`) kèm mã giao dịch hoàn tiền.
**Tiêu chí nghiệm thu:**
- [x] Nhận COD chỉ được với đơn COD đã `delivered` và `unpaid`, nếu không trả 409. Thành công thì thanh toán `paid`
- [x] Danh sách đơn của COM-14 lọc được đơn COD đã giao chưa nhận tiền
- [x] Hoàn tiền chỉ được từ `refund_pending`, nếu không trả 409. Thiếu mã giao dịch trả 400
- [x] Không có đường nào tự chuyển sang `refunded` mà không qua route này
- [x] Mỗi thao tác ghi 1 dòng audit. `support` nhận 403. `sales` gọi route hoàn tiền nhận 403
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-14 · **Quy mô:** S

### COM-UI5: Màn đơn hàng back-office
**Mô tả:** Thay trang giữ chỗ `/admin/orders` trong `apps/frontend/src/app.tsx`. Danh sách có tab theo trạng thái và bộ lọc cờ. Chi tiết đơn có nút thao tác theo quyền và lịch sử trạng thái.
**Tiêu chí nghiệm thu:**
- [x] Nút xác nhận, tạo vận đơn, hủy vận đơn, hủy đơn, hóa đơn, nhận COD, hoàn tiền chỉ hiện khi người dùng có quyền
- [x] Form tạo vận đơn điền sẵn kích thước kiện từ `BuildParcel` và cho sửa
- [x] Hủy đơn bắt buộc nhập lý do
- [x] Danh sách có ô tìm kiếm theo mã đơn, tên, số điện thoại, email (FLT-05 của `tasks/admin-search/todo.md`). Bộ lọc nằm trên URL
- [x] Lỗi 409 và 422 hiện thông báo tiếng Việt của API
- [x] Bảng dùng được bằng bàn phím. Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** COM-15, COM-16, COM-17 · **Quy mô:** M

### Checkpoint M4
- [ ] `sales` xác nhận đơn và tạo vận đơn bằng `FakeGhnClient`. Trạng thái giao hàng tự cập nhật vào đơn
- [ ] `support` xem được đơn, gọi API hủy hoặc tạo vận đơn nhận 403
- [ ] Review với người dùng

---

## Phase 5: Sổ địa chỉ, đánh giá, liên kết tài khoản

### COM-18: Sổ địa chỉ
**Mô tả:** `GET`, `POST`, `PATCH`, `DELETE /api/me/addresses[/{id}]` cho khách đã đăng nhập. Địa chỉ dùng mã của danh mục `shipping`.
**Tiêu chí nghiệm thu:**
- [ ] Khách chưa đăng nhập nhận 401
- [ ] Địa chỉ thứ 11 trả 422
- [ ] Chọn địa chỉ mặc định mới thì địa chỉ mặc định cũ bỏ cờ. Luôn có tối đa 1 địa chỉ mặc định
- [ ] Mã địa chỉ không có trong danh mục trả 400 (sau khi Câu hỏi mở 4 được chốt)
- [ ] Sửa hoặc xóa địa chỉ của người khác trả 404
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-03, Câu hỏi mở 4 · **Quy mô:** S

### COM-UI8: Sổ địa chỉ
**Mô tả:** Mục sổ địa chỉ trong trang tài khoản dùng `AddressPicker`. Checkout cho khách đã đăng nhập chọn địa chỉ đã lưu.
**Tiêu chí nghiệm thu:**
- [ ] Thêm, sửa, xóa, đặt mặc định trên giao diện
- [ ] Checkout điền sẵn địa chỉ mặc định
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** COM-18, COM-UI2 · **Quy mô:** M

### COM-19: Đánh giá sản phẩm
**Mô tả:** `GET /api/products/{id}/reviews` công khai, trả điểm trung bình, phân bố số sao, trang đánh giá đang hiện. `POST /api/products/{id}/reviews` và `PATCH /api/products/{id}/reviews/mine` cho người đã nhận hàng. `POST /api/reviews/{id}/reports` cho khách đã đăng nhập. Migration của task này tạo bảng `review_reports` theo Câu hỏi mở 9.
**Tiêu chí nghiệm thu:**
- [ ] Người chưa có đơn `delivered` chứa sản phẩm nhận 403 `REVIEW_NOT_ALLOWED`
- [ ] Đánh giá thứ hai của cùng người cho cùng sản phẩm trả 409
- [ ] Số sao ngoài 1 đến 5, hoặc nhận xét trên 2.000 ký tự trả 400
- [ ] Sửa sau 30 ngày trả 409 `REVIEW_EDIT_EXPIRED`
- [ ] Đánh giá mới hiện ngay. Điểm trung bình và phân bố số sao tính lại
- [ ] Danh sách phân trang theo cursor
- [ ] Một khách báo cáo một đánh giá tối đa 1 lần (sau khi Câu hỏi mở 9 được chốt)
- [ ] Phát `ReviewChangedEvent` sau commit, nếu Câu hỏi mở 7 được duyệt
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-16 (cần đơn `delivered`) · **Quy mô:** M

### COM-UI9: Khối đánh giá trên trang sản phẩm
**Mô tả:** Khối đánh giá trên trang sản phẩm: điểm trung bình, phân bố số sao, danh sách, form viết và sửa, nút báo cáo.
**Tiêu chí nghiệm thu:**
- [ ] Form chỉ hiện với người đủ điều kiện. Người khác thấy lý do
- [ ] Chọn số sao dùng được bằng bàn phím và có tên truy cập
- [ ] Nội dung đánh giá hiện dạng văn bản thuần, không render HTML
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** COM-19 · **Quy mô:** M

### COM-20: Kiểm duyệt đánh giá
**Mô tả:** `GET /api/admin/reviews` và `PATCH /api/admin/reviews/{id}` với quyền `reviews:moderate`. Ẩn hoặc hiện lại bắt buộc chọn lý do.
**Tiêu chí nghiệm thu:**
- [ ] Đánh giá có từ 3 báo cáo đứng đầu hàng chờ
- [ ] Ẩn hoặc hiện thiếu lý do trả 400
- [ ] Đánh giá bị ẩn biến mất khỏi `GET /api/products/{id}/reviews`. Điểm trung bình tính lại
- [ ] Mỗi lần ẩn hoặc hiện ghi đúng 1 dòng audit
- [ ] `sales` nhận 403. Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-19 · **Quy mô:** S

### COM-UI10: Hàng chờ kiểm duyệt đánh giá
**Mô tả:** Màn `/admin/reviews` cho người có `reviews:moderate`.
**Tiêu chí nghiệm thu:**
- [ ] Danh sách hiện số báo cáo và trạng thái ẩn hoặc hiện
- [ ] Ẩn hoặc hiện mở hộp thoại chọn lý do
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang. Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** COM-20 · **Quy mô:** S

### COM-21: Gắn đơn vãng lai và ẩn danh khi xóa tài khoản
**Mô tả:** Handler `EmailVerifiedEvent` gắn đơn vãng lai cùng email vào tài khoản. Handler `UserDeletedEvent` ẩn danh đánh giá và bỏ liên kết đơn với tài khoản.
**Trạng thái:** Câu hỏi mở 3 của plan đã chốt ngày 2026-10-11. Identity phát `EmailVerifiedEvent(UserId, Email)`.
**Tiêu chí nghiệm thu:**
- [ ] Xác thực email gắn mọi đơn có `UserId` rỗng và cùng email liên hệ, so không phân biệt hoa thường
- [ ] Đơn đã gắn tài khoản khác không đổi
- [ ] `UserDeletedEvent` giữ nội dung đánh giá, bỏ liên kết tới người viết
- [ ] `UserDeletedEvent` giữ đơn hàng, đặt `UserId` của đơn thành rỗng
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-08, COM-19, Câu hỏi mở 3 · **Quy mô:** S

### Checkpoint M5
- [ ] Chỉ người đã nhận hàng đánh giá được. Đánh giá bị ẩn biến mất và điểm trung bình đổi theo
- [ ] Review với người dùng

---

## Phase 6: Interface cho module sau

### COM-22: `ICommerceReader`
**Mô tả:** Cài `ICommerceReader` với năm hàm của spec: `GetRatingSummaryAsync`, `GetVisibleReviewsAsync`, `GetPurchasedProductIdsAsync`, `GetOrdersForCustomerAsync`, `GetOrderByCodeAsync`. `support-desk` cần thêm tra đơn theo `AnonymousId`, nên thêm `GetOrdersForAnonymousAsync`.
**Tiêu chí nghiệm thu:**
- [ ] `GetRatingSummaryAsync` bỏ đánh giá đã ẩn
- [ ] `GetPurchasedProductIdsAsync` chỉ tính đơn đã thanh toán hoặc đã giao, không tính đơn hủy
- [ ] `GetOrdersForCustomerAsync` trả tối đa `limit` đơn mới nhất
- [ ] `GetOrderByCodeAsync` trả `null` khi không có đơn
- [ ] `GetOrdersForAnonymousAsync` chỉ trả đơn đặt từ đúng `AnonymousId`
- [ ] `OrderDetail` có `UserId` và `AnonymousId`. `shopping-assistant` dùng hai trường này để kiểm chủ đơn
- [ ] Kiểu trả về nằm trong `Pickory.Modules.Commerce.Contracts`, không lộ entity EF Core
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-19 · **Quy mô:** M

### COM-23: `ICommerceReports`
**Mô tả:** Cài `GetDailySalesAsync`, `GetProductSalesAsync`, `GetPendingCountsAsync` theo định nghĩa chỉ số trong `spec/SPEC-admin-console.md`.
**Tiêu chí nghiệm thu:**
- [ ] Doanh thu ghi nhận là tổng `total` của đơn `paid` theo ngày thanh toán, trừ đơn `refunded`
- [ ] Giá trị đơn đặt bỏ đơn hủy vì quá hạn thanh toán
- [ ] Tỉ lệ hủy tách theo lý do: khách hủy, shop hủy, quá hạn
- [ ] Ngày tính theo giờ `Asia/Ho_Chi_Minh`, kiểm biên nửa đêm
- [ ] `GetPendingCountsAsync` trả số đơn chờ xác nhận, giao dịch chưa khớp, đơn COD chưa nhận tiền
- [ ] Bộ dữ liệu dựng sẵn cho ra đúng số đã tính tay
**Kiểm tra:** test tích hợp
**Phụ thuộc:** COM-17 · **Quy mô:** M

### Checkpoint M6
- [ ] `ICommerceReader` và `ICommerceReports` chỉ trả kiểu trong `Pickory.Modules.Commerce.Contracts`
- [ ] Review với người dùng

---

## Phase 7: E2E và nghiệm thu

### COM-E2E0: Sửa 6 spec E2E đỏ có sẵn
**Mô tả:** Bộ `pnpm test:e2e` có 6 spec đỏ từ trước COM-UI4. Ba spec nhận lỗi console 404 vì trang chủ tải sản phẩm theo slug ghi cứng. Hai spec nhận dữ liệu cấu hình mà spec khác để lại. Một spec không thấy liên kết `Tài khoản` sau đăng ký. Task sửa nguyên nhân, không làm yếu test.
**Tiêu chí nghiệm thu:**
- [x] Trang chủ không gây lỗi console khi sản phẩm ghi cứng không tồn tại. Ô dùng sản phẩm dự phòng
- [x] Spec đổi cấu hình commerce hoặc shipping khôi phục giá trị cũ. Spec khác không phụ thuộc thứ tự chạy
- [x] `profile-validation` kiểm đúng liên kết tài khoản theo header hiện tại
- [x] `pnpm test:e2e` xanh toàn bộ ở chế độ ẩn hai lần liên tiếp và ở chế độ `--headed`
- [x] Không thêm bộ lọc console, `skip`, `retries` hoặc sửa `playwright.config.ts`
  - Ngoại lệ đã duyệt: thêm biến `E2E_ACCOUNT_PASSWORD` vào khối `env` của API để seed khách. Cách chạy test không đổi.
**Kiểm tra:** `pnpm test:e2e`, chạy ở chế độ ẩn và `--headed`
**Phụ thuộc:** COM-UI4 · **Quy mô:** M

### COM-E2E1: Khách vãng lai mua COD
**Mô tả:** `apps/frontend/e2e/commerce-cod.spec.ts`. Khách thêm giỏ, chọn tỉnh/thành và phường/xã bằng `AddressPicker`, đặt đơn COD, đọc email trong outbox, mở link xem đơn.
**Tiêu chí nghiệm thu:**
- [ ] Đơn hiện ở trang thành công và trang xem đơn qua link có token
- [ ] Tạm tính trên ngưỡng thì phí ship khách trả bằng 0
- [ ] Admin xóa cứng sản phẩm vừa đặt nhận lỗi `PRODUCT_HAS_ORDERS`
- [ ] Console 0 lỗi và 0 cảnh báo
**Kiểm tra:** `pnpm test:e2e`, chạy ở chế độ ẩn và `--headed`
**Phụ thuộc:** COM-UI2, COM-UI3, COM-09 · **Quy mô:** M

### COM-E2E2: Khách mua SePay
**Mô tả:** `apps/frontend/e2e/commerce-sepay.spec.ts`. Khách đặt đơn SePay. Test bắn webhook giả lập với API key của môi trường E2E.
**Tiêu chí nghiệm thu:**
- [ ] Trang chờ thanh toán chuyển sang trang thành công trong 5 giây sau webhook
- [ ] Đơn đặt trên hạn mức COD không chọn được COD
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** COM-UI4 · **Quy mô:** S

### COM-E2E3: Sales xác nhận và tạo vận đơn
**Mô tả:** `apps/frontend/e2e/commerce-fulfillment.spec.ts`. `sales` đăng nhập, mở trang đơn hàng, xác nhận, tạo vận đơn bằng `FakeGhnClient`. Test phát trạng thái `Delivered` qua đường của `FakeGhnClient`.
**Tiêu chí nghiệm thu:**
- [ ] `sales` thấy mục "Đơn hàng" và mở được trang đơn thật
- [ ] Đơn chuyển `shipping`, rồi `delivered`, và trang chi tiết hiện mã vận đơn
- [ ] `support` mở cùng đơn không thấy nút hủy và nút tạo vận đơn
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** COM-UI5 · **Quy mô:** M

### COM-E2E4: Đánh giá và kiểm duyệt
**Mô tả:** `apps/frontend/e2e/commerce-reviews.spec.ts`. Khách có đơn đã giao viết đánh giá. `support` ẩn đánh giá đó.
**Tiêu chí nghiệm thu:**
- [ ] Khách chưa nhận hàng không thấy form đánh giá
- [ ] Sau khi ẩn, đánh giá biến mất khỏi trang sản phẩm và điểm trung bình đổi
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** COM-UI9, COM-UI10 · **Quy mô:** S

### COM-E2E5: Mua hàng khi API AI lỗi
**Mô tả:** Chạy COM-E2E1 đến COM-E2E4 với khóa AI sai.
**Tiêu chí nghiệm thu:**
- [ ] Mọi test E2E của `commerce` pass khi khóa AI sai
**Kiểm tra:** `pnpm test:e2e` với biến môi trường khóa AI sai
**Phụ thuộc:** COM-E2E1, COM-E2E2, COM-E2E3, COM-E2E4 · **Quy mô:** XS

### COM-24: Nghiệm thu commerce
**Mô tả:** Kiểm toàn module trước khi sang `activity-tracking`.
**Tiêu chí nghiệm thu:**
- [ ] Ma trận quyền có mọi endpoint back-office của `commerce`
- [ ] Coverage dòng của `Pickory.Modules.Commerce` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Khi có khóa SePay: một giao dịch thật chuyển đơn sang `paid`, trang chờ chuyển trang trong 5 giây
- [ ] Khi có token GHN: tạo rồi hủy 1 vận đơn sandbox từ trang đơn
- [ ] Không có khóa: ghi rõ hai tiêu chí còn mở và lý do
**Kiểm tra:** `dotnet test`, script gộp coverage, kiểm tay khi có khóa
**Phụ thuộc:** COM-E2E5, SHIP-09 (chỉ phần sandbox GHN) · **Quy mô:** S

## Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ dòng ghi chờ khóa
- [ ] Coverage dòng của `Pickory.Modules.Commerce` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `activity-tracking`
