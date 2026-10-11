# ADR 0003: Kết quả xác minh GHN sandbox cho module shipping

- Ngày: 2026-10-11
- Trạng thái: Đã xác minh trên sandbox. Code hiện tại lệch ở báo phí và thời gian giao (xem mục "Ảnh hưởng tới code")
- Liên quan: `spec/SPEC-shipping.md`, `tasks/shipping/plan.md` (quyết định 3, 4, 11), `tasks/shipping/todo.md` SHIP-00

## Phạm vi và cách xác minh

- Môi trường: `https://dev-online-gateway.ghn.vn` (staging của GHN, gọi là sandbox trong tài liệu này).
- Shop thử: `200974`. Token lấy từ `.env`, không ghi vào tài liệu này.
- Script tạm nằm ngoài repo. Mọi request và response bên dưới đã che token, tên, số điện thoại và địa chỉ.
- Người nhận trong đơn thử là dữ liệu giả (`Khach Thu Pickory`). Cả 3 vận đơn thử đã hủy (xem mục 3).
- Địa chỉ thử: gần là `Hồ Chí Minh` / `Phường Thủ Đức`, xa là `Lào Cai` / `Phường Lào Cai`. Địa chỉ lấy hàng của shop thử nằm ở Thủ Đức, Hồ Chí Minh.

Giá trị danh mục dùng trong bằng chứng:

| Nơi | Tỉnh `_id` | Tên tỉnh | Phường `_id` | Tên phường |
| --- | --- | --- | --- | --- |
| Gần | 1000001 | Hồ Chí Minh | 1003580 | Phường Thủ Đức |
| Xa | 1000024 | Lào Cai | 1004860 | Phường Lào Cai |

`province/all` của API v3 trả 34 tỉnh/thành, đúng với plan. `ward/all-by-province-id` trả 168 phường/xã cho Hồ Chí Minh và 99 cho Lào Cai khi phân trang 200 dòng.

## Nguồn

- Tính phí: https://developer.ghn.vn/en/docs/order/calculate-fee
- Tạo đơn: https://developer.ghn.vn/en/docs/order/create
- Hủy đơn: https://developer.ghn.vn/en/docs/order/cancel
- Tra đơn theo mã khách: https://developer.ghn.vn/en/docs/order/info-by-client-code
- Mã trạng thái: https://developer.ghn.vn/en/docs/master-data/order-status
- Mã lý do: https://developer.ghn.vn/en/docs/master-data/reason-code
- Tổng quan API: https://developer.ghn.vn/en/docs/getting-started/overview

## Tiêu chí 1: báo phí nhận địa chỉ 2 cấp

Câu hỏi: API `shipping-order/fee` nhận tỉnh/thành và phường/xã mới của danh mục v3 hay không.

Kết quả: **Có.** API nhận địa chỉ 2 cấp qua `is_new_to_address: true`, `to_ward_id_v2` và `to_province_id_v2`.

- Trang `calculate-fee` không nêu các trường này. Tên trường lấy từ thông báo lỗi của GHN và từ response `shipping-order/detail`, nơi đơn đã tạo có `to_ward_id_v2` và `to_province_id_v2`.
- `to_ward_id_v2` là `_id` của phường trong API v3, kiểu số.
- Phí thay đổi theo phường: Phường Thủ Đức 20.900 đ, Phường Lào Cai 60.500 đ (gói `service_type_id` 2, 1.000 g, 20x15x10 cm).

| Cách gửi | HTTP | Kết quả |
| --- | --- | --- |
| `to_ward_code: "1003580"` (cách code hiện tại, không có `to_district_id`) | 400 | `to ward not found`, `SEND_WARD_IS_INVALID` |
| `is_new_to_address: true` cùng `to_province_name` và `to_ward_name` | 400 | `mã phường mới không tồn tại: 0` |
| `to_province_id_v2` và `to_ward_id_v2`, không có `is_new_to_address` | 400 | `to district not found` |
| `is_new_to_address: true`, `to_province_id_v2: 1000001`, `to_ward_id_v2: 1003580` | 200 | `total: 20900` |
| Như trên với Lào Cai (`1000024`, `1004860`) | 200 | `total: 60500` |
| `is_new_to_address: true`, chỉ `to_ward_id_v2: 1003580` | 200 | `total: 20900` |
| `is_new_to_address: true`, `to_ward_id_v2: 99999999` | 400 | `mã phường mới không tồn tại: 99999999` |
| `to_district_id: 3695`, `to_ward_code: "90741"` (danh mục cũ) | 200 | `total: 20900` |

Ghi chú:
- GHN không kiểm tỉnh với phường: `to_province_id_v2` sai tỉnh vẫn trả 200 và phí theo phường. Module phải tự giữ cặp tỉnh và phường nhất quán.
- Code hiện tại gửi mã phường vào `to_ward_code` nên mọi báo phí thật sẽ bị GHN từ chối.

Request thành công (đã che):

```
POST /shiip/public-api/v2/shipping-order/fee
Token: ***   ShopId: 200974
{ "service_type_id": 2, "weight": 1000, "length": 20, "width": 15, "height": 10,
  "insurance_value": 0, "cod_value": 0,
  "is_new_to_address": true, "to_province_id_v2": 1000024, "to_ward_id_v2": 1004860 }
```

Response (rút gọn):

```
200 { "code": 200, "message": "Success", "data": { "total": 60500, "service_fee": 60500,
      "insurance_fee": 0, "cod_fee": 0, ... } }
```

Quyết định 3 của plan (lưu địa chỉ 2 cấp, không có quận/huyện) vẫn đúng. Không cần dừng plan.

## Tiêu chí 2: mã trạng thái vận đơn

Câu hỏi: danh sách mã trạng thái của GHN so với bảng ánh xạ trong spec và `ShipmentStatusMapper.cs`.

Kết quả: 23 mã trạng thái trong tài liệu GHN. Lúc xác minh, spec và code ánh xạ 20 mã. 3 mã chưa ánh xạ: `money_collect_picking`, `return_fail`, `scrap`. SHIP-10 đã thêm 3 mã này (xem bảng). Mọi mã code ánh xạ đều có trong tài liệu GHN.

| Mã GHN | Trạng thái chuẩn trong code | Có trong spec | Ghi chú |
| --- | --- | --- | --- |
| `ready_to_pick` | `PendingPickup` | Có | |
| `picking` | `PendingPickup` | Có | |
| `money_collect_picking` | `PendingPickup` (thêm ở SHIP-10) | Có (SHIP-10) | Thu tiền tại điểm lấy hàng. Hàng chưa rời shop |
| `picked` | `InTransit` | Có | |
| `storing` | `InTransit` | Có | |
| `sorting` | `InTransit` | Có | |
| `transporting` | `InTransit` | Có | |
| `delivering` | `Delivering` | Có | |
| `money_collect_delivering` | `Delivering` | Có | |
| `delivered` | `Delivered` | Có | Trạng thái cuối |
| `delivery_fail` | `DeliveryFailed` | Có | |
| `waiting_to_return` | `Returning` | Có | |
| `return` | `Returning` | Có | |
| `return_transporting` | `Returning` | Có | |
| `return_sorting` | `Returning` | Có | |
| `returning` | `Returning` | Có | |
| `return_fail` | `Returning` (thêm ở SHIP-10) | Có (SHIP-10) | Trả hàng thất bại một lần. GHN còn thử lại, nên không phải trạng thái cuối. Bản đầu của ADR đề xuất `Exception`, nhưng `Exception` làm job đồng bộ coi đơn là cần xử lý tay |
| `returned` | `Returned` | Có | Trạng thái cuối |
| `cancel` | `Cancelled` | Có | Trạng thái cuối |
| `exception` | `Exception` | Có | Trạng thái cuối |
| `lost` | `Exception` | Có | Trạng thái cuối |
| `damage` | `Exception` | Có | Trạng thái cuối |
| `scrap` | `Exception` (thêm ở SHIP-10) | Có (SHIP-10) | Hàng bị hủy bỏ. Cùng nhóm với `lost` và `damage` |

Hành vi hiện tại với mã chưa ánh xạ là đúng spec: giữ trạng thái cũ và ghi log cảnh báo. Ba mã trên có thể xuất hiện thật, nên nên ánh xạ chúng.

Tài liệu GHN dặn: "Your system should handle unknown codes gracefully, since GHN may add more over time."

## Tiêu chí 3: gửi lại cùng `client_order_code`

Câu hỏi: gửi lại đúng request tạo đơn có tạo đơn thứ hai hay không.

Kết quả: **Không.** GHN trả lại cùng `order_code`.

- Lần 1: `client_order_code: "ship00-20261011011227"` trả `order_code: "L8YUF4"` (HTTP 200).
- Lần 2, cùng body: trả `order_code: "L8YUF4"` (HTTP 200).
- `shipping-order/detail?order_code=L8YUF4` trả `client_order_code` khớp và `status: ready_to_pick`.
- `shipping-order/detail-by-client-code` với mã trên trả đúng một đơn `L8YUF4`.

Request tạo đơn 2 cấp (đã che):

```
POST /shiip/public-api/v2/shipping-order/create
{ "to_name": "<che>", "to_phone": "<che>", "to_address": "<che>",
  "is_new_to_address": true, "to_province_name": "Hồ Chí Minh", "to_ward_name": "Phường Thủ Đức",
  "client_order_code": "ship00-20261011011227", "weight": 500, "length": 15, "width": 10, "height": 5,
  "service_type_id": 2, "payment_type_id": 2, "required_note": "CHOXEMHANGKHONGTHU",
  "cod_amount": 0, "insurance_value": 0, "content": "Don thu SHIP-00",
  "items": [ { "name": "Hang thu", "quantity": 1, "weight": 500 } ] }
```

Response: `200 { "data": { "order_code": "L8YUF4", "sort_code": "GXT-M-11-00", "fee": { "main_service": 20900, ... } } }`.

Phát hiện thêm:
- Tạo đơn bằng tên tỉnh và tên phường (`to_province_name`, `to_ward_name`) với `is_new_to_address: true` chạy đúng. GHN tự ghi `to_ward_id_v2: 1003580` và `to_province_id_v2: 1000001` vào đơn.
- Shop thử vẫn dùng địa chỉ lấy hàng danh mục cũ (`is_new_address: false`). Tạo đơn không gửi `from_*` vẫn chạy, và GHN dùng địa chỉ của shop.
- **Tra theo `client_order_code` có API.** Tài liệu cũ của plan (kết quả tra tài liệu mục 8) ghi là không có. Trang `info-by-client-code` mô tả `GET .../shipping-order/detail-by-client-code?client_order_code=`. Sandbox chấp nhận cả `GET` có query và `POST` có body. API chỉ tìm thấy đơn chưa hủy (xem tiêu chí 5).

## Tiêu chí 4: `leadtime` nhận địa chỉ 2 cấp

Kết quả: **Có.** `shipping-order/leadtime` nhận cùng ba trường như báo phí.

| Cách gửi | HTTP | Kết quả |
| --- | --- | --- |
| `to_ward_code: "1003580"` (cách code hiện tại) | 400 | `Field validation for 'ToDistrictID' failed on the 'required' tag` |
| `is_new_to_address: true`, `to_province_id_v2: 1000001`, `to_ward_id_v2: 1003580` | 200 | `leadtime` 2026-10-11 16:59:59 UTC |
| Như trên với Lào Cai | 200 | `leadtime` 2026-10-14 16:59:59 UTC |
| `is_new_to_address: true`, chỉ `to_ward_id_v2` | 200 | Có `leadtime` |
| `to_ward_id_v2: 99999999` | 400 | `mã phường mới không tồn tại: 99999999` |

Response thành công (rút gọn): `{ "leadtime": 1791997199, "leadtime_order": { "from_estimate_date": "2026-10-14T16:59:59Z", "to_estimate_date": "2026-10-15T16:59:59Z" } }`.

- `leadtime` là số giây Unix. Đúng với `GhnLeadTime(long? Leadtime)` và `GhnLeadTimeDate`.
- Giá trị nằm ở 16:59:59 UTC, tức cuối ngày theo giờ Việt Nam (23:59:59 +07:00). Cách đổi sang ngày trong `GhnLeadTimeDate.ToVietnamDate` cho đúng ngày giao.

## Tiêu chí 5: hủy vận đơn

Câu hỏi: trạng thái nào còn hủy được, và hủy lặp lại, tạo lại sau khi hủy hoạt động ra sao.

Kết quả:

1. **Hủy lần đầu** đơn `ready_to_pick`: `200 { "data": [ { "order_code": "L8YUF4", "result": true, "message": "OK" } ] }`. Đơn chuyển `cancel`.
2. **Hủy lần hai** cùng đơn: vẫn `200` với `result: true`, `message: "OK"`. Hủy là idempotent, GHN không trả `result: false` cho đơn đã hủy.
3. **Tạo lại cùng `client_order_code` sau khi hủy**: GHN tạo **đơn mới** `L8YUFK`, không trả lại đơn đã hủy `L8YUF4`.
   - Gửi lại lần nữa trả `L8YUFK` (chống trùng vẫn chạy với đơn đang hoạt động).
   - `detail-by-client-code` trả `L8YUFK`. Sau khi hủy `L8YUFK`, API trả 400 `Đơn hàng không tồn tại`.
4. **Hủy theo lô có mã không tồn tại**: `400 Đơn hàng không tồn tại`, không đơn nào bị hủy (đơn còn lại giữ `ready_to_pick`). Đúng với mô tả "tất cả hoặc không gì" của tài liệu.
5. `reason_code` `GHN-CO003` và `GHN-CANCEL-OTHER` (kèm `reason`) đều được nhận.

Trạng thái còn hủy được:
- Tài liệu GHN không liệt kê. Trang `cancel` chỉ nói `result: false` xảy ra khi đơn "đã ở trạng thái không hủy được".
- Sandbox xác nhận `ready_to_pick` hủy được. Sandbox không cho đẩy đơn sang trạng thái khác, nên chưa quan sát được trường hợp `result: false`.
- Chưa kiểm được: trạng thái từ `picking` trở đi. `GhnHttpClient.CancelOrderAsync` đã xử lý `result: false` như từ chối nghiệp vụ. Contract test ở SHIP-09 chỉ kiểm được đến `ready_to_pick`.

Mã vận đơn thử đã tạo (tất cả đã hủy):

| `client_order_code` | `order_code` | Trạng thái cuối |
| --- | --- | --- |
| `ship00-20261011011227` | `L8YUF4` | `cancel` |
| `ship00-20261011011227` (tạo lại sau khi hủy) | `L8YUFK` | `cancel` |
| `ship00-b-...` (đơn thử thứ hai) | `L8YUFW` | `cancel` |

## Tiêu chí 6: giới hạn IP của token

Kết quả: **Chưa bật giới hạn IP trên token thử, và máy dev gọi được sandbox mà không đăng ký IP.**

- Người dùng chưa bật danh sách "Token API & IP tin cậy" trên trang tài khoản GHN.
- Máy dev gọi `shop/all`, `fee`, `leadtime`, `create`, `cancel` đều nhận HTTP 200 mà không đăng ký IP nào.
- Gọi thiếu header `Token` trả `401 Authorization header is required!`.
- Tài liệu công khai của GHN không có trang nào nói về danh sách IP. Mục điều hướng không có trang IP. Trang tổng quan chỉ dặn không ghi `Token` vào log, URL hay mã phía client. Kết quả tìm kiếm web không cho nguồn chính thức của GHN.
- Hệ quả: chưa có nguồn nào chỉ ra lỗi khi IP không được phép. Plan mục 10 (401 khi sai IP) vẫn là suy đoán.

IP cần đăng ký khi chạy production: IP công khai đi ra (outbound) của máy chủ chạy `Pickory.Api`. Nếu máy chủ nằm sau NAT, đó là IP của cổng NAT. Webhook là chiều ngược lại, nên không cần đăng ký IP cho webhook. Không ghi IP cụ thể vào tài liệu này.

Việc cần làm trước khi bật giới hạn IP ở production: gọi `fee` từ máy chủ đích và ghi mã lỗi thực tế.

## Ảnh hưởng tới code

SHIP-00 không sửa code. Bảng dưới liệt kê chỗ lệch tại thời điểm xác minh. Danh sách ngay dưới bảng ghi kết quả xử lý của SHIP-10 (2026-10-11).

| # | Vị trí | Hiện tại | Kết quả sandbox | Đề xuất |
| --- | --- | --- | --- | --- |
| 1 | `apps/backend/src/Pickory.Modules.Shipping/Ghn/GhnModels.cs:28-36` (`GhnFeeRequest`), `ShippingService.cs:96-105` | Gửi `to_ward_code` = mã phường mới | GHN trả 400 `to ward not found` | Thay `ToWardCode` bằng `IsNewToAddress = true`, `ToProvinceIdV2` (số), `ToWardIdV2` (số). `SnakeCaseLower` cho ra đúng tên trường |
| 2 | `Ghn/GhnModels.cs:42-48` (`GhnLeadTimeRequest`), `ShippingService.cs:107-108` | Giống dòng 1 | GHN trả 400 `ToDistrictID required` | Sửa giống dòng 1 |
| 3 | `Ghn/GhnModels.cs:26-27`, `:40-41` | Comment ghi "GIẢ ĐỊNH chờ SHIP-00" | Đã xác minh | Cập nhật comment theo kết quả mục 1 và 4 |
| 4 | `Ghn/FakeGhnClient.cs:107-113` | Client giả nhận `ToWardCode` | Cần khớp request thật | Cập nhật theo dòng 1 và 2. Bản giả nên từ chối phường không có trong danh mục như GHN |
| 5 | `Shipments/GhnOrderCode.cs:5-9`, `ShippingService.cs:171-173` | Comment: hậu tố tránh GHN trả lại vận đơn đã hủy | GHN tạo đơn mới khi tạo lại sau hủy, dù cùng mã | Hậu tố `-n` không còn bắt buộc. Có thể giữ để mỗi lần tạo có mã riêng và log rõ hơn. Sửa lại lý do trong comment và quyết định 4 của plan |
| 6 | `Shipments/ShipmentStatusMapper.cs:14-35`, bảng spec dòng 67 | Thiếu `money_collect_picking`, `return_fail`, `scrap` | Có trong tài liệu GHN | Thêm 3 mã (đề xuất ở tiêu chí 2) và cập nhật bảng spec |
| 7 | `tasks/shipping/plan.md` mục "Kết quả tra tài liệu GHN" dòng 3, 4, 8 | Dòng 3: báo phí chưa rõ. Dòng 4: gửi lại trả đơn cũ. Dòng 8: không có API tra theo `client_order_code` | Dòng 3 đã rõ. Dòng 4 chỉ đúng khi đơn chưa hủy. Dòng 8 sai | Cập nhật plan theo ADR này. Job đồng bộ dự phòng và xử lý lỗi mạng có thể dùng `detail-by-client-code` cho đơn đang hoạt động |
| 8 | `Ghn/GhnHttpClient.cs` `CancelOrderAsync` | Coi `result: false` là từ chối | Hủy lần hai trả `result: true` | Không cần sửa. Hủy lặp lại không lỗi |
| 9 | Shop production | Shop sandbox dùng địa chỉ lấy hàng danh mục cũ | Tạo đơn không gửi `from_*` vẫn chạy | Khi cấu hình shop production, kiểm shop có `is_new_address` hay không. Báo phí và tạo đơn vẫn nhận địa chỉ nhận 2 cấp ở cả hai trường hợp |

Kết quả xử lý của SHIP-10:
- Dòng 1 và 2: `GhnFeeRequest` và `GhnLeadTimeRequest` gửi `is_new_to_address`, `to_province_id_v2` và `to_ward_id_v2` dạng số. `GhnAddressIds` đổi mã chuỗi sang số. Mã không đọc được thành số dương thì module không gọi GHN và trả `SHIPPING_REJECTED`.
- Dòng 3: comment "GIẢ ĐỊNH" đã thay bằng lý do đúng.
- Dòng 4: `FakeGhnClient` kiểm cờ và mã phường, từ chối giống GHN. Bản giả vẫn khác GHN ở hai điểm: hủy lần hai bị từ chối (GHN trả `result: true`), và tạo lại mã của đơn đã hủy trả đơn đã hủy (GHN tạo đơn mới). Hai điểm này chưa sửa.
- Dòng 5: comment về hậu tố đã sửa. Hậu tố vẫn giữ.
- Dòng 6: đã thêm 3 mã và cập nhật bảng trong spec.
- Dòng 7: `plan.md` đã sửa theo ADR này.
- Dòng 8 và 9: không đổi.

Điểm không ảnh hưởng, đã khớp với code:
- `GhnFee(Total, ServiceFee, InsuranceFee, CodFee)` khớp response báo phí.
- `GhnCreatedOrder(OrderCode, TotalFee, ExpectedDeliveryTime)` khớp response tạo đơn (`order_code`, `total_fee`, `expected_delivery_time`).
- Tạo đơn bằng tên tỉnh và tên phường (`GhnCreateOrderRequest`) chạy đúng.
- Danh mục `province/all` và `ward/all-by-province-id` đúng với `GhnProvince`, `GhnWard`.
