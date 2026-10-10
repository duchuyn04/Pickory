# Implementation Plan: shipping

## Overview

Module `shipping` kết nối Pickory với GHN. Module cung cấp danh mục địa chỉ, báo phí, tạo và hủy vận đơn, theo dõi trạng thái giao hàng. Module không biết đơn hàng. Module chỉ nhận mã tham chiếu `ref` và phát sự kiện khi trạng thái vận đơn đổi.

`shipping` chỉ phụ thuộc `identity`. `commerce` cần `shipping` để báo phí ở checkout và tạo vận đơn, nên module này đi trước `commerce`.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-shipping.md`. Spec còn viết theo bản Node. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/shipping/todo.md`.

Ngày 2026-10-09 người dùng chọn làm `shipping` trước khi review catalog. Mục review của catalog vẫn mở trong `tasks/catalog/plan.md`.

Nhánh làm việc: `feat/shipping`, tách từ `main` sau khi `feat/catalog` đã merge vào `main` (commit `5393daa`).

## Kết quả tra tài liệu GHN (2026-10-09)

Nguồn: `https://developer.ghn.vn/en/docs/` (trang `api.ghn.vn` cũ đã chuyển về đây).

1. **Địa chỉ 2 cấp đã có API riêng.**
   - `GET /shiip/public-api/v3/master-data/province/all`: 34 tỉnh/thành, mỗi dòng có `_id`, `name`, `extension_names`, `status`.
   - `GET /shiip/public-api/v3/master-data/ward/all-by-province-id?province_id=`: phường/xã của một tỉnh, tối đa 200 dòng mỗi trang.
   - API cũ 3 cấp (`province`, `district`, `ward` bản v2) vẫn còn.
2. **Tạo vận đơn nhận địa chỉ mới bằng tên.** Đặt `is_new_to_address: true`, rồi gửi `to_province_name` và `to_ward_name` đúng chuỗi `name` của API mới. Không gửi `to_district_name`.
3. **Báo phí chưa rõ với địa chỉ mới.** Trang `calculate-fee` chỉ mô tả `to_district_id` và `to_ward_code` của danh mục cũ. Tài liệu không nói API báo phí có nhận địa chỉ 2 cấp hay không. Đây là rủi ro lớn nhất của module, xem task `SHIP-00`.
4. **`client_order_code` chống tạo trùng.** Mã này duy nhất trong một shop. Gửi lại cùng mã thì GHN trả `order_code` đã tạo, không tạo đơn mới.
5. **Giới hạn của GHN.** `insurance_value` tối đa 5.000.000 đ. `cod_amount` tối đa 50.000.000 đ. Cân nặng tối đa 50.000 g, mỗi cạnh tối đa 200 cm.
6. **`required_note`** nhận `KHONGCHOXEMHANG`, `CHOXEMHANGKHONGTHU`, `CHOTHUHANG`.
7. **Webhook không có chữ ký.** GHN cho đặt header tùy chọn khi đăng ký webhook. GHN thử lại khi nhận 5xx, 408, 429 hoặc timeout, tối đa 6 lần. Webhook có thể gửi trùng, nên phải chống trùng.
8. **Tra vận đơn** bằng `GET /shiip/public-api/v2/shipping-order/detail?order_code=`. Tài liệu không có API tra theo `client_order_code`.
9. **Hủy vận đơn** bằng `POST /shiip/public-api/v2/switch-status/cancel`, cần `reason_code`. Vận đơn ở trạng thái không hủy được thì GHN trả `result: false` với HTTP 200.
10. **Token có thể bị giới hạn theo IP.** GHN trả 401 khi IP không nằm trong danh sách cho phép.

## Đồ thị phụ thuộc

```
SHIP-00 xác minh GHN sandbox (cần token, chạy được song song)
SHIP-01 logic thuần: gộp kiện, ánh xạ trạng thái, mã tham chiếu GHN
 └── SHIP-02 khung module, Contracts, schema, migration, cấu hình GHN_*
      └── SHIP-03 adapter GHN + client giả + test HTTP mock
           ├── SHIP-04 danh mục địa chỉ: đồng bộ, API công khai ──── UI-S1 ô chọn địa chỉ
           ├── SHIP-06 cấu hình shipping (admin) ────────────────── UI-S2 màn cấu hình
           │    └── SHIP-05 báo phí (cần địa chỉ lấy hàng từ cấu hình)
           │    └── SHIP-07 tạo, hủy, tra vận đơn
           │         └── SHIP-08 webhook GHN + job đồng bộ dự phòng
           └── SHIP-09 nghiệm thu: ma trận quyền, coverage, contract test sandbox
```

`SHIP-05` và `SHIP-07` cần `SHIP-06`, vì địa chỉ lấy hàng và gói dịch vụ mặc định nằm trong cấu hình. `SHIP-08` cần `SHIP-07`, vì webhook chỉ nhận vận đơn đã có trong DB.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.Shipping` và `Pickory.Modules.Shipping.Contracts`. `ShippingModule.cs` có `AddShippingModule` và `MapShippingEndpoints`. `Pickory.ArchitectureTests` thêm luật cho module mới.
2. **Interface công khai ở Contracts.** `IShippingService` có `QuoteAsync`, `CreateShipmentAsync`, `CancelShipmentAsync`, `GetShipmentAsync`, `BuildParcel`. Contracts có thêm kiểu `ShippingAddress`, `Parcel`, `Quote`, `Shipment`, `ShipmentStatus` và sự kiện `ShipmentStatusChangedEvent`.
3. **Địa chỉ 2 cấp.** Module lưu tỉnh/thành và phường/xã theo API mới của GHN. Mã là `_id` của GHN ở dạng chuỗi, kèm `name`. Không có cấp quận/huyện. `ShippingAddress` có `ProvinceCode`, `ProvinceName`, `WardCode`, `WardName`, `Line`. Nếu `SHIP-00` cho thấy báo phí vẫn cần mã cũ, reviewer dừng plan và hỏi người dùng.
4. **Mã gửi GHN theo lần tạo.** `client_order_code` là `{ref}` ở lần tạo đầu. Sau mỗi lần hủy, mã thêm hậu tố, ví dụ `{ref}-2`. GHN chống trùng theo mã này, nên bấm tạo lại sau lỗi mạng trả đúng vận đơn cũ. Mã mới sau khi hủy tránh việc GHN trả lại vận đơn đã hủy. Mã tối đa 50 ký tự.
5. **`createShipment` không tự retry.** Đây là luật "Never" của spec. Lỗi mạng trả `SHIPPING_UNAVAILABLE`. Người dùng bấm lại thì module gửi lại cùng `client_order_code`, GHN không tạo đơn thứ hai.
6. **Vận đơn đã hủy đổi `ref`.** Vận đơn cũ chuyển `cancelled`, cột `ref` đổi thành `{ref}#cancelled#{n}`, theo spec. Ràng buộc unique của `ref` vẫn giữ.
7. **Bảo hiểm tối đa 5.000.000 đ.** Open Question 2 chọn mua bảo hiểm bằng giá trị hàng. GHN chỉ nhận tối đa 5.000.000 đ, nên module lấy giá trị nhỏ hơn. Báo cáo ghi rõ giới hạn này.
8. **Cho xem hàng, không cho thử.** Open Question 3 chọn mặc định này. Module gửi `required_note = CHOXEMHANGKHONGTHU`.
9. **Webhook xác thực bằng header bí mật.** GHN không ký payload. Module đọc header `X-Pickory-Webhook-Secret` và so với biến `GHN_WEBHOOK_SECRET` bằng phép so thời gian cố định. Sau đó module chỉ lấy `OrderCode`, rồi gọi API chi tiết của GHN để lấy trạng thái thật, theo luật "Always" của spec.
10. **Chống trùng webhook.** Trạng thái chuẩn không đổi thì không phát sự kiện. Mọi lần GHN đổi trạng thái gốc vẫn ghi một dòng `shipment_events`.
11. **Ánh xạ trạng thái là hàm thuần.** Trạng thái GHN lạ ghi log cảnh báo và giữ trạng thái cũ. Bảng ánh xạ lấy từ spec. `SHIP-00` đối chiếu với tài liệu mã trạng thái của GHN.
12. **Mã lỗi.** `SHIPPING_UNAVAILABLE` (503): GHN lỗi, timeout hoặc 5xx. `SHIPPING_REJECTED` (422): GHN từ chối, ví dụ địa chỉ không phục vụ. Thông báo viết tiếng Việt.
13. **Adapter GHN dùng `HttpClient` qua `IHttpClientFactory`, không thêm package.** Cách này giống provider DeepSeek của `ai-gateway`. Timeout 5 giây. `QuoteAsync` retry 1 lần khi lỗi mạng hoặc 5xx.
14. **Client giả khi thiếu token.** Dev và test thiếu `GHN_TOKEN` thì module dùng `FakeGhnClient`, giống kho ảnh giả của catalog. Production thiếu `GHN_TOKEN`, `GHN_SHOP_ID` hoặc `GHN_WEBHOOK_SECRET` thì dừng khi khởi động.
15. **Token không ra log và frontend.** Test kiểm log của `HttpClient` không chứa giá trị token. Frontend chỉ gọi API của Pickory.
16. **Job chạy nền theo mẫu `ImageDeletionWorker`.** Hai job: đồng bộ địa chỉ mỗi ngày, đồng bộ trạng thái vận đơn mỗi 30 phút. Test gọi thẳng processor, không chờ timer. Lệnh `shipping:sync-addresses` chạy đồng bộ địa chỉ một lần cho dev.
17. **Giới hạn tần suất dùng chung.** `RateLimiter` đang nằm trong module Identity. `SHIP-08` chuyển lớp này sang `Pickory.Shared`, không đổi hành vi. Webhook giới hạn theo IP. `commerce` dùng lại lớp này cho route báo phí.
18. **Audit log.** Sửa cấu hình ghi một dòng audit `shipping.settings.update` trong cùng transaction.
19. **Route báo phí cho giỏ hàng thuộc `commerce`.** Giỏ hàng thuộc `commerce`, và `shipping` không đọc catalog. `shipping` chỉ cung cấp `IShippingService.QuoteAsync`. Người dùng chốt ngày 2026-10-09. `spec/SPEC-shipping.md` đã bỏ route `POST /api/shipping/quote`.
20. **Route theo REST thuần, mức tài nguyên.** Người dùng chốt ngày 2026-10-10. Đường dẫn là danh từ. Hành động đi qua method HTTP và mã trạng thái chuẩn. Response giữ dạng `{ data }`, không kèm link (không có HATEOAS).
   - Route của `shipping` đã theo luật này: `GET` và `PUT /api/admin/shipping/settings`, `GET /api/addresses/provinces`, `GET /api/addresses/wards?provinceCode=`, `POST /api/webhooks/ghn`.
   - Route mới của mọi module từ ngày này phải theo luật này.
   - Route cũ đặt theo hành động, ví dụ `POST /api/auth/login` và `POST /api/admin/staff/{id}/lock`, được đổi trong một plan riêng. Plan đó chạy sau Phase 2 của `shipping` và trước `commerce`.

## Task List

Chi tiết từng task ở `tasks/shipping/todo.md`.

### Phase 0: Xác minh GHN
- [ ] SHIP-00 (chờ token sandbox, không chặn các task khác)

### Phase 1: Nền tảng
- [x] SHIP-01 · [x] SHIP-02 · [x] SHIP-03

### Checkpoint S1
- [x] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [x] Test kiến trúc chặn `Shipping` tham chiếu module khác ngoài Contracts

### Phase 2: Tính năng
- [x] SHIP-04 · [x] UI-S1 · [x] SHIP-06 · [x] UI-S2 · [x] SHIP-05 · [x] SHIP-07 · [ ] SHIP-08

### Checkpoint S2
- [ ] Admin cấu hình địa chỉ lấy hàng trên giao diện, khách chọn được tỉnh/thành và phường/xã
- [ ] Webhook và job đồng bộ phát sự kiện đúng 1 lần cho mỗi lần đổi trạng thái
- [ ] Review với người dùng

### Phase 3: Nghiệm thu
- [ ] SHIP-09

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.Shipping` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `commerce`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Khách chọn tỉnh/thành và phường/xã từ danh mục đồng bộ từ GHN | SHIP-04, UI-S1, UI-S2 | Test tích hợp đồng bộ và API; Vitest ô chọn; E2E qua màn cấu hình (UI-S2). E2E ở checkout thuộc `commerce` |
| Báo phí p95 dưới 2 giây trên sandbox, hoặc trả lỗi rõ ràng | SHIP-05, SHIP-09 | Test HTTP mock cho timeout và lỗi; đo p95 với sandbox khi có token (chưa có token, để mở) |
| Tạo vận đơn trên sandbox, lưu đúng mã; bấm lại sau lỗi mạng không tạo đơn thứ hai | SHIP-07, SHIP-09 | Test tích hợp với client giả mô phỏng lỗi mạng; contract test sandbox khi có token (chưa có token, để mở) |
| Webhook cập nhật trạng thái, phát `ShipmentStatusChangedEvent` đúng 1 lần mỗi lần đổi | SHIP-08 | Test tích hợp gửi trùng webhook |
| Tắt webhook, job đồng bộ cập nhật trạng thái trong 30 phút | SHIP-08 | Test tích hợp gọi processor; test cấu hình chu kỳ 30 phút |
| `support` gọi API cấu hình nhận 403 | SHIP-06, SHIP-09 | Ma trận quyền |
| Coverage ít nhất 80% | SHIP-09 | Coverage gộp unit và tích hợp, cách đo như catalog |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| API báo phí không nhận địa chỉ 2 cấp | Phải lưu thêm mã cũ hoặc tự ánh xạ địa chỉ mới sang cũ | `SHIP-00` xác minh trước. Kết quả sai thì dừng và hỏi người dùng |
| Chưa có token GHN sandbox | Không kiểm được p95 báo phí và tạo vận đơn thật | Làm toàn bộ bằng client giả. Hai tiêu chí sandbox để mở như mục Cloudinary của catalog |
| Token giới hạn IP | Máy dev hoặc server không gọi được GHN | `SHIP-00` ghi IP cần đăng ký |
| Webhook gửi trùng hoặc sai thứ tự | Phát sự kiện thừa | Luôn tra trạng thái thật bằng API, so với trạng thái đã lưu |

## Quyết định của người dùng (2026-10-09)

1. **Chưa có token GHN sandbox.** `SHIP-00` chờ token. Các task khác làm bằng `FakeGhnClient`. Hai tiêu chí cần sandbox (p95 báo phí, tạo vận đơn thật) để mở, giống mục Cloudinary của catalog.
2. **Route báo phí cho giỏ hàng thuộc `commerce`.** Xem quyết định 19.
3. **Bảo hiểm tối đa 5.000.000 đ.** Xem quyết định 7.
4. **Nhánh `feat/shipping`.**
