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

export type Quote = { fee: number; serviceId: string; expectedDeliveryDate: string | null };
export type Shipment = { id: string; ref: string; trackingCode: string; fee: number; status: ShipmentStatus };

export { onShipmentStatusChanged } from './events';  // { shipmentId, ref, status, previousStatus, occurredAt }
export { shippingPermissions } from './permissions';  // ['shipping-settings:manage']
```

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
| `pending_pickup` | `ready_to_pick`, `picking` |
| `in_transit` | `picked`, `storing`, `transporting`, `sorting` |
| `delivering` | `delivering`, `money_collect_delivering` |
| `delivered` | `delivered` |
| `delivery_failed` | `delivery_fail` |
| `returning` | `waiting_to_return`, `return`, `return_transporting`, `return_sorting`, `returning` |
| `returned` | `returned` |
| `cancelled` | `cancel` |
| `exception` | `exception`, `damage`, `lost` |

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

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/addresses/provinces` | công khai | Tỉnh/thành |
| GET | `/api/addresses/wards?provinceCode=` | công khai | Phường/xã (và quận/huyện nếu cần) |
| POST | `/api/webhooks/ghn` | GHN | Nhận cập nhật trạng thái |
| GET · PUT | `/api/admin/shipping/settings` | `shipping-settings:manage` | Cấu hình. Lỗi 400 `VALIDATION_ERROR` có `details` dạng `[{ key, message }]`. `key` là tên trường, và là `body` khi lỗi cả body. |

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
```

- Đơn bị hủy vận đơn rồi tạo lại: vận đơn cũ chuyển `cancelled`, trường `ref` của nó được đổi thành `ref#cancelled#<n>` để giữ ràng buộc unique.

## Testing

- Unit: `buildParcel`; bảng ánh xạ trạng thái (kể cả trạng thái lạ); xử lý webhook idempotent.
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
2. Có mua bảo hiểm hàng hóa GHN theo giá trị đơn không? Đồ điện tử giá trị cao nên mình đề xuất có. Mặc định: có, `insuredValue` bằng giá trị hàng.
   - Chốt 2026-10-09: có mua. GHN nhận tối đa 5.000.000 đ, nên module gửi giá trị nhỏ hơn giữa giá trị hàng và 5.000.000 đ.
3. Có cho khách xem hàng trước khi nhận (cho xem, không cho thử) không? Mặc định: cho xem, không cho thử.
   - Chốt 2026-10-09: dùng mặc định. Module gửi `required_note = CHOXEMHANGKHONGTHU`.
