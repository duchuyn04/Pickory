# Spec: inventory

> Trạng thái: không triển khai. Ngày 2026-10-27 người dùng chọn giữ tồn kho trong `catalog` và quản lý số lượng tại form sản phẩm. Tài liệu này giữ làm nghiên cứu cho hướng tách module sau này.
>
> Bản đã duyệt ngày 2026-10-22, theo phương án mặc định của mọi câu hỏi mở. Plan: `tasks/inventory/plan.md`. Nghiên cứu nền: `docs/research/inventory-module.md`.

Module id: `inventory` · Phụ thuộc: `identity`, `catalog` · Quy ước chung: `SPEC.md`

## Tóm tắt dễ hiểu

Module `inventory` quản lý hàng trong kho. Hiện nay phần này nằm trong `catalog` và chỉ có một con số tồn kho cho mỗi SKU. SKU là một biến thể bán hàng, ví dụ iPhone 256 GB màu đen.

Module mới tách con số đó thành ba số:

| Tên | Tên trong code | Ý nghĩa |
|---|---|---|
| Số trên kệ | `on_hand` | Số máy đang nằm thật trong kho |
| Số đã giữ | `reserved` | Số máy khách đã đặt nhưng chưa giao cho shipper |
| Số bán được | `available` | Số trên kệ trừ số đã giữ |

Ví dụ: kho nhập 10 chiếc, khách đặt 1 chiếc. Số trên kệ là 10, số đã giữ là 1, số bán được là 9. Khi nhân viên giao chiếc đó cho shipper, số trên kệ còn 9 và số đã giữ về 0.

Module có thêm bốn việc mới:

1. Phiếu nhập kho: ghi hàng nhập từ nhà cung cấp, kèm giá nhập.
2. Kiểm kê: nhân viên đếm hàng thật, hệ thống sửa số trên kệ theo số đếm.
3. Ngưỡng sắp hết riêng cho từng SKU, thay cho số 5 cố định.
4. Giá vốn và giá trị tồn kho: hệ thống tính giá vốn bình quân từ phiếu nhập, rồi tính tổng tiền hàng trong kho.

Mọi thay đổi số lượng ghi vào một sổ (gọi là ledger). Không ai sửa hay xóa được dòng trong sổ. Khi cần sửa sai, nhân viên ghi thêm một dòng điều chỉnh.

## Objective

Cho shop biết chính xác hàng đang có, hàng đang chờ giao, hàng bán được và giá trị hàng trong kho. Giữ nguyên bảo đảm hiện nay: nhiều khách mua cùng lúc không làm bán vượt số bán được.

**User stories**

*Role có `inventory:write`*
- Tôi tạo phiếu nhập kho gồm nhiều SKU, số lượng và giá nhập. Tôi lưu nháp rồi xác nhận khi hàng tới.
- Tôi quản lý danh sách nhà cung cấp.
- Tôi tạo phiếu kiểm kê, nhập số đếm thật, rồi áp dụng phần chênh.
- Tôi điều chỉnh số trên kệ kèm mã lý do, ví dụ hàng hỏng hoặc hàng khách trả.
- Tôi đặt ngưỡng sắp hết cho từng SKU.

*Role có `inventory:read`*
- Tôi xem ba con số của mọi SKU, lọc SKU sắp hết, xem sổ thay đổi và các đơn đang giữ hàng.

*Role có `inventory-cost:read`*
- Tôi xem giá nhập, giá vốn bình quân và tổng giá trị tồn kho. Tôi tải file CSV giá trị tồn kho.

## Thiết kế

### Ranh giới với module khác

| Module | Sở hữu |
|---|---|
| `catalog` | Sản phẩm, SKU, giá bán, trạng thái bật tắt. Ba cột chiếu: `skus.stock`, `products.inStock`, `products.minPrice` |
| `inventory` | Kho, số lượng, giữ hàng, sổ thay đổi, nhà cung cấp, phiếu nhập, kiểm kê, giá vốn |
| `commerce` | Giỏ, đơn, thanh toán. Gọi `inventory` để giữ hàng, trả hàng giữ và xuất kho |

- Cột chiếu là cột `catalog` giữ bản sao của số bán được. Giỏ hàng, bộ lọc "còn hàng" và trang sản phẩm đọc cột chiếu, nên các phần này không đổi.
- Chỉ `catalog` ghi cột chiếu. `catalog` ghi khi `inventory` gọi hàm chiếu, trong cùng transaction với lần đổi số lượng.
- `catalog` tham chiếu `Pickory.Modules.Inventory.Contracts`. `inventory` tham chiếu `Pickory.Modules.Catalog.Contracts`. Hai project Contracts không tham chiếu nhau.

### Kho

- MVP có một kho mặc định. Bảng `warehouses` có đúng một dòng, tạo trong migration.
- Mọi bảng số lượng có cột `warehouse_id`. Thêm kho sau này là thêm dòng dữ liệu.
- API của MVP không nhận `warehouseId`. Module luôn dùng kho mặc định.

### Số lượng

- Mỗi cặp (SKU, kho) có một dòng `stock_levels` với `on_hand`, `reserved`, `reorder_point`, `average_cost`.
- `available` không lưu thành cột. Module tính `available = on_hand - reserved`.
- Ràng buộc DB: `on_hand >= 0`, `reserved >= 0`, `reserved <= on_hand`. Mọi cột số lượng là `NOT NULL`.
- Mọi SKU của `catalog` có một dòng `stock_levels`. `catalog` báo SKU mới cho `inventory` trong transaction lưu sản phẩm.

### Sổ thay đổi (ledger)

- Bảng `stock_movements` ghi mọi lần số lượng đổi. Mỗi dòng có `on_hand_delta` và `reserved_delta`.
- Trigger `BEFORE UPDATE OR DELETE` trên `stock_movements` gọi `RAISE EXCEPTION`. Không câu lệnh nào sửa hay xóa được dòng trong sổ.
- Bất biến: với mỗi dòng `stock_levels`, `on_hand` bằng tổng `on_hand_delta`, `reserved` bằng tổng `reserved_delta` của cùng (SKU, kho).
- Mỗi dòng sổ lưu `unit_cost`: giá nhập với phiếu nhập, giá vốn bình quân lúc ghi với loại khác.

**Loại thay đổi:**

| Loại | Khi nào | `on_hand` | `reserved` |
|---|---|---|---|
| `opening_balance` | Chuyển dữ liệu từ `catalog`, chạy một lần | cộng | cộng |
| `receipt` | Xác nhận phiếu nhập | cộng | không đổi |
| `adjustment` | Nhân viên điều chỉnh tay | cộng hoặc trừ | không đổi |
| `return_restock` | Nhân viên nhập lại hàng khách trả | cộng | không đổi |
| `stocktake` | Áp dụng phiếu kiểm kê | cộng hoặc trừ | không đổi |
| `reserve` | Khách đặt đơn | không đổi | cộng |
| `release` | Đơn hủy hoặc quá hạn khi chưa giao | không đổi | trừ |
| `ship` | Đơn chuyển sang `shipping` | trừ | trừ |
| `unship` | Đơn từ `shipping` về `confirmed` | cộng | cộng |
| `ship_cancel` | Hủy đơn `shipping` có vận đơn đã hủy | cộng | không đổi |
| `seed` | Lệnh seed dữ liệu mẫu | cộng | không đổi |

### Giữ hàng cho đơn

- Bảng `stock_holds` có một dòng cho mỗi cặp (`order_id`, `sku_id`). Cột `status` nhận `active`, `committed` hoặc `released`.
- `active`: hàng còn trên kệ, đã giữ cho đơn. `committed`: hàng đã giao cho shipper. `released`: đơn không cần hàng nữa.

**Vòng đời đơn và thao tác tồn kho** (trạng thái đơn theo `OrderTransitions.cs` của `commerce`):

| Đơn chuyển | Hàm gọi | Trạng thái giữ hàng | Loại thay đổi |
|---|---|---|---|
| Đặt đơn mới | `ReserveAsync` | không có hoặc `released` sang `active` | `reserve` |
| Hủy hoặc quá hạn khi chưa `shipping` | `ReleaseAsync` | `active` sang `released` | `release` |
| `confirmed` sang `shipping` | `CommitAsync` | `active` sang `committed` | `ship` |
| `shipping` về `confirmed` | `UncommitAsync` | `committed` sang `active` | `unship` |
| `shipping` sang `cancelled` | `ReleaseAsync` | `committed` sang `released` | `ship_cancel` |
| `shipping` sang `returned` | không gọi | giữ `committed` | không có |
| `shipping` sang `delivered` | không gọi | giữ `committed` | không có |

- Hàng trả về không tự nhập lại kho. Nhân viên kiểm hàng, rồi tạo điều chỉnh với mã lý do `customer_return`.
- Đơn `shipping` chỉ hủy được khi vận đơn đã hủy (luật của `commerce`). Hàng đã quay về kho, nên `ship_cancel` cộng lại số trên kệ.
- Tiền SePay về muộn cho đơn đã hủy vì quá hạn: `commerce` gọi `ReserveAsync` lại với cùng `orderId`. Dòng giữ `released` chuyển lại `active` nếu còn đủ hàng. Spec `catalog` cũ ghi khác code ở điểm này. Spec này theo code.

**Interface cho `commerce`** (project `Pickory.Modules.Inventory.Contracts`, thư mục `Stock/`):

```csharp
public interface IInventoryService
{
    // Giữ hàng cho đơn. Tất cả hoặc không: thiếu 1 SKU thì không giữ SKU nào.
    Task<ReserveResult> ReserveAsync(
        string orderId, IReadOnlyList<StockItem> items, StockCallerTransaction? caller, CancellationToken cancellationToken);

    // Trả hàng đã giữ. Dòng `active` giảm số đã giữ. Dòng `committed` cộng lại số trên kệ.
    Task ReleaseAsync(string orderId, StockCallerTransaction? caller, CancellationToken cancellationToken);

    // Hàng rời kho khi đơn sang `shipping`.
    Task CommitAsync(string orderId, StockCallerTransaction? caller, CancellationToken cancellationToken);

    // Hàng về kho khi vận đơn bị hủy và đơn về `confirmed`.
    Task UncommitAsync(string orderId, StockCallerTransaction? caller, CancellationToken cancellationToken);
}
```

- `StockItem`, `StockShortage`, `ReserveResult`, `StockCallerTransaction` chuyển từ `Pickory.Modules.Catalog.Contracts` sang đây, giữ nguyên tên và ý nghĩa.
- `IStockService` của `catalog` bị xóa. Module không giữ lớp vỏ cũ.
- Cả bốn hàm idempotent theo `orderId`. Đầu mỗi hàm lấy `pg_advisory_xact_lock` theo `orderId`, nên hai lời gọi cùng đơn chạy nối tiếp.
- Gọi lại một hàm khi dòng giữ đã ở trạng thái đích thì hàm không ghi gì.
- `caller` bằng `null`: module tự mở transaction và tự phát sự kiện sau commit. `caller` có giá trị: bên gọi tự commit rồi gọi `Events.FlushAsync`.
- `Quantity` phải lớn hơn 0. Mục trùng `SkuId` được cộng số lượng.

### Chống bán vượt và thứ tự khóa

Mọi lần đổi số lượng đi qua một lõi ghi duy nhất trong `inventory`. Lõi chạy các bước theo thứ tự:

1. Gọi `catalog` khóa dòng `products` rồi dòng `skus` liên quan, theo thứ tự `id`.
2. Khóa dòng `stock_levels` bằng `SELECT ... FOR UPDATE`, theo thứ tự `sku_id`.
3. Kiểm đủ hàng trước khi ghi. Thiếu hàng thì không ghi gì.
4. Cập nhật bằng câu lệnh có điều kiện, ví dụ `on_hand - reserved >= @quantity`.
5. Ghi dòng sổ thay đổi.
6. Gọi hàm chiếu của `catalog` với số bán được mới.

- Thứ tự khóa luôn là `products`, `skus`, `stock_levels`. Sửa sản phẩm trong `catalog` dùng cùng thứ tự, nên hai việc không deadlock.
- Bước 1 và bước 6 dùng interface `ISkuStockProjection` trong `Pickory.Modules.Catalog.Contracts`.
- Hàm chiếu chạy trong transaction của lõi ghi. Module không chiếu bằng sự kiện, vì bộ phát sự kiện chỉ ghi log khi người nghe lỗi.

### Đồng bộ SKU từ `catalog`

- `inventory` giữ bảng `inventory_skus`: `sku_id`, `product_id`, `product_name`, `code`, `option_label`, `enabled`, `removed_at`.
- Màn hình tồn kho tìm và lọc SKU trên bảng này. Module không đọc bảng của `catalog`.
- `catalog` gọi `IInventorySkuRegistry.SyncAsync` trong transaction lưu sản phẩm. Hàm tạo dòng `stock_levels` bằng 0 cho SKU mới.
- Xóa cứng sản phẩm: `catalog` hỏi `IInventoryReader.GetRemovalBlockersAsync` trước.
  - SKU từng được giữ cho đơn: API trả 409 `PRODUCT_HAS_ORDERS`, như hiện nay.
  - SKU còn số trên kệ lớn hơn 0: API trả 409 `PRODUCT_HAS_STOCK`. Đây là luật mới.
- Xóa thành công thì `inventory` đặt `removed_at` cho SKU. Sổ thay đổi giữ nguyên.

### Điều chỉnh tay

- Điều chỉnh đổi số trên kệ, không đổi số đã giữ.
- Mỗi điều chỉnh có một mã lý do và ghi chú tùy chọn tối đa 500 ký tự.

| Mã lý do | Ý nghĩa | Chiều |
|---|---|---|
| `damaged` | Hàng hỏng, không bán được | trừ |
| `lost` | Hàng mất | trừ |
| `found` | Tìm lại hàng tưởng đã mất | cộng |
| `customer_return` | Hàng khách trả, đã kiểm đạt | cộng, loại thay đổi `return_restock` |
| `correction` | Sửa sai do nhập nhầm | cộng hoặc trừ |
| `other` | Lý do khác, ghi chú bắt buộc | cộng hoặc trừ |

- Điều chỉnh làm số bán được âm thì API trả 409 `INSUFFICIENT_STOCK`. Nhân viên phải hủy đơn đang giữ trước.
- Mỗi điều chỉnh ghi một dòng audit log `inventory.adjustment.create`.

### Ngưỡng sắp hết

- `reorder_point` là số nguyên từ 0 đến 100.000, mặc định 5.
- SKU sắp hết là SKU đang bật, chưa bị xóa, có `available <= reorder_point`.
- Badge của `admin-console` đọc `IInventoryReader.CountLowStockAsync`, thay cho `ICatalogReader.CountLowStockAsync`.
- Bộ lọc `lowStock=true` của `GET /api/admin/products` dùng cùng định nghĩa, qua `IInventoryReader`.

### Nhà cung cấp

- Nhà cung cấp có: tên (bắt buộc, tối đa 200 ký tự), số điện thoại, email, mã số thuế, địa chỉ, ghi chú, `active`.
- Tên không trùng, so sánh không phân biệt hoa thường.
- Không xóa nhà cung cấp. Nhân viên tắt `active`. Phiếu nhập mới không chọn được nhà cung cấp đã tắt.

### Phiếu nhập kho

- Phiếu có: mã phiếu tự sinh (`PN-yyyyMMdd-NNN`), nhà cung cấp, số hóa đơn của nhà cung cấp, ngày nhận, ghi chú, các dòng.
- Mỗi dòng có `sku_id`, `quantity` (1 đến 100.000), `unit_cost` (số nguyên VND, từ 0). Một SKU chỉ xuất hiện một lần trong phiếu.
- Trạng thái: `draft`, `received`, `cancelled`.

| Từ | Sang | Điều kiện |
|---|---|---|
| `draft` | `received` | Có ít nhất một dòng. Nhà cung cấp đang `active`. Mọi SKU đang có trong `inventory_skus` và chưa bị xóa |
| `draft` | `cancelled` | Luôn được |

- Chỉ phiếu `draft` sửa được nội dung.
- Xác nhận phiếu cộng số trên kệ và tính lại giá vốn bình quân cho từng dòng.
- Mỗi dòng phiếu tạo một dòng sổ loại `receipt`, với `document_id` là id phiếu.
- Phiếu `received` không sửa, không hủy được. Sai số lượng thì sửa bằng điều chỉnh `correction`. Giá vốn không tính lại.
- Xác nhận hai lần cùng phiếu chỉ cộng hàng một lần. Lần sau trả 200 với phiếu đã nhận.
- Mỗi lần xác nhận hoặc hủy ghi một dòng audit log.

### Kiểm kê

- Phiếu kiểm kê chọn SKU theo một trong hai cách: mọi SKU đang bật, hoặc danh sách SKU nhân viên chọn.
- Lúc tạo phiếu, mỗi dòng lưu `on_hand_at_start`: số trên kệ tại thời điểm đó.
- Nhân viên nhập `counted_quantity` cho từng dòng. Dòng chưa nhập số đếm giữ `null`.
- Trạng thái: `counting`, `applied`, `cancelled`. Chỉ phiếu `counting` nhận số đếm.
- Một SKU chỉ nằm trong một phiếu `counting` tại một thời điểm. Tạo phiếu trùng SKU thì API trả 409 `STOCKTAKE_IN_PROGRESS`.
- Áp dụng phiếu: với mỗi dòng có số đếm, `chênh = counted_quantity - on_hand_at_start`. Module cộng phần chênh vào số trên kệ hiện tại.
- Cách tính trên giữ đúng các lần giao hàng xảy ra giữa lúc tạo phiếu và lúc áp dụng.
- Màn hình chi tiết hiện SKU có số trên kệ đổi từ lúc tạo phiếu, để nhân viên kiểm lại trước khi áp dụng.
- Phần chênh làm số trên kệ nhỏ hơn số đã giữ thì API trả 409 `INSUFFICIENT_STOCK` kèm danh sách SKU. Module không áp dụng dòng nào.
- Dòng không có số đếm và dòng có chênh bằng 0 không tạo dòng sổ.
- Mỗi lần áp dụng hoặc hủy ghi một dòng audit log.

### Giá vốn

- Module tính giá vốn theo phương pháp bình quân gia quyền di động. Mỗi lần nhập hàng, giá vốn tính lại từ hàng cũ và hàng mới.
- Công thức khi xác nhận một dòng phiếu nhập:
  - `average_cost` đang `null` hoặc `on_hand` bằng 0: `average_cost mới = unit_cost`.
  - Trường hợp khác: `average_cost mới = (on_hand × average_cost + quantity × unit_cost) / (on_hand + quantity)`.
- Kết quả làm tròn về số nguyên VND, nửa đơn vị làm tròn lên. Phép tính dùng `long` để không tràn số.
- `on_hand` trong công thức gồm cả hàng đã giữ, vì hàng đó vẫn nằm trong kho.
- Các loại thay đổi khác không đổi giá vốn.
- Hàng có từ trước module mới không có giá vốn. Nhân viên có `inventory:write` và `inventory-cost:read` khai giá vốn đầu kỳ một lần cho mỗi SKU. Khai lần hai thì API trả 409 `AVERAGE_COST_ALREADY_SET`.
- Giá trị tồn kho của SKU bằng `on_hand × average_cost`. Báo cáo tính theo thời điểm xem, không tính theo ngày trong quá khứ.
- SKU chưa có giá vốn không cộng vào tổng. Báo cáo hiện số SKU này riêng.

### Quyền

| Quyền | Dùng cho | Role mặc định |
|---|---|---|
| `inventory:read` (mới) | Xem số lượng, sổ thay đổi, đơn đang giữ, nhà cung cấp, phiếu nhập, phiếu kiểm kê | `admin`, `sales`, `support` |
| `inventory:write` (đã có) | Điều chỉnh, ngưỡng sắp hết, nhà cung cấp, phiếu nhập, kiểm kê | `admin` |
| `inventory-cost:read` (mới) | Xem giá nhập, giá vốn, giá trị tồn kho | `admin` |

- Response chỉ có trường tiền (`unitCost`, `averageCost`, `value`) khi người gọi có `inventory-cost:read`.
- `inventory:write` chuyển từ nhóm `catalog` sang nhóm `inventory` trong danh mục quyền của `identity`.
- Migration của `identity` cấp `inventory:read` cho mọi role đang có `products:read`. Nhờ vậy không role nào mất quyền xem tồn kho.

### Sự kiện

- `StockLevelChangedEvent(SkuId, OnHand, Reserved, Available, PreviousAvailable)` thay cho `StockChangedEvent` của `catalog`.
- Module phát sự kiện sau khi transaction commit, mỗi SKU một sự kiện. Transaction lỗi thì không phát.
- Người nghe hiện có: bộ xóa cache của `catalog` (`CatalogCacheInvalidationHandler`).

### Chuyển dữ liệu từ `catalog`

- Migration `OpeningBalance` của `inventory` chạy một lần. Migration đọc `skus`, `products`, `inventory_movements` và `orders`.
- Đây là ngoại lệ duy nhất của luật "module chỉ đọc bảng của mình". Ngoại lệ chỉ nằm trong migration này.
- Các bước của migration:
  1. Chép SKU của `catalog` sang `inventory_skus`.
  2. Tìm đơn đang giữ hàng mà chưa rời kho: `pending_payment`, `pending_confirmation`, `confirmed`. Tạo dòng `stock_holds` trạng thái `active`.
  3. Đặt `reserved` bằng tổng hàng giữ của SKU. Đặt `on_hand` bằng `skus.stock` cộng `reserved`.
  4. Ghi một dòng sổ `opening_balance` cho mỗi SKU có số khác 0.
  5. Tạo dòng giữ `committed` cho đơn `shipping`, không đổi số lượng.
- Sau migration, với mọi SKU: `skus.stock` cũ bằng `on_hand - reserved`.
- Bảng `inventory_movements` cũ giữ nguyên tên và dữ liệu. `catalog` thôi ghi vào bảng. Một trigger chặn sửa và xóa bảng này. Xóa bảng ở một bản phát hành sau.

## API

Mọi route back-office dưới `/api/admin/inventory/`, theo REST mức tài nguyên. Response thành công là `{ data }`. Danh sách phân trang bằng `page` (từ 1) và `limit` (1 đến 100, mặc định 20).

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/admin/inventory/stock-levels` | `inventory:read` | Mọi SKU chưa bị xóa với `onHand`, `reserved`, `available`, `reorderPoint`, `lowStock`. Lọc `search` (tên sản phẩm hoặc mã SKU, không phân biệt dấu), `lowStock=true`. Sắp theo `available` tăng dần rồi theo `code` |
| GET | `/api/admin/inventory/stock-levels/{skuId}` | `inventory:read` | Chi tiết một SKU. Lỗi: 404 `SKU_NOT_FOUND` |
| PATCH | `/api/admin/inventory/stock-levels/{skuId}` | `inventory:write` | Body `{ reorderPoint?, averageCost? }`. `averageCost` cần thêm `inventory-cost:read`. Lỗi: 400 `VALIDATION_ERROR`, 403, 404 `SKU_NOT_FOUND`, 409 `AVERAGE_COST_ALREADY_SET` |
| GET | `/api/admin/inventory/movements` | `inventory:read` | Sổ thay đổi, mới nhất trước. Lọc `skuId`, `type` |
| POST | `/api/admin/inventory/adjustments` | `inventory:write` | Body `{ skuId, quantity, reasonCode, note? }`. `quantity` khác 0, chiều theo bảng mã lý do. Trả 201 với số mới và dòng sổ. Lỗi: 400 `VALIDATION_ERROR`, 404 `SKU_NOT_FOUND`, 409 `INSUFFICIENT_STOCK` |
| GET | `/api/admin/inventory/stock-holds` | `inventory:read` | Dòng giữ hàng. Lọc `skuId`, `status` |
| GET | `/api/admin/inventory/suppliers` | `inventory:read` | Danh sách nhà cung cấp. Lọc `search`, `active` |
| POST | `/api/admin/inventory/suppliers` | `inventory:write` | Tạo nhà cung cấp. Trả 201. Lỗi: 400, 409 `SUPPLIER_NAME_TAKEN` |
| PATCH | `/api/admin/inventory/suppliers/{id}` | `inventory:write` | Sửa thông tin hoặc `active`. Lỗi: 400, 404 `SUPPLIER_NOT_FOUND`, 409 `SUPPLIER_NAME_TAKEN` |
| GET | `/api/admin/inventory/goods-receipts` | `inventory:read` | Danh sách phiếu nhập. Lọc `status`, `supplierId`, `from`, `to` |
| POST | `/api/admin/inventory/goods-receipts` | `inventory:write` | Tạo phiếu `draft`. Trả 201 |
| GET | `/api/admin/inventory/goods-receipts/{id}` | `inventory:read` | Chi tiết phiếu kèm các dòng. Lỗi: 404 `GOODS_RECEIPT_NOT_FOUND` |
| PUT | `/api/admin/inventory/goods-receipts/{id}` | `inventory:write` | Thay toàn bộ nội dung phiếu `draft`. Lỗi: 409 `GOODS_RECEIPT_NOT_EDITABLE` |
| PUT | `/api/admin/inventory/goods-receipts/{id}/status` | `inventory:write` | Body `{ status: "received" \| "cancelled" }`. Lỗi: 409 `INVALID_STATUS_TRANSITION`, 422 `GOODS_RECEIPT_NOT_RECEIVABLE` (lý do trong `details`) |
| GET | `/api/admin/inventory/stocktakes` | `inventory:read` | Danh sách phiếu kiểm kê. Lọc `status` |
| POST | `/api/admin/inventory/stocktakes` | `inventory:write` | Body `{ scope: "all_enabled" \| "selected", skuIds?, note? }`. Trả 201. Lỗi: 409 `STOCKTAKE_IN_PROGRESS` |
| GET | `/api/admin/inventory/stocktakes/{id}` | `inventory:read` | Chi tiết phiếu, mỗi dòng có `onHandAtStart`, `onHandNow`, `countedQuantity`, `difference`. Lỗi: 404 `STOCKTAKE_NOT_FOUND` |
| PATCH | `/api/admin/inventory/stocktakes/{id}/lines` | `inventory:write` | Body `[{ skuId, countedQuantity }]`, `countedQuantity` từ 0 hoặc `null`. Lỗi: 409 `STOCKTAKE_NOT_EDITABLE` |
| PUT | `/api/admin/inventory/stocktakes/{id}/status` | `inventory:write` | Body `{ status: "applied" \| "cancelled" }`. Lỗi: 409 `INVALID_STATUS_TRANSITION`, 409 `INSUFFICIENT_STOCK` |
| GET | `/api/admin/inventory/valuation` | `inventory-cost:read` | Giá trị từng SKU, tổng giá trị, số SKU chưa có giá vốn |
| GET | `/api/admin/exports/inventory-valuation?format=csv` | `inventory-cost:read` + `reports:export` | File CSV UTF-8 có BOM. Mỗi lần xuất ghi một dòng audit log |

Route cũ bị xóa sau khi frontend chuyển sang route mới:

- `POST /api/admin/skus/{id}/movements`
- `GET /api/admin/skus/{id}/movements`
- `GET /api/admin/inventory/low-stock`

## Data Model (Prisma, rút gọn)

Bản dưới viết theo cú pháp Prisma để dễ đọc. Module viết bằng .NET và EF Core. Lịch sử migration nằm trong schema `ef_inventory`.

```prisma
model Warehouse     { id String @id; code String @unique; name String; isDefault Boolean; @@map("warehouses") }

model InventorySku {
  skuId       String    @id
  productId   String
  productName String
  code        String
  optionLabel String    // 'Dung lượng: 256GB, Màu: Đen'
  enabled     Boolean
  removedAt   DateTime?
  @@map("inventory_skus")
}

model StockLevel {
  skuId        String
  warehouseId  String
  onHand       Int      // CHECK (on_hand >= 0)
  reserved     Int      // CHECK (reserved >= 0 AND reserved <= on_hand)
  reorderPoint Int      @default(5)
  averageCost  Int?     // VND, null khi chưa có giá vốn
  updatedAt    DateTime
  @@id([skuId, warehouseId])
  @@map("stock_levels")
}

model StockHold {
  id          String   @id
  orderId     String
  skuId       String
  warehouseId String
  quantity    Int      // > 0
  status      StockHoldStatus  // active | committed | released
  updatedAt   DateTime
  @@unique([orderId, skuId])
  @@map("stock_holds")
}

model StockMovement {
  id            String   @id
  skuId         String
  warehouseId   String
  type          StockMovementType
  onHandDelta   Int
  reservedDelta Int
  unitCost      Int?
  orderId       String?
  documentId    String?  // phiếu nhập hoặc phiếu kiểm kê
  actorId       String?
  reasonCode    String?
  note          String?
  createdAt     DateTime
  @@index([skuId, createdAt])
  @@map("stock_movements")   // trigger chặn UPDATE và DELETE
}

model Supplier      { /* id, name, nameKey @unique (chữ thường), phone?, email?, taxCode?, address?, note?, active, createdAt, updatedAt */ }
model GoodsReceipt  { /* id, code @unique, supplierId, supplierInvoiceNo?, receivedOn?, note?, status, createdBy, receivedBy?, receivedAt?, createdAt, updatedAt */ }
model GoodsReceiptLine { /* receiptId, skuId, quantity, unitCost; @@id([receiptId, skuId]) */ }
model Stocktake     { /* id, code @unique, scope, status, note?, createdBy, appliedBy?, appliedAt?, createdAt */ }
model StocktakeLine { /* stocktakeId, skuId, onHandAtStart, countedQuantity?; @@id([stocktakeId, skuId]) */ }
```

- Bảng của `inventory` không có khóa ngoại sang bảng của `catalog` hay `commerce`. Module kiểm SKU qua `inventory_skus`.
- Không thêm chỉ mục ngoài khóa chính, khóa duy nhất và chỉ mục cho sổ thay đổi theo SKU. Web demo chưa có vấn đề hiệu năng đo được.

## Frontend

*Back-office* (`features/inventory/admin/`). Menu có nhóm mới "Kho hàng".

| Trang | Route | Quyền hiện menu |
|---|---|---|
| Tồn kho: bảng mọi SKU, ba con số, nhãn sắp hết, ô tìm, lọc sắp hết | `/admin/inventory` | `inventory:read` |
| Chi tiết SKU: ba con số, ngưỡng, giá vốn, sổ thay đổi, đơn đang giữ, nút điều chỉnh | `/admin/inventory/skus/:skuId` | `inventory:read` |
| Phiếu nhập: danh sách, tạo, sửa nháp, xác nhận | `/admin/inventory/goods-receipts` | `inventory:read` |
| Kiểm kê: danh sách, nhập số đếm, áp dụng | `/admin/inventory/stocktakes` | `inventory:read` |
| Nhà cung cấp | `/admin/inventory/suppliers` | `inventory:read` |
| Giá trị tồn kho, nút tải CSV | `/admin/inventory/valuation` | `inventory-cost:read` |

- Nút ghi ẩn hoặc hiện theo `useCan(...)`.
- Bảng biến thể trong form sản phẩm giữ cột tồn kho chỉ đọc. Link "Tồn kho" trỏ sang `/admin/inventory/skus/:skuId`.
- Route cũ `/admin/products/:productId/skus/:skuId/inventory` chuyển hướng sang route mới.
- Hằng số `LOW_STOCK_THRESHOLD` của frontend bị xóa. Nhãn sắp hết đọc trường `lowStock` của API.
- Giao diện theo `docs/design/design-system.md`. Trang không tràn ngang ở 375 px.

Trang khách không đổi. Trang sản phẩm, giỏ hàng và checkout vẫn đọc cột chiếu của `catalog`.

## Dữ liệu mẫu

- Seed của `catalog` gọi `IInventorySeeding.SeedStockAsync` để đặt số trên kệ ban đầu. Hàm ghi dòng sổ loại `seed` và bỏ qua SKU đã có dòng sổ.
- Seed thêm 3 nhà cung cấp, 2 phiếu nhập `received` có giá nhập, 1 phiếu nhập `draft`.

## Testing

- Unit:
  - Công thức giá vốn, gồm làm tròn, `on_hand` bằng 0 và `average_cost` bằng `null`.
  - Chênh lệch kiểm kê, gồm dòng chưa đếm.
  - Luật chiều của từng mã lý do.
  - Bảng chuyển trạng thái của dòng giữ hàng, phiếu nhập, phiếu kiểm kê.
  - Định nghĩa sắp hết.
- Integration:
  - Mọi endpoint ở bảng API, kèm 401 và 403 theo quyền.
  - Response không có trường tiền khi người gọi thiếu `inventory-cost:read`.
  - `UPDATE` và `DELETE` trên `stock_movements` bị DB từ chối.
  - Mỗi dòng của bảng vòng đời đơn có một test, kiểm `on_hand`, `reserved` và trạng thái giữ hàng.
  - Cột chiếu `skus.stock` bằng `on_hand - reserved` sau mỗi lần ghi.
  - Migration `OpeningBalance` trên bộ dữ liệu dựng sẵn có đơn ở mọi trạng thái.
  - Xóa cứng sản phẩm còn hàng trả 409 `PRODUCT_HAS_STOCK`.
- Đồng thời:
  - 20 request giữ hàng cùng lúc cho SKU có số bán được bằng 5: đúng 5 thành công.
  - Xác nhận phiếu nhập chạy cùng lúc với giữ hàng và với sửa sản phẩm: không deadlock, số lượng đúng.
- Bất biến kiểm sau mọi test tích hợp: số trong `stock_levels` bằng tổng sổ thay đổi, và cột chiếu khớp số bán được.
- E2E:
  - Admin tạo nhà cung cấp, tạo phiếu nhập, xác nhận. Khách đặt đơn. Nhân viên tạo vận đơn. Kiểm kê thấy số trên kệ đúng.
  - `sales` xem được trang tồn kho, không thấy giá vốn, không thấy nút điều chỉnh.

## Boundaries (riêng module)

- **Always:**
  - Ghi dòng sổ cho mọi lần đổi số lượng.
  - Khóa theo thứ tự `products`, `skus`, `stock_levels`.
  - Chiếu số bán được sang `catalog` trong cùng transaction.
  - Ghi audit log cho mọi thao tác của nhân viên.
  - Ẩn trường tiền với người thiếu `inventory-cost:read`.
- **Ask first:**
  - Thêm kho thứ hai.
  - Đổi phương pháp giá vốn.
  - Cho hủy phiếu nhập đã nhận.
  - Thêm serial hoặc IMEI.
  - Giữ hàng có hạn trong `inventory`.
- **Never:**
  - Sửa hay xóa dòng sổ thay đổi.
  - Cho số trên kệ, số đã giữ hoặc số bán được âm.
  - Ghi vào bảng của `catalog` hay `commerce`. Migration `OpeningBalance` chỉ đọc các bảng này.
  - Dùng số thực cho tiền.

## Success Criteria

- [ ] Admin nhập 10 chiếc bằng phiếu nhập: số trên kệ và số bán được tăng 10, giá vốn bình quân đúng công thức.
- [ ] Khách đặt 1 chiếc: số đã giữ tăng 1, số trên kệ không đổi. Đơn sang `shipping`: số trên kệ và số đã giữ cùng giảm 1.
- [ ] Mọi dòng của bảng vòng đời đơn cho đúng `on_hand`, `reserved` và trạng thái giữ hàng.
- [ ] 20 request giữ hàng đồng thời không làm bán vượt số bán được.
- [ ] Phiếu kiểm kê áp dụng đúng phần chênh, kể cả khi có đơn giao xen giữa lúc tạo phiếu và lúc áp dụng.
- [ ] SKU có ngưỡng riêng hiện nhãn sắp hết đúng ngưỡng đó. Bộ lọc sắp hết của trang sản phẩm admin cho cùng kết quả.
- [ ] Báo cáo giá trị tồn kho bằng tổng `on_hand × average_cost` của dữ liệu dựng sẵn. File CSV mở trong Excel hiện đúng tiếng Việt.
- [ ] `sales` gọi API ghi nhận 403. Response cho `sales` không có trường tiền.
- [ ] Không câu lệnh nào sửa hay xóa được dòng trong `stock_movements`.
- [ ] Sau migration `OpeningBalance`, `skus.stock` cũ bằng `on_hand - reserved` của mọi SKU.
- [ ] Trang khách, giỏ hàng và checkout hoạt động như trước. Bộ E2E của `catalog` và `commerce` vẫn pass.
- [ ] Coverage dòng của `Pickory.Modules.Inventory` ít nhất 80%, không tính `Persistence/Migrations/`.

## Open Questions

> Ngày 2026-10-22 người dùng duyệt phương án mặc định của cả 7 câu dưới đây. Spec ở các mục trên đã viết theo các phương án này.

1. **Quyền mới (Ask first).** Spec thêm `inventory:read` và `inventory-cost:read`, cấp mặc định như bảng Quyền. Đã duyệt.
2. **Thứ tự build.** `inventory` làm ngay sau `activity-tracking`, trước `ai-gateway-removal`. Lý do: `admin-console` đọc badge sắp hết từ module này. Đã duyệt.
3. **File Excel.** `admin-console` chưa chốt thư viện `.xlsx` (`ClosedXML`). Module chỉ xuất CSV. Excel thêm sau khi thư viện được duyệt.
4. **Hủy phiếu nhập đã nhận.** Không cho hủy. Sai số lượng thì điều chỉnh. Giá vốn không tính lại.
5. **Duyệt kiểm kê.** Người có `inventory:write` tự áp dụng phiếu, không cần người thứ hai duyệt.
6. **Giá vốn hàng cũ.** Nhân viên khai giá vốn đầu kỳ một lần cho mỗi SKU. SKU chưa khai thì phiếu nhập đầu tiên đặt giá vốn bằng giá nhập.
7. **Xóa sản phẩm còn hàng.** API chặn bằng 409 `PRODUCT_HAS_STOCK`. Đây là luật mới cho API của `catalog`.
