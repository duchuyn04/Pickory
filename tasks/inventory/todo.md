# Task List: inventory

> Trạng thái: không triển khai ngày 2026-10-27. Tồn kho ở lại `catalog`; số lượng sửa trong form sản phẩm.

Plan và quyết định thiết kế: `tasks/inventory/plan.md`. Spec: `spec/SPEC-inventory.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Mọi test tích hợp chạm số lượng kiểm bất biến mới ở bước dọn dữ liệu.
- Kích thước: XS 1 file, S 1-2, M 3-5, L 6-10. Số file không tính file test. Task L ghi lý do không tách được.

Viết tắt trong file này:
- `INV`: `Pickory.Modules.Inventory`.
- `INVC`: `Pickory.Modules.Inventory.Contracts`.
- `CAT`: `Pickory.Modules.Catalog`.
- `CATC`: `Pickory.Modules.Catalog.Contracts`.

---

## Phase 0: Chốt spec

### INV-00: Sửa spec liên quan sau khi duyệt
**Mô tả:** Người dùng trả lời 7 câu hỏi mở của `spec/SPEC-inventory.md`. Task sửa các tài liệu đang mô tả tồn kho trong `catalog`.
**Tiêu chí nghiệm thu:**
- [x] `spec/SPEC-inventory.md` ghi câu trả lời của người dùng và đổi trạng thái sang "đã duyệt"
- [x] `spec/CAPABILITY-MAP.md`: thêm dòng `inventory` vào bảng Modules và thứ tự build. Bỏ "tồn kho" khỏi trách nhiệm của `catalog`. Đổi phụ thuộc của `commerce`
- [x] `spec/SPEC-catalog.md`: mục "Tồn kho" trỏ sang spec mới. Sửa dòng lệch về giữ hàng lại sau `release`
- [x] `spec/SPEC-commerce.md`: đổi `stock.reserve`, `stock.release` sang `IInventoryService`. Thêm `CommitAsync`, `UncommitAsync`
- [x] `spec/SPEC-identity.md`: thêm hai quyền mới, chuyển `inventory:write` sang nhóm `inventory`
- [x] `spec/SPEC-admin-console.md` và `tasks/admin-console/plan.md`: badge sắp hết đọc từ `IInventoryReader.CountLowStockAsync`
- [x] `tasks/README.md`: dòng `inventory/` cập nhật trạng thái
**Kiểm tra:** người dùng đọc lại các đoạn đã sửa
**Phụ thuộc:** không · **Quy mô:** M
**File:** các file spec và plan nêu trên

---

## Phase 1: Nền tảng

### INV-01: Logic thuần
**Mô tả:** Các hàm không gọi DB.
- `AverageCostCalculator.Next(onHand, averageCost, quantity, unitCost)`.
- `StocktakeDifference.For(onHandAtStart, countedQuantity)`.
- `AdjustmentReasons`: mã lý do, chiều cho phép, loại thay đổi tương ứng.
- `StockHoldTransitions`, `GoodsReceiptTransitions`, `StocktakeTransitions`.
- `LowStockRule.IsLow(enabled, removed, available, reorderPoint)`.
**Tiêu chí nghiệm thu:**
- [ ] Giá vốn: `averageCost` bằng `null` hoặc `onHand` bằng 0 trả `unitCost`
- [ ] Giá vốn: ví dụ 10 cái giá 100 và 10 cái giá 120 trả 110. Làm tròn nửa lên. Số lớn không tràn `long`
- [ ] Chênh kiểm kê: số đếm `null` trả "không đổi"
- [ ] Mã `damaged` và `lost` chỉ nhận số âm. `found` và `customer_return` chỉ nhận số dương. `customer_return` ra loại `return_restock`
- [ ] `other` không có ghi chú bị từ chối
- [ ] Bảng chuyển trạng thái đúng với spec. Cặp ngoài bảng bị từ chối
- [ ] SKU tắt hoặc đã xóa không bao giờ "sắp hết"
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.Inventory/{Costing/, Stocktakes/, Adjustments/, Holds/, GoodsReceipts/, StockLevels/}`, test trong `apps/backend/tests/Pickory.UnitTests/Modules/Inventory/`

### INV-02: Khung module và Contracts
**Mô tả:** Tạo `INV` và `INVC`, thêm vào `Pickory.sln` và `Pickory.Api.csproj`. Thêm `InventoryDbContext` với lịch sử migration ở `ef_inventory`, factory thiết kế, `InventoryModule.cs`. Khai báo interface và record trong `INVC`, chưa có code chạy.
**Tiêu chí nghiệm thu:**
- [ ] `INVC` có `IInventoryService`, `IInventoryReader`, `IInventorySkuRegistry`, `IInventorySeeding`, `StockLevelChangedEvent`
- [ ] `INVC` có `StockItem`, `StockShortage`, `ReserveResult`, `StockCallerTransaction` với tên và trường như bản trong `CATC`. Bản trong `CATC` chưa xóa
- [ ] `INVC` chỉ tham chiếu `Pickory.Shared`
- [ ] `Program.cs` chỉ gọi `AddInventoryModule` và `MapInventoryEndpoints`
- [ ] Thứ tự migrate: `inventory` sau `commerce` ở `DatabaseMigrationCommand`, `E2eStartup`, `IntegrationDatabaseFixture`
- [ ] `ModuleBoundaryTests` có `Inventory` trong `KnownModules` và chặn tham chiếu ngoài Contracts
**Kiểm tra:** build, test kiến trúc
**Phụ thuộc:** không · **Quy mô:** L (hai project mới cần csproj, module, DbContext, factory, Contracts, đăng ký host)

### INV-03: Bảng mới và trigger
**Mô tả:** Migration đầu của `inventory`. Tạo `warehouses` với một kho mặc định, `inventory_skus`, `stock_levels`, `stock_holds`, `stock_movements`, `suppliers`, `goods_receipts`, `goods_receipt_lines`, `stocktakes`, `stocktake_lines`. Tạo trigger chặn sửa và xóa `stock_movements`.
**Tiêu chí nghiệm thu:**
- [ ] Ràng buộc `on_hand >= 0`, `reserved >= 0`, `reserved <= on_hand`. Mọi cột số lượng `NOT NULL`
- [ ] `stock_holds` có khóa duy nhất (`order_id`, `sku_id`) và `quantity > 0`
- [ ] `UPDATE` và `DELETE` trên `stock_movements` ném lỗi DB. Test tích hợp kiểm cả hai
- [ ] Hai enum Postgres `StockMovementType`, `StockHoldStatus` khai báo qua `MapEnum` và `HasPostgresEnum`
- [ ] Migration chạy trên DB sạch. `e2e:start` và fixture test dựng được schema mới
- [ ] `ApplicationDatabaseSchema` và `BaselineMigrationIntegrationTests` có bảng và schema lịch sử mới
**Kiểm tra:** test tích hợp schema và ràng buộc
**Phụ thuộc:** INV-02 · **Quy mô:** M

### Checkpoint I1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `Inventory` tham chiếu module khác ngoài Contracts
- [ ] `UPDATE` và `DELETE` trên `stock_movements` bị DB từ chối
- [ ] Review với người dùng

---

## Phase 2: Lõi ghi số lượng

### INV-04: Hàm khóa và hàm chiếu của catalog
**Mô tả:** Thêm `ISkuStockProjection` vào `CATC` với `LockAsync(skuIds, transaction)` và `ApplyAsync(changes, transaction)`. `CAT` cài interface này bằng code tách từ `StockLedger`.
**Tiêu chí nghiệm thu:**
- [ ] `LockAsync` khóa `products` rồi `skus` theo thứ tự `id`, trên transaction của bên gọi
- [ ] `ApplyAsync` ghi `skus.stock` bằng số bán được, rồi tính lại `inStock`, `minPrice` bằng `ProductPricing`
- [ ] Hai hàm không mở transaction riêng và không gọi `SaveChanges` của bên gọi
- [ ] `StockLedger` cũ vẫn chạy như trước. Bộ test cũ xanh
**Kiểm tra:** test tích hợp gọi thẳng hai hàm
**Phụ thuộc:** INV-03 · **Quy mô:** S

### INV-05: Lõi ghi số lượng
**Mô tả:** `StockWriter` trong `INV` chạy 6 bước của spec (mục "Chống bán vượt và thứ tự khóa"). Thêm `InventoryTransaction.RunAsync` theo mẫu `CatalogTransaction`. Thêm bất biến `InventoryInvariant` cho test.
**Tiêu chí nghiệm thu:**
- [ ] Thiếu hàng ở 1 SKU thì không ghi SKU nào, kể cả sổ và cột chiếu
- [ ] Câu cập nhật có điều kiện chặn số âm ngay cả khi bước kiểm bị bỏ (thử đột biến)
- [ ] Mỗi lần ghi tạo đúng 1 dòng sổ cho mỗi SKU, có `unit_cost` theo spec
- [ ] Cột chiếu `skus.stock` bằng `on_hand - reserved` sau mỗi lần ghi
- [ ] `StockLevelChangedEvent` phát sau commit, mỗi SKU một sự kiện. Rollback thì không phát
- [ ] `InventoryInvariant` kiểm: số trong `stock_levels` bằng tổng sổ, và cột chiếu khớp
**Kiểm tra:** test tích hợp, test đồng thời
**Phụ thuộc:** INV-04 · **Quy mô:** M

### INV-06: Giữ hàng, trả hàng, xuất kho cho đơn
**Mô tả:** `InventoryService` cài `IInventoryService` trên `StockWriter` và bảng `stock_holds`. Chưa nối vào `commerce`.
**Tiêu chí nghiệm thu:**
- [ ] 20 request `ReserveAsync` cùng lúc cho SKU có số bán được bằng 5: đúng 5 thành công, `reserved` bằng 5
- [ ] Gọi lại mỗi hàm với cùng `orderId` không đổi số lượng lần thứ hai
- [ ] `ReserveAsync` sau `ReleaseAsync` cùng đơn: dòng `released` chuyển `active` nếu đủ hàng, trả thiếu hàng nếu không đủ
- [ ] Mỗi dòng của bảng vòng đời đơn có một test kiểm `on_hand`, `reserved`, trạng thái giữ và loại thay đổi
- [ ] `ReleaseAsync` trên dòng `committed` ghi `ship_cancel` và cộng lại số trên kệ
- [ ] Chạy với `caller` có giá trị: bên gọi rollback thì không có dòng nào và không có sự kiện
**Kiểm tra:** test tích hợp, test đồng thời
**Phụ thuộc:** INV-05 · **Quy mô:** M

### INV-07: Đồng bộ SKU và kiểm khi xóa sản phẩm
**Mô tả:** `INV` cài `IInventorySkuRegistry.SyncAsync` và `IInventoryReader.GetRemovalBlockersAsync`. `ProductManagementHandler` gọi hai hàm này khi lưu và khi xóa sản phẩm.
**Tiêu chí nghiệm thu:**
- [ ] Tạo sản phẩm có 3 biến thể: `inventory_skus` có 3 dòng, `stock_levels` có 3 dòng bằng 0, trong cùng transaction
- [ ] Đổi tên sản phẩm, mã SKU hoặc bật tắt SKU: `inventory_skus` cập nhật theo
- [ ] Lưu sản phẩm lỗi thì `inventory_skus` không đổi
- [ ] Xóa sản phẩm có SKU từng giữ hàng: 409 `PRODUCT_HAS_ORDERS`. SKU còn hàng: 409 `PRODUCT_HAS_STOCK`
- [ ] Xóa thành công đặt `removed_at`. Sổ giữ nguyên
- [ ] Thứ tự khóa khi lưu sản phẩm vẫn là `products`, `skus`, rồi bảng của `inventory`
**Kiểm tra:** test tích hợp
**Phụ thuộc:** INV-03 · **Quy mô:** M

### Checkpoint I2
- [ ] 20 request giữ hàng đồng thời: đúng 5 thành công trên SKU có số bán được bằng 5
- [ ] Mỗi dòng của bảng vòng đời đơn có test riêng và pass
- [ ] Bộ test cũ vẫn xanh, vì `commerce` chưa đổi
- [ ] Review với người dùng

---

## Phase 3: Cắt chuyển

### INV-08: Migration số dư đầu kỳ
**Mô tả:** Migration `OpeningBalance` của `inventory` theo 5 bước của spec. Thêm trigger chặn sửa và xóa `inventory_movements` cũ.
**Tiêu chí nghiệm thu:**
- [ ] Bộ dữ liệu dựng sẵn có đơn ở mọi trạng thái. Sau migration, `skus.stock` cũ bằng `on_hand - reserved` của mọi SKU
- [ ] Đơn `pending_payment`, `pending_confirmation`, `confirmed` có dòng giữ `active` đúng số lượng
- [ ] Đơn `shipping` có dòng giữ `committed`. Số lượng không đổi
- [ ] Đơn `delivered`, `returned`, `cancelled` không có dòng giữ
- [ ] Migration dừng với lỗi rõ ràng nếu phép so ở tiêu chí đầu sai
- [ ] DB sạch chạy migration không lỗi
- [ ] `UPDATE` và `DELETE` trên `inventory_movements` bị DB từ chối
**Kiểm tra:** test tích hợp chạy migration trên DB dựng riêng
**Phụ thuộc:** INV-06, INV-07 · **Quy mô:** S

### INV-09: Cắt chuyển
**Mô tả:** Đổi mọi nơi dùng tồn kho cũ sang `inventory` trong một task, để không có lúc hai nơi cùng ghi.
- `commerce` gọi `IInventoryService` ở 4 nơi đang gọi `IStockService`.
- `commerce` gọi `CommitAsync` khi đơn sang `shipping`, `UncommitAsync` khi đơn về `confirmed` (`AdminOrderShipmentService`, `ShipmentStatusChangedHandler`).
- `CatalogSeeder` gọi `IInventorySeeding.SeedStockAsync`.
- `CatalogCacheInvalidationHandler` nghe `StockLevelChangedEvent`.
- Helper test (`StockTestData`, `ReadingTestData`, `CartTestKit`, `PaymentExpiryTestKit`, `CheckoutWebApplicationFactory`) tạo số lượng qua `inventory`. `StockInvariant` đổi sang `InventoryInvariant`.
**Tiêu chí nghiệm thu:**
- [ ] `commerce` không còn `using Pickory.Modules.Catalog.Contracts.Stock`
- [ ] Đơn sang `shipping` giảm số trên kệ. Hủy vận đơn đưa số trên kệ về như cũ
- [ ] Seed chạy hai lần không cộng thêm hàng
- [ ] Toàn bộ `dotnet test` xanh. Không test nào bị xóa hay skip
- [ ] Bộ E2E của `catalog` và `commerce` xanh
**Kiểm tra:** `dotnet test`, `pnpm test:e2e`
**Phụ thuộc:** INV-08 · **Quy mô:** L (cắt chuyển phải đổi `commerce`, seed, cache và helper test cùng lúc)

### INV-10: Gỡ code tồn kho khỏi catalog
**Mô tả:** Xóa `CAT/Stock/`, `IStockService` và bốn record trong `CATC/Stock/`, `StockChangedEvent`, entity và cấu hình `InventoryMovement`. Bộ lọc `lowStock=true` của `GET /api/admin/products` và `CountLowStockAsync` đọc qua `IInventoryReader`. Ba route cũ chuyển sang `INV`, giữ đường dẫn và hành vi.
**Tiêu chí nghiệm thu:**
- [ ] `CAT` không còn code ghi `skus.stock` ngoài `ISkuStockProjection.ApplyAsync`
- [ ] `ICatalogReader.CountLowStockAsync` bị xóa. `IInventoryReader.CountLowStockAsync` thay thế
- [ ] Bộ lọc sắp hết của trang sản phẩm admin và route tồn kho cho cùng tập SKU
- [ ] Ba route cũ trả cùng response như trước. `InventoryTests` cũ chuyển sang thư mục test của `inventory` và xanh
- [ ] `CatalogPermissions.All` chỉ còn `products:read`, `products:write`
**Kiểm tra:** `dotnet test`, test kiến trúc
**Phụ thuộc:** INV-09 · **Quy mô:** L (xóa nhiều file nhỏ trong `CAT` và `CATC`)

### Checkpoint I3
- [ ] Toàn bộ `dotnet test` và bộ E2E của `catalog`, `commerce` xanh
- [ ] Bất biến mới đúng sau mọi test tích hợp
- [ ] `catalog` không còn ghi tồn kho, không còn `IStockService`
- [ ] Review với người dùng

---

## Phase 4: Quyền và màn hình tồn kho

### INV-11: Quyền mới
**Mô tả:** Thêm `inventory:read` và `inventory-cost:read` vào `Permissions.cs` và `PermissionCatalog.cs`, nhóm `inventory`. Thêm `InventoryPermissions.All`. Migration của `identity` cấp `inventory:read` cho role đang có `products:read`.
**Tiêu chí nghiệm thu:**
- [ ] `InventoryPermissions.All` khớp nhóm `inventory` của danh mục quyền (test như `CatalogPermissionsTests`)
- [ ] Role `sales` và `support` có `inventory:read`, không có `inventory:write` và `inventory-cost:read`
- [ ] Role tự tạo đang có `products:read` nhận thêm `inventory:read` sau migration
- [ ] Ba route cũ đổi sang `inventory:read` cho route đọc. Ma trận quyền cập nhật
**Kiểm tra:** test tích hợp, unit test
**Phụ thuộc:** INV-10 · **Quy mô:** M

### INV-12: API tồn kho, sổ thay đổi, giữ hàng
**Mô tả:** `GET` danh sách và chi tiết `stock-levels`, `PATCH stock-levels/{skuId}`, `GET movements`, `GET stock-holds` theo bảng API của spec.
**Tiêu chí nghiệm thu:**
- [ ] Danh sách tìm được theo tên sản phẩm và mã SKU, có dấu và không dấu cho cùng kết quả. `%` và `_` không khớp mọi dòng
- [ ] `lowStock=true` dùng `reorder_point` của từng SKU
- [ ] `PATCH` đổi `reorderPoint` ghi 1 dòng audit `inventory.stock_level.update`
- [ ] `PATCH` có `averageCost` mà thiếu `inventory-cost:read` trả 403. Khai lần hai trả 409 `AVERAGE_COST_ALREADY_SET`
- [ ] Người thiếu `inventory-cost:read` nhận JSON không có `averageCost` và `unitCost`
- [ ] Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp
**Phụ thuộc:** INV-11 · **Quy mô:** M

### INV-13: API điều chỉnh
**Mô tả:** `POST /api/admin/inventory/adjustments` với mã lý do theo spec.
**Tiêu chí nghiệm thu:**
- [ ] Mã lý do sai chiều hoặc thiếu ghi chú trả 400 `VALIDATION_ERROR`
- [ ] Điều chỉnh làm số bán được âm trả 409 `INSUFFICIENT_STOCK`, không ghi gì
- [ ] Mỗi điều chỉnh ghi 1 dòng sổ, 1 dòng audit `inventory.adjustment.create` trong cùng transaction
- [ ] `customer_return` ghi loại `return_restock`
**Kiểm tra:** test tích hợp
**Phụ thuộc:** INV-12 · **Quy mô:** S

### INV-UI1: Trang tồn kho
**Mô tả:** `features/inventory/admin/stock-levels-page.tsx` ở `/admin/inventory`. Bảng mọi SKU với ba con số, ngưỡng, nhãn sắp hết. Ô tìm và bộ lọc sắp hết nằm trên URL. Menu có nhóm "Kho hàng".
**Tiêu chí nghiệm thu:**
- [ ] Nhãn sắp hết đọc trường `lowStock` của API. `LOW_STOCK_THRESHOLD` bị xóa khỏi `shared/catalog/inventory.ts` và mọi nơi dùng
- [ ] Bộ lọc trang sản phẩm admin vẫn chạy sau khi xóa hằng số
- [ ] Người thiếu `inventory:read` không thấy menu. Mở thẳng URL thấy trang không đủ quyền
- [ ] Theo `docs/design/design-system.md`. Ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** INV-12 · **Quy mô:** M

### INV-UI2: Trang chi tiết SKU và hộp điều chỉnh
**Mô tả:** `/admin/inventory/skus/:skuId`: ba con số, ngưỡng (sửa được), giá vốn (theo quyền), sổ thay đổi, đơn đang giữ, nút điều chỉnh. Route cũ chuyển hướng sang route mới. Link "Tồn kho" trong bảng biến thể trỏ sang route mới.
**Tiêu chí nghiệm thu:**
- [ ] Hộp điều chỉnh chọn mã lý do. Ô số chỉ nhận chiều hợp lệ. Lỗi hiện dưới đúng ô bằng `FieldError`
- [ ] Điều chỉnh thành công làm mới ba con số và sổ thay đổi
- [ ] Người thiếu `inventory:write` không thấy nút điều chỉnh và ô sửa ngưỡng
- [ ] Người thiếu `inventory-cost:read` không thấy cột giá vốn
- [ ] Mở `/admin/products/:productId/skus/:skuId/inventory` chuyển sang `/admin/inventory/skus/:skuId`
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** INV-UI1, INV-13 · **Quy mô:** M

### INV-14: Xóa route và file cũ
**Mô tả:** Xóa ba route cũ và các file frontend cũ trong `features/catalog/admin/` (`inventory-page.tsx`, `sku-inventory-page.tsx`, `adjust-stock-dialog.tsx`, `inventory-logic.ts`, `inventory.css`) cùng test của chúng.
**Tiêu chí nghiệm thu:**
- [ ] Ba route cũ trả 404. `RouteNamingTests` và ma trận quyền cập nhật
- [ ] Không file nào import file đã xóa. `pnpm build` sạch
- [ ] `catalog-inventory.spec.ts` chuyển sang route mới và xanh
**Kiểm tra:** `dotnet test`; bộ lệnh frontend; E2E
**Phụ thuộc:** INV-UI2 · **Quy mô:** L (xóa nhiều file cũ ở hai phía)

### Checkpoint I4
- [ ] Ma trận quyền có mọi route mới, route cũ đã xóa
- [ ] `sales` thấy trang tồn kho, không thấy giá vốn và nút điều chỉnh
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Review với người dùng

---

## Phase 5: Nhà cung cấp và phiếu nhập

### INV-15: API nhà cung cấp
**Mô tả:** `GET`, `POST`, `PATCH /api/admin/inventory/suppliers` theo spec.
**Tiêu chí nghiệm thu:**
- [ ] Tên trùng (không phân biệt hoa thường) trả 409 `SUPPLIER_NAME_TAKEN`
- [ ] Số điện thoại kiểm bằng luật của PROFILE-VAL và lưu dạng `0xxxxxxxxx`. Email kiểm định dạng
- [ ] Không có route xóa. Tắt `active` giữ nhà cung cấp trong phiếu cũ
- [ ] Mỗi lần tạo hoặc sửa ghi 1 dòng audit
**Kiểm tra:** test tích hợp
**Phụ thuộc:** INV-12 · **Quy mô:** M

### INV-UI3: Màn hình nhà cung cấp
**Mô tả:** `/admin/inventory/suppliers`: danh sách, tìm, lọc đang dùng, hộp tạo và sửa.
**Tiêu chí nghiệm thu:**
- [ ] Lỗi tên trùng hiện dưới ô tên
- [ ] Người thiếu `inventory:write` chỉ xem
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** INV-15 · **Quy mô:** M

### INV-16: API phiếu nhập và giá vốn
**Mô tả:** Tạo, sửa nháp, xem, đổi trạng thái phiếu nhập. Xác nhận phiếu đi qua `StockWriter` và `AverageCostCalculator`.
**Tiêu chí nghiệm thu:**
- [ ] Mã phiếu theo dạng `PN-yyyyMMdd-NNN`, không trùng khi tạo đồng thời
- [ ] Một SKU lặp trong phiếu trả 400. Số lượng ngoài khoảng 1 đến 100.000 trả 400
- [ ] Xác nhận cộng số trên kệ, ghi dòng sổ `receipt` có `document_id`, tính lại `average_cost`
- [ ] Hai request xác nhận cùng lúc chỉ cộng hàng một lần
- [ ] Phiếu rỗng, nhà cung cấp đã tắt hoặc SKU đã xóa: 422 `GOODS_RECEIPT_NOT_RECEIVABLE` kèm lý do
- [ ] Phiếu `received` sửa hoặc hủy trả 409
- [ ] Xác nhận chạy cùng lúc với giữ hàng và sửa sản phẩm: không deadlock
- [ ] Người thiếu `inventory-cost:read` không thấy `unitCost`
**Kiểm tra:** test tích hợp, test đồng thời
**Phụ thuộc:** INV-15, INV-01 · **Quy mô:** M

### INV-UI4: Màn hình phiếu nhập
**Mô tả:** Màn hình `/admin/inventory/goods-receipts` gồm ba phần:
- Danh sách phiếu có lọc.
- Form tạo và sửa nháp: chọn nhà cung cấp, tìm SKU, nhập số lượng và giá nhập.
- Nút xác nhận có hộp hỏi lại.
**Tiêu chí nghiệm thu:**
- [ ] Form hiện tổng số lượng và tổng tiền của phiếu
- [ ] Phiếu `received` và `cancelled` chỉ xem, không có nút sửa
- [ ] Lỗi 422 hiện danh sách lý do trên form
- [ ] Người thiếu `inventory-cost:read` không thấy cột giá nhập và tổng tiền
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** INV-16, INV-UI3 · **Quy mô:** M

### Checkpoint I5
- [ ] Xác nhận phiếu nhập cộng hàng đúng một lần và tính đúng giá vốn
- [ ] Review với người dùng

---

## Phase 6: Kiểm kê

### INV-17: API kiểm kê
**Mô tả:** Tạo phiếu, nhập số đếm, xem, đổi trạng thái phiếu kiểm kê theo spec.
**Tiêu chí nghiệm thu:**
- [ ] Tạo phiếu lưu `on_hand_at_start` của từng SKU
- [ ] SKU đang nằm trong phiếu `counting` khác: 409 `STOCKTAKE_IN_PROGRESS`
- [ ] Áp dụng cộng `counted - on_hand_at_start` vào số trên kệ hiện tại, ghi dòng sổ `stocktake`
- [ ] Test có một lần xuất kho giữa lúc tạo và lúc áp dụng: số trên kệ cuối đúng
- [ ] Phần chênh làm số trên kệ nhỏ hơn số đã giữ: 409 `INSUFFICIENT_STOCK` kèm SKU, không áp dụng dòng nào
- [ ] Dòng chưa đếm và dòng chênh 0 không tạo dòng sổ
- [ ] Áp dụng hai lần chỉ đổi số lượng một lần
**Kiểm tra:** test tích hợp
**Phụ thuộc:** INV-12, INV-01 · **Quy mô:** M

### INV-UI5: Màn hình kiểm kê
**Mô tả:** `/admin/inventory/stocktakes`: danh sách, tạo phiếu (mọi SKU đang bật hoặc chọn SKU), trang nhập số đếm, nút áp dụng có hộp hỏi lại.
**Tiêu chí nghiệm thu:**
- [ ] Trang nhập số đếm dùng được bằng bàn phím: Enter chuyển sang dòng sau
- [ ] Dòng có số trên kệ đổi từ lúc tạo phiếu có nhãn cảnh báo
- [ ] Hộp hỏi lại liệt kê số SKU tăng, giảm và tổng chênh
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** INV-17 · **Quy mô:** M

### Checkpoint I6
- [ ] Áp dụng phiếu đúng phần chênh khi có đơn giao xen giữa
- [ ] Review với người dùng

---

## Phase 7: Giá trị tồn kho

### INV-18: API giá trị tồn kho và file CSV
**Mô tả:** `GET /api/admin/inventory/valuation` và `GET /api/admin/exports/inventory-valuation?format=csv`.
**Tiêu chí nghiệm thu:**
- [ ] Tổng giá trị bằng tổng `on_hand × average_cost` của bộ dữ liệu dựng sẵn
- [ ] SKU chưa có giá vốn không cộng vào tổng. Response có số SKU này
- [ ] CSV mở đầu bằng BOM UTF-8. Ô có dấu phẩy, dấu ngoặc kép hoặc xuống dòng được escape
- [ ] CSV thiếu `reports:export` trả 403. Mỗi lần xuất ghi 1 dòng audit
- [ ] `format` khác `csv` trả 400
**Kiểm tra:** unit test CSV; test tích hợp
**Phụ thuộc:** INV-16 · **Quy mô:** M

### INV-UI6: Trang giá trị tồn kho
**Mô tả:** `/admin/inventory/valuation`: tổng giá trị, số SKU chưa có giá vốn, bảng từng SKU, nút tải CSV.
**Tiêu chí nghiệm thu:**
- [ ] Tiền hiện theo định dạng VND của dự án
- [ ] Nút tải CSV chỉ hiện với người có `reports:export`
- [ ] Menu chỉ hiện với người có `inventory-cost:read`
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; ảnh chụp; người dùng mở file CSV bằng Excel
**Phụ thuộc:** INV-18 · **Quy mô:** S

### Checkpoint I7
- [ ] Tổng giá trị khớp bộ dữ liệu dựng sẵn, CSV mở đúng tiếng Việt trong Excel
- [ ] Review với người dùng

---

## Phase 8: E2E và nghiệm thu

### INV-E2E1: Luồng nhập, bán, giao, kiểm kê
**Mô tả:** Playwright chạy luồng đầy đủ.
1. Admin tạo nhà cung cấp.
2. Admin tạo phiếu nhập 10 chiếc và xác nhận.
3. Khách đặt 1 chiếc COD.
4. Admin xác nhận đơn và tạo vận đơn (client GHN giả).
5. Admin tạo phiếu kiểm kê, nhập số đếm 9, áp dụng.
**Tiêu chí nghiệm thu:**
- [ ] Sau bước 2: số trên kệ 10, số bán được 10
- [ ] Sau bước 3: số đã giữ 1, số bán được 9
- [ ] Sau bước 4: số trên kệ 9, số đã giữ 0
- [ ] Sau bước 5: số trên kệ vẫn 9, phiếu không tạo dòng sổ
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** INV-UI4, INV-UI5 · **Quy mô:** S

### INV-E2E2: Quyền của `sales`
**Mô tả:** Playwright đăng nhập `sales` và mở các trang kho hàng.
**Tiêu chí nghiệm thu:**
- [ ] `sales` thấy trang tồn kho, phiếu nhập, kiểm kê, nhà cung cấp
- [ ] `sales` không thấy menu giá trị tồn kho, cột giá vốn, cột giá nhập
- [ ] `sales` không thấy nút điều chỉnh, tạo phiếu, áp dụng kiểm kê
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** INV-UI6 · **Quy mô:** S

### INV-19: Nghiệm thu
**Mô tả:** Kiểm mọi dòng Traceability của plan.
**Tiêu chí nghiệm thu:**
- [ ] Coverage dòng của `Pickory.Modules.Inventory` ít nhất 80%, không tính `Persistence/Migrations/`
- [ ] Ma trận quyền có mọi route của module
- [ ] Bộ lệnh frontend và `dotnet test` sạch
- [ ] Code và tài liệu thay đổi qua kiểm tra mục 6 của `CLAUDE.md`
**Kiểm tra:** báo cáo coverage; script kiểm comment cho `.cs` và bản cho frontend
**Phụ thuộc:** INV-E2E1, INV-E2E2 · **Quy mô:** S
