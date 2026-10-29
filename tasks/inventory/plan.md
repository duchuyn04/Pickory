# Implementation Plan: inventory

> Trạng thái: không triển khai ngày 2026-10-27. Tồn kho ở lại `catalog`; số lượng sửa trong form sản phẩm.

## Tóm tắt dễ hiểu

Plan này tách phần tồn kho ra khỏi `catalog` thành module riêng `inventory`, rồi thêm bốn tính năng mới:

1. Phiếu nhập kho và danh sách nhà cung cấp.
2. Kiểm kê.
3. Ngưỡng sắp hết riêng cho từng SKU.
4. Giá vốn bình quân và báo cáo giá trị tồn kho.

Mỗi SKU có ba con số thay cho một: số trên kệ (`on_hand`), số đã giữ cho đơn (`reserved`), số bán được (`available`). Hàng rời kho khi đơn chuyển sang `shipping`. Người dùng chốt mốc này ngày 2026-10-22.

Plan chia việc thành 8 Phase:

1. Nền tảng: logic thuần, khung module, bảng mới.
2. Lõi ghi số lượng: khóa, kiểm, ghi sổ, chiếu sang `catalog`.
3. Cắt chuyển: `commerce` gọi module mới, chuyển dữ liệu cũ, gỡ code cũ khỏi `catalog`.
4. Quyền mới và màn hình tồn kho.
5. Nhà cung cấp và phiếu nhập.
6. Kiểm kê.
7. Giá trị tồn kho.
8. E2E và nghiệm thu.

Phase 1 đến Phase 3 không đổi hành vi khách thấy. Bộ test cũ của `catalog` và `commerce` phải xanh sau mỗi task.

## Overview

`inventory` phụ thuộc `identity` và `catalog`. `catalog` và `commerce` gọi lại `inventory` qua project Contracts.

- Spec: `spec/SPEC-inventory.md` (chờ duyệt).
- Nghiên cứu nền: `docs/research/inventory-module.md`.
- Danh sách task: `tasks/inventory/todo.md`.
- Cấu trúc .NET theo `tasks/dotnet-migration/plan.md`.

Người dùng chọn ngày 2026-10-22:

- Làm cả bốn tính năng: phiếu nhập, kiểm kê, ngưỡng theo SKU, giá vốn.
- Tách hẳn code tồn kho ra module `inventory`, thay cho mở rộng trong `catalog`.

Tài liệu nghiên cứu xếp kiểm kê và giá vốn vào phần "để sau". Plan này đưa hai việc đó vào phạm vi theo lựa chọn trên. Phần còn lại của "để sau" giữ nguyên: nhiều kho, chuyển kho, serial, IMEI, giữ hàng có hạn, gợi ý đặt hàng lại, thông báo sắp hết.

## Điều kiện trước

1. Spec `spec/SPEC-inventory.md` được người dùng duyệt, gồm 7 câu hỏi mở. Đã duyệt ngày 2026-10-22.
2. Nhánh `feat/activity-tracking` đã merge vào `main`. Plan làm trên nhánh mới `feat/inventory` tách từ `main`.
3. `commerce` đã có đủ bốn nơi gọi `IStockService` và ba nơi đổi trạng thái `shipping` (đã có trong code ngày 2026-10-22):
   - `PlaceOrderService.cs`, `OrderCancellationService.cs`, `ExpiredPaymentProcessor.cs`, `SepayLatePaymentMatcher.cs`.
   - `AdminOrderShipmentService.cs` (sang `shipping` và về `confirmed`), `ShipmentStatusChangedHandler.cs` (GHN hủy vận đơn).

## Đồ thị phụ thuộc

```
INV-00 sửa spec liên quan sau khi duyệt
INV-01 logic thuần: giá vốn, chênh kiểm kê, mã lý do, trạng thái
INV-02 khung module, Contracts, DbContext, test kiến trúc
 └── INV-03 bảng mới, kho mặc định, trigger chặn sửa sổ
      ├── INV-04 hàm khóa và hàm chiếu của catalog
      │    └── INV-05 lõi ghi số lượng + bất biến mới
      │         └── INV-06 giữ, trả, xuất kho, nhập lại cho đơn
      └── INV-07 đồng bộ SKU từ catalog, kiểm khi xóa sản phẩm
           └── INV-08 migration số dư đầu kỳ (cần INV-06)
                └── INV-09 cắt chuyển: commerce, seed, helper test, cache
                     └── INV-10 gỡ code tồn kho khỏi catalog
                          ├── INV-11 quyền mới
                          │    └── INV-12 API tồn kho, sổ, giữ hàng, ngưỡng
                          │         ├── INV-13 API điều chỉnh
                          │         │    └── INV-UI1 trang tồn kho ── INV-UI2 trang chi tiết SKU
                          │         │                                  └── INV-14 xóa route cũ
                          │         ├── INV-15 API nhà cung cấp ── INV-UI3
                          │         │    └── INV-16 API phiếu nhập + giá vốn ── INV-UI4
                          │         ├── INV-17 API kiểm kê ── INV-UI5
                          │         └── INV-18 API giá trị tồn kho + CSV ── INV-UI6 (cần INV-16)
                          └── INV-E2E1, INV-E2E2 → INV-19 nghiệm thu
```

- INV-01 và INV-02 chạy song song được.
- INV-04 và INV-07 chỉ cần INV-03, nên chạy song song được.
- Sau INV-12, bốn nhánh API (điều chỉnh, phiếu nhập, kiểm kê, giá trị) chạy song song được. INV-18 cần giá vốn của INV-16.

## Architecture Decisions

1. **Hai project mới.** `Pickory.Modules.Inventory` và `Pickory.Modules.Inventory.Contracts`. `InventoryModule.cs` có `AddInventoryModule`, `MigrateInventoryDatabaseAsync`, `MapInventoryEndpoints`. `Pickory.ArchitectureTests` thêm module vào `KnownModules`.
2. **Thứ tự migrate.** `inventory` migrate sau `commerce`, vì migration `OpeningBalance` đọc bảng `orders`. Cập nhật thứ tự ở ba nơi:
   - `DatabaseMigrationCommand.CreateSteps`.
   - `E2eStartup` (migrate và xóa schema `ef_inventory`).
   - `IntegrationDatabaseFixture`.
3. **Bảng ở schema `public`, lịch sử migration ở `ef_inventory`.** Cách này giống các module hiện có.
4. **Ba con số, `available` không lưu.** `stock_levels` có `on_hand` và `reserved`. Ràng buộc `reserved <= on_hand` thay cho `skus_stock_non_negative`.
5. **Sổ không sửa xóa được.** Trigger `BEFORE UPDATE OR DELETE` trên `stock_movements` gọi `RAISE EXCEPTION`. Trigger cùng loại đặt lên bảng `inventory_movements` cũ.
6. **Giữ hàng theo dòng.** `stock_holds` có khóa duy nhất (`order_id`, `sku_id`). Bảng này thay cho phép tính "tổng delta theo đơn" của `StockService`. Khóa `pg_advisory_xact_lock` theo `orderId` giữ nguyên.
7. **Thứ tự khóa `products`, `skus`, `stock_levels`.** Lõi ghi gọi `ISkuStockProjection.LockAsync` của `catalog` trước, rồi mới khóa `stock_levels`. `ProductStockConcurrencyTests` mở rộng để kiểm thứ tự này.
8. **Chiếu trong cùng transaction.** `ISkuStockProjection.ApplyAsync` ghi `skus.stock`, rồi tính lại `products.inStock` và `products.minPrice` bằng `ProductPricing` hiện có. Module không chiếu bằng sự kiện, vì `InProcessEventDispatcher` nuốt lỗi của người nghe.
9. **Giữ `skus.stock` làm cột chiếu.** Giỏ hàng, bộ lọc `inStock`, trang sản phẩm và `SkuSnapshot.Stock` không đổi. Phần lớn test đọc `Sku.Stock` không phải sửa.
10. **`catalog` báo SKU cho `inventory`.** `ProductManagementHandler` gọi `IInventorySkuRegistry.SyncAsync` trong transaction lưu sản phẩm. Hàm ghi `inventory_skus` và tạo `stock_levels` bằng 0 cho SKU mới. Màn hình tồn kho tìm và lọc trên `inventory_skus`, không đọc bảng của `catalog`.
11. **Xóa sản phẩm hỏi `inventory`.** `IInventoryReader.GetRemovalBlockersAsync` trả SKU từng có đơn và SKU còn hàng. Code xóa movement `manual` và `return` trong `ProductManagementHandler.DeleteAsync` bị bỏ, vì sổ không xóa được.
12. **Thay `IStockService`, không giữ lớp vỏ.** Bốn record `StockItem`, `StockShortage`, `ReserveResult`, `StockCallerTransaction` chuyển namespace, giữ tên. `commerce` đổi `using` ở bốn file và thêm tham chiếu `Pickory.Modules.Inventory.Contracts`.
13. **Xuất kho gắn với chuyển trạng thái đơn.** `CommitAsync` và `UncommitAsync` chạy trong transaction đổi trạng thái của `commerce`, qua `StockCallerTransaction`. Hai hàm không bao giờ thiếu hàng, vì `reserved` đã đủ.
14. **Số dư đầu kỳ bằng migration EF.** Migration `OpeningBalance` dùng SQL đọc `skus`, `products`, `inventory_movements`, `orders`. Đây là ngoại lệ duy nhất của luật ranh giới, spec ghi rõ. DB mới không có dữ liệu, nên migration chạy rỗng.
15. **Seed qua Contracts.** `CatalogSeeder` gọi `IInventorySeeding.SeedStockAsync` thay cho `StockLedger.ApplyAsync`. Hàm bỏ qua SKU đã có dòng sổ, nên seed chạy lại không cộng thêm.
16. **Sự kiện mới.** `StockLevelChangedEvent` thay `StockChangedEvent`. `CatalogCacheInvalidationHandler` đổi kiểu sự kiện nó nghe. Test host đổi `RecordingDomainEventHandler<StockChangedEvent>` theo.
17. **Quyền.** Thêm `inventory:read` và `inventory-cost:read` vào `Permissions.cs` và `PermissionCatalog.cs`. `inventory:write` chuyển sang nhóm `inventory`. Lớp `InventoryPermissions.All` khớp nhóm này, kiểm bằng test như `CatalogPermissionsTests`. Migration của `identity` cấp `inventory:read` cho role đang có `products:read`.
18. **Ẩn trường tiền theo quyền.** Handler đọc quyền của `Actor`. Thiếu `inventory-cost:read` thì DTO để `null` cho trường tiền, và serializer bỏ trường `null` khỏi JSON.
19. **Giá vốn là hàm thuần.** `AverageCostCalculator.Next(onHand, averageCost, quantity, unitCost)` dùng `long`, làm tròn nửa lên. Unit test phủ mọi nhánh của spec.
20. **Kiểm kê theo phần chênh.** Áp dụng phiếu cộng `counted - onHandAtStart` vào số trên kệ hiện tại. Cách này theo Odoo, xem tài liệu nghiên cứu.
21. **Route REST mức tài nguyên.** Đổi trạng thái phiếu dùng `PUT .../{id}/status`, giống `PUT /api/admin/products/{id}/status`. File xuất dùng `GET /api/admin/exports/inventory-valuation`, theo quyết định 16 của `tasks/admin-console/plan.md`.
22. **Giữ route cũ tới INV-14.** Ba route cũ chạy song song route mới cho tới khi frontend chuyển xong. INV-14 xóa route cũ và cập nhật `RouteNamingTests`, `PermissionMatrixTests`.
23. **Frontend chuyển thư mục.** Trang tồn kho chuyển từ `features/catalog/admin/` sang `features/inventory/admin/`. Menu có nhóm "Kho hàng". Hằng số `LOW_STOCK_THRESHOLD` bị xóa.
24. **CSV trước, Excel sau.** Module chỉ xuất CSV cho tới khi người dùng duyệt thư viện `.xlsx` (câu hỏi mở 3 của spec).

## Task List

Chi tiết từng task ở `tasks/inventory/todo.md`.

### Phase 0: Chốt spec
- [x] INV-00

### Phase 1: Nền tảng
- [ ] INV-01 · [ ] INV-02 · [ ] INV-03

### Checkpoint I1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `Inventory` tham chiếu module khác ngoài Contracts
- [ ] `UPDATE` và `DELETE` trên `stock_movements` bị DB từ chối
- [ ] Review với người dùng

### Phase 2: Lõi ghi số lượng
- [ ] INV-04 · [ ] INV-05 · [ ] INV-06 · [ ] INV-07

### Checkpoint I2
- [ ] 20 request giữ hàng đồng thời: đúng 5 thành công trên SKU có số bán được bằng 5
- [ ] Mỗi dòng của bảng vòng đời đơn có test riêng và pass
- [ ] Bộ test cũ vẫn xanh, vì `commerce` chưa đổi
- [ ] Review với người dùng

### Phase 3: Cắt chuyển
- [ ] INV-08 · [ ] INV-09 · [ ] INV-10

### Checkpoint I3
- [ ] Toàn bộ `dotnet test` và bộ E2E của `catalog`, `commerce` xanh
- [ ] Bất biến mới đúng sau mọi test tích hợp
- [ ] `catalog` không còn ghi tồn kho, không còn `IStockService`
- [ ] Review với người dùng

### Phase 4: Quyền và màn hình tồn kho
- [ ] INV-11 · [ ] INV-12 · [ ] INV-13 · [ ] INV-UI1 · [ ] INV-UI2 · [ ] INV-14

### Checkpoint I4
- [ ] Ma trận quyền có mọi route mới, route cũ đã xóa
- [ ] `sales` thấy trang tồn kho, không thấy giá vốn và nút điều chỉnh
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Review với người dùng

### Phase 5: Nhà cung cấp và phiếu nhập
- [ ] INV-15 · [ ] INV-UI3 · [ ] INV-16 · [ ] INV-UI4

### Checkpoint I5
- [ ] Xác nhận phiếu nhập cộng hàng đúng một lần và tính đúng giá vốn
- [ ] Review với người dùng

### Phase 6: Kiểm kê
- [ ] INV-17 · [ ] INV-UI5

### Checkpoint I6
- [ ] Áp dụng phiếu đúng phần chênh khi có đơn giao xen giữa
- [ ] Review với người dùng

### Phase 7: Giá trị tồn kho
- [ ] INV-18 · [ ] INV-UI6

### Checkpoint I7
- [ ] Tổng giá trị khớp bộ dữ liệu dựng sẵn, CSV mở đúng tiếng Việt trong Excel
- [ ] Review với người dùng

### Phase 8: E2E và nghiệm thu
- [ ] INV-E2E1 · [ ] INV-E2E2 · [ ] INV-19

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.Inventory` ít nhất 80%, không tính `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Nhập 10 chiếc: số trên kệ và số bán được tăng 10, giá vốn đúng | INV-01, INV-16, INV-E2E1 | Unit test công thức; test tích hợp xác nhận phiếu; E2E |
| Đặt 1 chiếc tăng số đã giữ; sang `shipping` giảm số trên kệ và số đã giữ | INV-06, INV-09, INV-E2E1 | Test tích hợp qua API của `commerce`; E2E |
| Mọi dòng của bảng vòng đời đơn đúng | INV-06, INV-09 | Một test tích hợp cho mỗi dòng |
| 20 request giữ hàng đồng thời không bán vượt | INV-05, INV-06 | Test đồng thời |
| Kiểm kê đúng phần chênh khi có đơn giao xen giữa | INV-01, INV-17 | Unit test; test tích hợp có lần xuất kho giữa lúc tạo và lúc áp dụng |
| Ngưỡng riêng hiện nhãn sắp hết đúng; bộ lọc trang sản phẩm admin cùng kết quả | INV-10, INV-12, INV-UI1 | Test tích hợp so hai route; Vitest nhãn |
| Giá trị tồn kho đúng; CSV đúng tiếng Việt | INV-18, INV-UI6 | Test tích hợp với dữ liệu dựng sẵn; test BOM; người dùng mở thử bằng Excel |
| `sales` nhận 403 khi ghi, không thấy trường tiền | INV-11, INV-12, INV-E2E2 | Ma trận quyền; test tích hợp đọc JSON; E2E |
| Không sửa xóa được dòng sổ | INV-03 | Test tích hợp chạy `UPDATE`, `DELETE` |
| Sau `OpeningBalance`, `skus.stock` cũ bằng `on_hand - reserved` | INV-08 | Test tích hợp trên bộ dữ liệu có đơn ở mọi trạng thái |
| Trang khách, giỏ, checkout như trước | INV-09, INV-10, INV-19 | Bộ E2E của `catalog` và `commerce` |
| Coverage ít nhất 80% | INV-19 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Deadlock giữa giữ hàng, nhập hàng và sửa sản phẩm | API trả 500 khi nhiều người thao tác | Một thứ tự khóa (quyết định 7). Mở rộng `ProductStockConcurrencyTests` cho phiếu nhập và kiểm kê |
| Cột chiếu `skus.stock` lệch số bán được | Khách mua được hàng đã hết, hoặc không mua được hàng còn | Chiếu trong cùng transaction (quyết định 8). Bất biến so hai nguồn sau mọi test |
| Sai số dư khi chuyển dữ liệu | Số trên kệ sai ngay từ đầu | Test trên dữ liệu dựng sẵn có đơn ở mọi trạng thái. Migration kiểm `skus.stock = on_hand - reserved` và dừng nếu sai |
| Nhiều file test phải sửa cùng lúc ở INV-09 | Task cắt chuyển lớn, khó review | Đổi helper chung (`StockTestData`, `ReadingTestData`, `StockInvariant`) trước, rồi đổi namespace hàng loạt |
| Hai module tham chiếu Contracts của nhau | Dễ tạo vòng khi thêm kiểu mới | Test kiến trúc chặn Contracts tham chiếu Contracts khác |
| Role mất quyền xem tồn kho khi đổi quyền | Nhân viên không mở được trang | Migration cấp `inventory:read` cho role đang có `products:read` (INV-11) |
| Phạm vi trượt sang nhiều kho, serial, IMEI | Plan kéo dài | Giữ danh sách "để sau" trong Overview. Thêm phần mới cần người dùng duyệt |

## Câu hỏi mở

Ngày 2026-10-22 người dùng duyệt phương án mặc định của cả bảy câu hỏi mở trong spec:

1. Thêm `inventory:read` và `inventory-cost:read`.
2. Làm `inventory` ngay sau `activity-tracking`, trước `ai-gateway-removal`.
3. Chỉ xuất CSV.
4. Không hủy phiếu nhập đã nhận.
5. Không cần người thứ hai duyệt kiểm kê.
6. Khai giá vốn đầu kỳ một lần cho mỗi SKU.
7. Chặn xóa sản phẩm còn hàng.

Đổi phương án nào thì sửa spec, todo và bảng Traceability trước khi bắt đầu task liên quan.
