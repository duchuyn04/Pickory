# Spec: shipping

Module id: `shipping` · Phụ thuộc: `identity` · Quy ước chung: `SPEC.md`

## Objective

Kết nối với đơn vị vận chuyển GHN: cung cấp danh mục địa chỉ, tính phí giao hàng, tạo và hủy vận đơn, theo dõi trạng thái. Module này **không biết đơn hàng là gì**. Nó nhận một mã tham chiếu (`ref`, là mã đơn của `commerce`) và phát sự kiện khi trạng thái vận đơn đổi. Nhờ vậy sau này thêm GHTK hay đơn vị khác chỉ cần viết thêm adapter.

**User stories**
- Là khách, khi nhập địa chỉ ở checkout, tôi chọn tỉnh/thành và phường/xã từ danh sách, không phải tự gõ.
- Là khách, tôi thấy phí vận chuyển và ngày giao dự kiến ngay khi chọn địa chỉ.
- Là khách, tôi thấy mã vận đơn và trạng thái giao hàng trong trang đơn hàng.
- Là nhân viên `sales`, tôi bấm "Tạo vận đơn" trên đơn hàng, hệ thống tạo đơn bên GHN, không phải nhập lại thông tin.
- Là nhân viên `sales`, tôi chỉnh được cân nặng và kích thước gói hàng trước khi tạo vận đơn.
- Là admin, tôi cấu hình địa chỉ lấy hàng và tài khoản GHN của shop.

## Thiết kế

### Interface public

```ts
// apps/backend/src/modules/shipping/index.ts
export type Address = {
  provinceCode: string;       // mã theo danh mục địa chỉ của GHN
  wardCode: string;
  districtCode?: string;      // chỉ khi GHN vẫn yêu cầu, xem Open Questions
  line: string;               // số nhà, tên đường
};

export type Parcel = { weightGrams: number; lengthCm: number; widthCm: number; heightCm: number };

export const shipping: {
  quote(input: { to: Address; parcel: Parcel; codAmount: number; insuredValue: number }): Promise<Quote>;
  createShipment(input: {
    ref: string;                         // mã đơn của commerce
    to: Address & { name: string; phone: string };
    parcel: Parcel;
    items: { name: string; quantity: number }[];
    codAmount: number;                   // 0 nếu đã thanh toán SePay
    insuredValue: number;
    note?: string;
  }): Promise<Shipment>;
  cancelShipment(shipmentId: string): Promise<void>;
  getShipment(shipmentId: string): Promise<Shipment | null>;
  buildParcel(items: { parcel: Parcel; quantity: number }[]): Parcel;   // gộp nhiều SKU thành 1 kiện
};

export type Quote = { fee: number; serviceId: string; expectedDeliveryDate: string | null; source: 'ghn' | 'cached' | 'fallback' };
export type Shipment = { id: string; ref: string; trackingCode: string; fee: number; status: ShipmentStatus };

export { onShipmentStatusChanged } from './events';  // { shipmentId, ref, status, previousStatus, occurredAt }
export { shippingPermissions } from './permissions';  // ['shipping-settings:manage']
```

- `Quote.source` cho biết nguồn của phí: `ghn` là phí GHN vừa tính, `cached` là phí GHN đã lưu, `fallback` là phí theo bảng dự phòng. Hai nguồn sau có `expectedDeliveryDate` là `null`. Xem mục "Phí dự phòng".
- Mọi lỗi từ GHN quy về `ShippingUnavailableError` (GHN lỗi, timeout) hoặc `ShippingRejectedError` (GHN từ chối, ví dụ địa chỉ không phục vụ). Lỗi kèm thông báo tiếng Việt cho người dùng.
- `quote` timeout 5 giây, retry 1 lần với lỗi mạng hoặc 5xx. `createShipment` **không retry tự động** để tránh tạo trùng vận đơn. Nếu lỗi mạng, người dùng bấm tạo lại. Module gửi lại cùng `client_order_code`, và GHN trả vận đơn đã tạo thay vì tạo vận đơn mới (cập nhật 2026-10-09, xem `tasks/shipping/plan.md`).

### Gộp kiện hàng

`buildParcel` gộp nhiều SKU thành 1 kiện:
- Cân nặng = tổng cân nặng × số lượng.
- Dài, rộng = lớn nhất trong các SKU.
- Cao = tổng chiều cao × số lượng.

Cách gộp này ước lượng dư nên phí báo cho khách không thấp hơn phí thật. Khi tạo vận đơn, `sales` sửa được kích thước thật. Nếu phí thật khác phí đã báo khách, shop chịu phần chênh, không thu thêm của khách.

### Trạng thái vận đơn

Trạng thái chuẩn hóa, không phụ thuộc đơn vị vận chuyển:

| Trạng thái chuẩn | Trạng thái GHN tương ứng (xác minh lại theo tài liệu GHN) |
|---|---|
| `pending_pickup` | `ready_to_pick`, `picking`, `money_collect_picking` |
| `in_transit` | `picked`, `storing`, `transporting`, `sorting` |
| `delivering` | `delivering`, `money_collect_delivering` |
| `delivered` | `delivered` |
| `delivery_failed` | `delivery_fail` |
| `returning` | `waiting_to_return`, `return`, `return_transporting`, `return_sorting`, `returning`, `return_fail` |
| `returned` | `returned` |
| `cancelled` | `cancel` |
| `exception` | `exception`, `damage`, `lost`, `scrap` |

- Trạng thái GHN chưa có trong bảng: ghi log cảnh báo, giữ trạng thái cũ, không làm hỏng luồng.
- Lưu lịch sử mọi lần đổi trạng thái (`shipment_events`) kèm trạng thái gốc của GHN.

### Nhận cập nhật từ GHN

- **Webhook:** `POST /api/webhooks/ghn`. Không tin dữ liệu trong payload: chỉ lấy mã vận đơn, rồi gọi API GHN lấy trạng thái thật. Xử lý idempotent: trạng thái không đổi thì không phát sự kiện.
- **Đồng bộ dự phòng:** job chạy mỗi 30 phút, tra lại các vận đơn chưa kết thúc (khác `delivered`, `returned`, `cancelled`) để bù cho webhook bị mất.
- Endpoint webhook có giới hạn tần suất và chỉ nhận mã vận đơn có trong DB.

### Danh mục địa chỉ

- Lấy từ API master data của GHN, lưu vào bảng `address_provinces`, `address_wards` (và `address_districts` nếu cần). Job đồng bộ chạy mỗi ngày.
- Frontend lấy danh sách qua API của Pickory, không gọi GHN trực tiếp.
- Địa chỉ đã lưu trong đơn hàng giữ cả mã và tên tại thời điểm đặt, để danh mục đổi về sau không làm sai đơn cũ.

### Cấu hình

Bảng `shipping_settings` (sửa bằng `shipping-settings:manage`, ghi audit log):
- Địa chỉ lấy hàng (tên, SĐT, địa chỉ có mã).
- GHN `ShopId`. Token GHN nằm trong biến môi trường, không lưu DB.
- Gói dịch vụ GHN mặc định.

```bash
GHN_API_BASE=https://dev-online-gateway.ghn.vn   # sandbox; production đổi URL
GHN_TOKEN=
GHN_SHOP_ID=
GHN_LIVE_TESTS=0
```

### Phí dự phòng

GHN lỗi lúc báo phí thì khách không đặt được đơn. Phí dự phòng cho phép shop vẫn báo một mức phí gần đúng. SHIP-13 làm bảng phí và cách tính. SHIP-14 làm phần báo phí khi GHN không phản hồi, ghi ở mục "Báo phí khi GHN không phản hồi".

Người dùng chốt các quyết định sau ngày 2026-10-19:

1. Khi GHN không phản hồi, shipping dùng phí GHN đã lưu trong 24 giờ trước. Chưa có phí đã lưu thì dùng bảng phí dự phòng.
2. Bảng phí chia 3 tuyến theo vùng của GHN: nội tỉnh, nội vùng, liên vùng.
3. Khối lượng tính cước giống GHN. Admin nhập giá đã gồm phụ phí.
4. Chỉ dùng phí dự phòng khi GHN không phản hồi: lỗi mạng, hết thời gian chờ hoặc 5xx. GHN từ chối yêu cầu thì vẫn báo lỗi.
5. Admin bật hoặc tắt phí dự phòng. Tắt thì giữ cách báo lỗi hiện tại.
6. Khách không thấy nhãn nào khác. Trang đơn admin có nhãn "Phí ship dự phòng".
7. Phí dự phòng không có ngày giao dự kiến.

#### Bảng phí

Mỗi tuyến có 3 giá trị:

| Giá trị | Đơn vị | Giới hạn |
|---|---|---|
| Giá gốc | đồng | từ 0 đến 5.000.000 |
| Khối lượng gốc (đã gồm trong giá gốc) | gram | từ 1 đến 20.000 |
| Giá bước (cho mỗi 500 g tiếp theo) | đồng | từ 0 đến 5.000.000 |

- Mọi giá trị là số nguyên.
- Bảng lưu trong `shipping_fallback_fee`. Bảng chỉ có một dòng, mã dòng là `singleton`.
- Khi tắt, chín giá trị được phép để trống. Giá trị đã nhập vẫn phải đúng giới hạn.
- Khi bật, cả chín giá trị phải có.

#### Vùng và tuyến

- Code giữ bảng 34 tỉnh theo đơn vị hành chính từ ngày 1/7/2025, ghép theo mã tỉnh của GHN API v3. Mã lấy từ `province/all` của sandbox.
- Vùng 3 có 18 tỉnh từ Hà Tĩnh trở ra miền Bắc. Vùng 2 có 4 tỉnh từ Quảng Trị đến Quảng Ngãi. Vùng 1 có 12 tỉnh từ Gia Lai trở vào miền Nam.
- Cùng mã tỉnh với địa chỉ lấy hàng là nội tỉnh, kể cả khi tỉnh không có trong bảng vùng.
- Khác tỉnh, cùng vùng là nội vùng. Khác vùng là liên vùng.
- Một trong hai tỉnh không có trong bảng vùng thì tuyến là "không xác định". Hàm không đoán tuyến. Khi đó không tính được phí dự phòng.

#### Cách tính phí

1. Tính cân quy đổi (gram) bằng `Dài × Rộng × Cao / 5` (cm). Công thức này tương đương chia 5000 rồi đổi kg sang gram.
2. Lấy số lớn hơn giữa cân thật và cân quy đổi.
3. Làm tròn lên bội số của 500 g. Kết quả là khối lượng tính cước.
4. Khối lượng tính cước không vượt khối lượng gốc thì phí bằng giá gốc.
5. Mỗi 500 g vượt, kể cả phần lẻ, cộng một lần giá bước.

Ví dụ: khối lượng gốc 1200 g, giá gốc 10.000 đồng, giá bước 3.000 đồng. Khối lượng tính cước 1500 g vượt 300 g, tính một bước. Phí là 13.000 đồng.

Phép tính dùng số nguyên 64 bit, nên giá lớn nhất và kiện 30 kg không tràn số.

#### Báo phí khi GHN không phản hồi

`QuoteAsync` coi GHN là không phản hồi khi `CalculateFeeAsync` ném `ShippingUnavailableException`. Adapter đưa lỗi mạng, hết thời gian chờ, 5xx, 401, 403 và 429 về lỗi này. Khách bị chặn trong mọi trường hợp đó, nên các trường hợp dùng chung một luật.

Các lỗi sau giữ nguyên, không dùng phí đã lưu hay phí dự phòng:

- `ShippingRejectedException`: GHN đã trả lời và từ chối yêu cầu.
- `ShippingNotConfiguredException`: admin chưa cấu hình địa chỉ lấy hàng.
- Lỗi danh mục địa chỉ: phường không còn trong danh mục, hoặc mã không phải số.

Khi GHN không phản hồi, `QuoteAsync` làm theo thứ tự này:

1. Tìm phí đã lưu còn hạn cùng khóa. Nếu có, trả phí đó. `source` là `cached`.
2. Đọc bảng phí dự phòng. Nếu bảng bật, tuyến xác định được và tuyến đủ giá, trả phí theo bảng. `source` là `fallback`.
3. Nếu không có nguồn nào dùng được, ném lại `ShippingUnavailableException` gốc.

- Tuyến lấy mã tỉnh trong cấu hình lấy hàng và mã tỉnh của địa chỉ nhận.
- Cả hai nguồn giữ `serviceTypeId` đang dùng.
- Cả hai nguồn có `expectedDeliveryDate` là `null`. Lời gọi ngày giao đang chạy song song bị hủy, như khi báo phí lỗi.
- Phí dự phòng vượt giới hạn của `int` (chỉ có với kiện vô lý) coi là không tính được.
- Phí GHN tính thành công có `source` là `ghn`.
- Mỗi lần dùng phí đã lưu hoặc phí dự phòng ghi một log cảnh báo. Log chỉ có nguồn phí, tuyến và khối lượng tính cước. Log không có địa chỉ, tên, số điện thoại hay mã phường.

#### Phí đã lưu

Mỗi lần GHN báo phí thành công, module lưu phí vào bảng `shipping_quote_cache`. Một dòng có sáu cột khóa, cột `fee` và cột `savedAt`. Sáu cột khóa là khóa chính của bảng.

| Cột khóa | Ghi chú |
|---|---|
| `receiverWardCode` | Mã phường nhận |
| `pickupWardCode` | Mã phường lấy hàng trong cấu hình lấy hàng. Đổi địa chỉ lấy hàng thì khóa cũ không còn khớp |
| `serviceTypeId` | Loại dịch vụ |
| `chargeableWeightGrams` | Khối lượng tính cước, tính bằng `FallbackFeeCalculator.ChargeableWeightGrams` |
| `insuredValue` | Giá trị khai giá đã kẹp trần 5.000.000 đồng |
| `codAmount` | Số tiền thu hộ. GHN tính phí thu hộ theo số này |

- Cùng khóa thì ghi đè phí và `savedAt`. Lệnh dùng `INSERT ... ON CONFLICT DO UPDATE` của Postgres, nên hai request song song cùng khóa không gây lỗi.
- Phí còn hạn khi `bây giờ - savedAt < 24 giờ`. Đúng 24 giờ là quá hạn.
- Mỗi lần lưu thành công xóa các dòng có `savedAt` từ 24 giờ trở lên. Chỉ mục trên `savedAt` giúp lệnh xóa không quét cả bảng.
- Lưu phí lỗi chỉ ghi log cảnh báo. Báo phí vẫn trả phí GHN.
- Đọc phí đã lưu hoặc bảng phí dự phòng lỗi thì ghi log cảnh báo và giữ lỗi `SHIPPING_UNAVAILABLE`.

#### Route cho Playwright

Hai route sau chỉ dành cho Playwright. Chúng giúp test E2E (end-to-end) bật và tắt lỗi báo phí của GHN giả. Cả hai trả 204.

- `PUT /api/e2e/ghn/failures/calculate-fee`: bật lỗi `Unavailable` ở hàm báo phí của `FakeGhnClient`.
- `DELETE /api/e2e/ghn/failures/calculate-fee`: tắt lỗi báo phí. Lỗi của các hàm khác giữ nguyên.

Route chỉ có khi API không có `GHN_TOKEN`, DI có `FakeGhnClient` và API chạy bằng lệnh `e2e:start`. Host thường trả 404. Route làm hỏng báo phí mà không cần đăng nhập, nên điều kiện này là bắt buộc.

#### Quyền và audit

- Hai route phí dự phòng dùng quyền `shipping-settings:manage`, giống cấu hình lấy hàng.
- Mỗi lần lưu ghi một dòng audit log với hành động `shipping.fallback_fee.update` và loại đích `shipping_fallback_fee`. Dòng audit có trạng thái trước và sau.
- Lần lưu đầu, trạng thái trước là giá trị mặc định: tắt và chín giá trị trống.

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/addresses/provinces` | công khai | Tỉnh/thành |
| GET | `/api/addresses/wards?provinceCode=` | công khai | Phường/xã (và quận/huyện nếu cần) |
| POST | `/api/webhooks/ghn` | GHN | Nhận cập nhật trạng thái |
| GET · PUT | `/api/admin/shipping/settings` | `shipping-settings:manage` | Cấu hình. Lỗi 400 `VALIDATION_ERROR` có `details` dạng `[{ key, message }]`. `key` là tên trường, và là `body` khi lỗi cả body. |
| GET · PUT | `/api/admin/shipping/fallback-fee` | `shipping-settings:manage` | Bảng phí dự phòng. Body là đối tượng phẳng gồm `enabled` và 9 trường giá, tên như `sameProvinceBaseFee`. `GET` khi chưa lưu trả tắt và 9 trường `null`. Sai kiểu, sai khoảng hoặc trường lạ trả 400 `VALIDATION_ERROR`. Bật mà thiếu giá trả 422 `FALLBACK_FEE_INCOMPLETE`. Cả hai lỗi có `details` dạng `[{ key, message }]`. |

Tạo và hủy vận đơn cho đơn hàng đi qua endpoint của `commerce` (quyền `orders:fulfill`). `commerce` gọi `shipping.createShipment`.

Route báo phí cho giỏ hàng hiện tại thuộc `commerce`, vì giỏ hàng thuộc `commerce`. Route giới hạn 20 lần mỗi phút cho mỗi khách và gọi `shipping.quote`. Người dùng chốt ngày 2026-10-09.

## Data Model (Prisma, rút gọn)

```prisma
model Shipment {
  id            String         @id @default(uuid())
  ref           String         @unique     // mã đơn, mỗi đơn tối đa 1 vận đơn còn hiệu lực
  carrier       String         @default("ghn")
  trackingCode  String         @unique
  fee           Int
  codAmount     Int
  parcel        Json
  status        ShipmentStatus
  rawStatus     String
  createdAt     DateTime       @default(now())
  updatedAt     DateTime       @updatedAt
  @@map("shipments")
}

model ShipmentEvent   { /* shipmentId, status, rawStatus, occurredAt, source(webhook|poll|manual) */ }
model AddressProvince { /* code, name, syncedAt */ }
model AddressWard     { /* code, provinceCode, districtCode?, name, syncedAt */ }
model ShippingSettings { /* pickup address, ghnShopId, defaultServiceTypeId */ }
model ShippingFallbackFee { /* enabled, 3 tuyến x (baseFee, baseWeightGrams, stepFee), updatedAt */ }
model ShippingQuoteCache { /* receiverWardCode, pickupWardCode, serviceTypeId, chargeableWeightGrams, insuredValue, codAmount (sáu cột khóa chính), fee, savedAt (có chỉ mục) */ }
```

- Đơn bị hủy vận đơn rồi tạo lại: vận đơn cũ chuyển `cancelled`, trường `ref` của nó được đổi thành `ref#cancelled#<n>` để giữ ràng buộc unique.

## Testing

- Unit: `buildParcel`; bảng ánh xạ trạng thái (kể cả trạng thái lạ); xử lý webhook idempotent.
- Unit phí dự phòng: bảng 34 tỉnh, chọn tuyến, khối lượng tính cước và phí tại từng giá trị biên (500 g, 501 g, phần lẻ của bước), không tràn số ở giá trị lớn nhất.
- Integration báo phí khi GHN không phản hồi, dùng DB thật và đồng hồ giả:
  - Phí đã lưu còn hạn ở 23:59:59 và quá hạn ở 24:00:00.
  - Mỗi thành phần của khóa khác đi thì phí đã lưu không khớp.
  - Nhiều lần ghi song song cùng khóa không gây lỗi và chỉ còn một dòng.
  - Mỗi lần lưu xóa dòng quá hạn và giữ dòng còn hạn.
  - Phí đã lưu được dùng trước, rồi tới bảng phí dự phòng ở cả 3 tuyến.
  - GHN từ chối giữ nguyên lỗi, kể cả khi có phí đã lưu và bảng đang bật.
  - Lưu phí lỗi vẫn trả phí GHN.
  - Log không có dữ liệu khách.
- Adapter GHN test bằng HTTP mock: thành công, GHN từ chối địa chỉ, 5xx, timeout.
- Integration: webhook chỉ phát sự kiện khi trạng thái thật sự đổi; job đồng bộ cập nhật vận đơn khi webhook bị mất; webhook bị giới hạn tần suất.
- `createShipment` lỗi mạng: không tạo trùng khi bấm lại (tra theo `ref` trước).
- Contract test với GHN sandbox chỉ chạy khi `GHN_LIVE_TESTS=1`: lấy danh mục địa chỉ, báo phí, tạo rồi hủy 1 vận đơn.

## Boundaries (riêng module)

- **Always:** xác minh trạng thái bằng API GHN thay vì tin payload webhook; lưu tên và mã địa chỉ tại thời điểm đặt.
- **Ask first:** thêm đơn vị vận chuyển thứ hai; cho khách chọn gói dịch vụ (nhanh, tiết kiệm); thu thêm phí chênh lệch của khách.
- **Never:** tự động retry `createShipment`; để token GHN ra frontend hoặc log; gọi GHN từ frontend.

## Success Criteria

- [ ] Khách chọn được tỉnh/thành và phường/xã từ danh mục đã đồng bộ từ GHN.
- [ ] Báo phí trả kết quả trong dưới 2 giây (p95, gọi GHN sandbox), hoặc trả lỗi rõ ràng để `commerce` xử lý.
- [ ] Tạo vận đơn trên sandbox thành công và lưu đúng mã vận đơn; bấm tạo lại sau lỗi mạng không sinh vận đơn thứ hai.
- [ ] Webhook GHN cập nhật trạng thái và phát `onShipmentStatusChanged` đúng 1 lần mỗi lần đổi.
- [ ] Tắt webhook, job đồng bộ vẫn cập nhật trạng thái trong vòng 30 phút.
- [ ] Nhân viên `support` gọi API cấu hình nhận 403.
- [ ] Coverage `service.ts` ≥ 80%.

## Open Questions

1. **Địa giới hành chính mới:** từ 01/07/2025 Việt Nam bỏ cấp quận/huyện. Cần kiểm tra trong tài liệu GHN hiện tại xem API địa chỉ và báo phí đang dùng 2 cấp hay vẫn cần mã quận/huyện. Spec để `districtCode` tùy chọn cho tới khi xác minh.
   - Cập nhật 2026-10-09: GHN đã có API danh mục 2 cấp, và API tạo vận đơn nhận địa chỉ 2 cấp. Tài liệu API báo phí chưa nói rõ. Task `SHIP-00` xác minh khi có token sandbox.
   - Chốt 2026-10-11 (sandbox, ADR 0003): báo phí và ngày giao nhận địa chỉ 2 cấp qua `is_new_to_address`, `to_province_id_v2` và `to_ward_id_v2`. Không cần `districtCode`.
2. Có mua bảo hiểm hàng hóa GHN theo giá trị đơn không? Đồ điện tử giá trị cao nên mình đề xuất có. Mặc định: có, `insuredValue` bằng giá trị hàng.
   - Chốt 2026-10-09: có mua. GHN nhận tối đa 5.000.000 đ, nên module gửi giá trị nhỏ hơn giữa giá trị hàng và 5.000.000 đ.
3. Có cho khách xem hàng trước khi nhận (cho xem, không cho thử) không? Mặc định: cho xem, không cho thử.
   - Chốt 2026-10-09: dùng mặc định. Module gửi `required_note = CHOXEMHANGKHONGTHU`.
