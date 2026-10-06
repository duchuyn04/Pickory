# Task List: catalog

Plan và quyết định thiết kế: `tasks/plan-catalog.md`. Spec: `spec/SPEC-catalog.md`. Quy ước chung: `spec/SPEC.md`, `AGENTS.md`.

Quy ước: mỗi task phải qua `pnpm lint && pnpm typecheck && pnpm test` trước khi commit. Lệnh test tập trung: `pnpm --filter @pickory/backend test -- <đường dẫn>`. Kích thước: XS 1 file, S 1-2, M 3-5. Số file không tính file test.

Mọi task back-office thêm endpoint mới vào `apps/backend/test/integration/permission-matrix.int.test.ts`.

---

## Phase 1: Nền tảng logic và dữ liệu

### CAT-01: Bộ thuộc tính và validate thông số
**Mô tả:** Kiểu `AttributeDefinition` và hàm `validateSpecs(specs, attributes)` trong `packages/shared`. Hàm `resolveCategoryAttributes(parent, child)` gộp thuộc tính của danh mục cha và danh mục con. Backend và form admin dùng chung hai hàm này.
**Tiêu chí nghiệm thu:**
- [x] Thiếu thuộc tính `required` trả lỗi có `key` của ô thiếu
- [x] Sai kiểu (`number`, `boolean`, `enum` ngoài `options`, `text` rỗng) trả lỗi theo từng `key`
- [x] Khóa không có trong bộ thuộc tính bị từ chối
- [x] Danh mục con có cả thuộc tính của cha; trùng `key` giữa cha và con bị báo lỗi định nghĩa
- [x] Hàm kiểm tra định nghĩa từ chối `enum` không có `options` và `key` sai mẫu `^[a-z][a-z0-9_]*$`
**Kiểm tra:** unit test cho từng nhánh hợp lệ và không hợp lệ
**Phụ thuộc:** không · **File:** `packages/shared/src/catalog/{attributes,validate-specs}.ts`, `packages/shared/src/index.ts` · **Quy mô:** S

### CAT-02: Slug, tổ hợp biến thể, giá hiển thị và chuyển trạng thái
**Mô tả:** Logic thuần trong `modules/catalog/domain/`. `toSlug(name)` bỏ dấu tiếng Việt. `buildVariantCombinations(options)` sinh tổ hợp tối đa 3 trục. `computeDisplayPrice(skus)` tính `minPrice` và `inStock`. `checkPublishable(product)` và `canTransition(from, to)` kiểm tra quy tắc trạng thái.
**Tiêu chí nghiệm thu:**
- [x] `Laptop ASUS ROG Strix G16 Đen` thành `laptop-asus-rog-strix-g16-den`; ký tự đặc biệt và khoảng trắng liên tiếp gộp thành một `-`
- [x] 2 trục `128GB, 256GB` và `Đen, Trắng` sinh đúng 4 tổ hợp; 0 trục sinh 1 biến thể mặc định; 4 trục bị từ chối
- [x] `minPrice` là giá thấp nhất trong SKU bật và còn hàng; hết hàng toàn bộ thì lấy giá thấp nhất trong SKU bật; không có SKU bật thì `null`
- [x] Giá phải là số nguyên lớn hơn 0; `compareAtPrice` trống hoặc lớn hơn giá bán
- [x] `checkPublishable` báo đủ lý do: thiếu thông số bắt buộc, không có ảnh, không có SKU bật có giá, SKU bật thiếu cân nặng hoặc kích thước
**Kiểm tra:** unit test
**Phụ thuộc:** CAT-01 · **File:** `modules/catalog/domain/{slug,variants,pricing,status}.ts` · **Quy mô:** M

### CAT-03: Schema, migration, khung module và sự kiện
**Mô tả:** `prisma/schema/catalog.prisma` với `categories`, `brands`, `products`, `skus`, `product_images`, `product_slug_history`, `inventory_movements`, `image_deletion_jobs`. Migration thêm `CHECK (stock >= 0)`, chỉ mục GIN trên `products.specs`, `@@unique([orderId, skuId, type])`. Thêm `ProductChangedEvent` và `StockChangedEvent` vào `shared/events.ts`. Tạo `modules/catalog/index.ts` (re-export `catalogPermissions`, sự kiện) và `catalogRouter` rỗng gắn vào `app.ts`.
**Tiêu chí nghiệm thu:**
- [x] `pnpm db:migrate` chạy trên DB sạch và trên DB đã có migration cũ
- [x] Cập nhật `stock` thành số âm bị PostgreSQL từ chối
- [x] Hai movement cùng `orderId`, `skuId`, `type` bị từ chối
- [x] Hàm `emitAfterCommit` không phát sự kiện khi transaction ném lỗi, phát đúng 1 lần khi commit
- [x] ESLint vẫn chặn module khác import file nội bộ của `catalog`
- [x] `vitest.config.ts` có ngưỡng 80% dòng cho `modules/catalog/services/*.ts` và `modules/catalog/domain/*.ts`
**Kiểm tra:** integration test ràng buộc DB; unit test `emitAfterCommit`
**Phụ thuộc:** CAT-02, T03 · **File:** `prisma/schema/catalog.prisma`, migration mới, `shared/events.ts`, `modules/catalog/{index,data/events}.ts`, `app.ts`, `vitest.config.ts` · **Quy mô:** M (vượt 5 file vì phần khung chỉ thêm vài dòng mỗi file)

### Checkpoint C1
- [x] `pnpm lint && pnpm typecheck && pnpm test && pnpm build` sạch

---

## Phase 2: Back-office backend và UI

### CAT-13: Tài liệu mô tả: schema và trích text
**Mô tả:** Schema Zod của tài liệu mô tả trong `packages/shared/src/catalog/`. Schema chỉ nhận nút `doc`, `paragraph`, `heading` (cấp 1 đến 3) và `text`. Nút `text` chỉ có mark `bold` hoặc `italic`. Thêm `descriptionToPlainText(doc)` và `plainTextToDescription(text)`. Backend và trình soạn thảo dùng chung schema.
**Tiêu chí nghiệm thu:**
- [x] Tài liệu có đoạn văn, tiêu đề cấp 1, 2, 3, chữ đậm, chữ nghiêng, vừa đậm vừa nghiêng được nhận
- [x] Nút lạ (`script`, `image`, `bulletList`, `codeBlock`), mark lạ (`link`, `underline`), tiêu đề cấp 4, thuộc tính thừa đều bị từ chối
- [x] Tài liệu vượt 20.000 ký tự text hoặc 500 nút bị từ chối; giới hạn nằm ở một hằng số
- [x] Tài liệu rỗng (một đoạn văn không có text) được nhận
- [x] `descriptionToPlainText` giữ thứ tự, mỗi đoạn và tiêu đề cách nhau một dòng, bỏ mark
- [x] `plainTextToDescription` tách đoạn theo dòng trống; kết quả luôn qua schema; hai hàm đổi qua lại giữ nguyên text
**Kiểm tra:** unit test, gồm bộ ví dụ hợp lệ và không hợp lệ
**Phụ thuộc:** CAT-01 · **File:** `packages/shared/src/catalog/{description-schema,description-text}.ts`, `packages/shared/src/index.ts` · **Quy mô:** S

### UI-C8: Trình soạn thảo mô tả
**Mô tả:** Component `DescriptionEditor` dùng Tiptap. Chỉ bật đoạn văn, tiêu đề 3 cấp, chữ đậm, chữ nghiêng, hoàn tác và làm lại. Thanh công cụ có nhãn tiếng Việt: Đoạn văn, Tiêu đề 1, Tiêu đề 2, Tiêu đề 3, Đậm, Nghiêng. Kết quả luôn qua schema của CAT-13. Người dùng đã duyệt Tiptap ngày 2026-10-05. Builder chọn extension tối thiểu, ghim phiên bản sau khi đối chiếu tài liệu chính thức, và ghi lựa chọn vào ADR hoặc mục Kết quả của task.
**Tiêu chí nghiệm thu:**
- [x] Bấm Tiêu đề 2 và Đậm cho ra tài liệu JSON đúng và qua schema
- [x] Nút trên thanh công cụ có `aria-pressed` theo định dạng tại vị trí con trỏ và có nhãn truy cập được
- [x] Dùng được hoàn toàn bằng bàn phím: phím tắt Ctrl+B, Ctrl+I, và di chuyển giữa các nút của thanh công cụ
- [x] Dán nội dung có link, ảnh, danh sách, `script` thì chỉ giữ text và định dạng trong danh sách bật
- [x] Trình soạn thảo mở được tài liệu đã lưu và lưu lại không đổi nội dung
- [x] Không cuộn ngang ở viewport 375 px; ô nhập có `role="textbox"` và nhãn
- [x] Không thêm extension ngoài danh sách bật; `pnpm-lock.yaml` chỉ thêm các package của Tiptap cần dùng
**Kiểm tra:** test component với jsdom; Playwright trên `pnpm dev` cho thao tác bàn phím và dán
**Phụ thuộc:** CAT-13 · **File:** `features/catalog/admin/{description-editor,description-toolbar}.tsx`, `apps/frontend/package.json`, `pnpm-lock.yaml` · **Quy mô:** M

### CAT-04: Danh mục và thương hiệu
**Mô tả:** `POST/PATCH/DELETE /api/admin/categories[/:id]` và `/api/admin/brands[/:id]` (`products:write`). `GET /api/categories` trả cây kèm thuộc tính `filterable`. `GET /api/brands` công khai. Cây tối đa 2 cấp.
**Tiêu chí nghiệm thu:**
- [x] Tạo danh mục cấp 3 trả 400
  Cách hiểu: Danh mục gốc có `parentId` là null (cấp 1). Danh mục con có `parentId` trỏ tới cấp 1 (cấp 2). Khi tạo hoặc sửa mà `parentId` trỏ tới danh mục cấp 2 thì API trả 400 `CATEGORY_LEVEL_EXCEEDED`.
- [x] Thêm thuộc tính không bắt buộc và sửa `label` khi đã có sản phẩm thì thành công
  Cách hiểu: Khi danh mục đã có sản phẩm, chỉ cho phép thêm thuộc tính có `required=false`. Cho phép sửa `label`, `group`, `unit` và thêm giá trị `enum`. Không được đổi `required` sang `true` hoặc bỏ giá trị `enum` đang dùng.
- [x] Xóa thuộc tính hoặc đổi `type` khi có sản phẩm dùng trả 409 `ATTRIBUTE_IN_USE`
  Cách hiểu: Thuộc tính có sản phẩm dùng khi sản phẩm trong danh mục hoặc danh mục con chứa `key` đó trong `specs`. Khi xóa thuộc tính hoặc đổi `type` của thuộc tính đang dùng thì trả 409 `ATTRIBUTE_IN_USE`. Bỏ option `enum` đang dùng cũng trả 409.
- [x] Đổi `key` của thuộc tính đã có bị từ chối
  Cách hiểu: Xóa thuộc tính không có sản phẩm dùng được phép thành công. Đổi khóa của thuộc tính đang dùng bị coi là xóa khóa cũ và trả 409 `ATTRIBUTE_IN_USE`.
- [x] Xóa danh mục còn danh mục con hoặc còn sản phẩm bị từ chối; xóa thương hiệu còn sản phẩm bị từ chối
  Cách hiểu: Xóa danh mục còn danh mục con trả 400 `CATEGORY_HAS_CHILDREN`. Xóa danh mục còn sản phẩm trả 400 `CATEGORY_HAS_PRODUCTS`. Xóa thương hiệu còn sản phẩm trả 400 `BRAND_HAS_PRODUCTS`. Quy tắc lá: không tạo con dưới danh mục có sản phẩm. Không chuyển danh mục có sản phẩm hoặc chuyển dưới cha có sản phẩm.
- [x] `sales` gọi `GET` được, gọi `POST` nhận 403; endpoint mới có trong ma trận quyền
  Cách hiểu: Các route `GET /api/categories` và `GET /api/brands` là công khai. Các route `POST`, `PATCH`, `DELETE` back-office yêu cầu quyền `products:write`. Nhân viên role `sales` chỉ có `products:read` nên nhận 403 khi gọi ghi.

Cách hiểu thêm: Đổi tên danh mục hoặc thương hiệu giữ nguyên slug ban đầu vì bộ lọc URL dùng slug.
**Kiểm tra:** integration test
**Phụ thuộc:** CAT-03 · **File:** `modules/catalog/services/{categories,brands}.ts`, `modules/catalog/routes/{admin-categories,public-catalog}.ts`, `modules/catalog/schemas.ts` · **Quy mô:** M
**File (.NET):** `apps/backend/src/Pickory.Modules.Catalog/Features/Categories/{CategoryModels.cs, CategoryValidators.cs, CategoryManagementHandler.cs, CategoriesEndpoints.cs}`, `apps/backend/src/Pickory.Modules.Catalog/Features/Brands/{BrandModels.cs, BrandValidators.cs, BrandManagementHandler.cs, BrandsEndpoints.cs}`, `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`, `apps/backend/tests/Pickory.IntegrationTests/Api/Catalog/{CategoryManagementTests.cs, BrandManagementTests.cs}`

**Bằng chứng (2026-10-06):**

- `dotnet build apps/backend/Pickory.sln` thành công, đạt 0 cảnh báo và 0 lỗi.
- `dotnet test apps/backend/Pickory.sln` chạy xanh 790 test trên 3 project (3 kiến trúc, 359 unit, 428 integration). Có 4 test gọi AI thật được bỏ qua.
- Port thành công ma trận quyền sang `PermissionMatrixTests.cs`, đạt 120 test (5 theory x 24 endpoint) cho các route back-office.
- Bộ test integration gồm 24 test danh mục và 9 test thương hiệu đều vượt qua.
- Phép thử đột biến: 8/8 đột biến bị test bắt trên toàn bộ các điểm kiểm tra.
- Trong `apps/frontend`: `pnpm lint`, `pnpm typecheck`, `pnpm test` và `pnpm test:e2e` đều xanh.
- Route công khai `GET /api/categories` và `GET /api/brands` trả mã 200 khi chạy API thật.
- Script kiểm tra comment báo 0 vi phạm. Toàn bộ file không chứa ký tự em dash hoặc en dash.
### UI-C4: Màn danh mục và thương hiệu
**Mô tả:** Back-office quản lý cây danh mục, trình soạn bộ thuộc tính, quản lý thương hiệu. Ẩn nút sửa khi thiếu `products:write`.
**Tiêu chí nghiệm thu:**
- [x] Tạo danh mục con, thêm thuộc tính `enum` có `options`, lưu thành công
- [x] Lỗi `ATTRIBUTE_IN_USE` hiện thông báo tiếng Việt có `role="alert"`
- [x] `sales` thấy danh sách, không thấy nút tạo, sửa, xóa
**Kiểm tra:** test component; Playwright trên `pnpm dev`
**Phụ thuộc:** CAT-04, UI-05 · **File:** `features/catalog/admin/{categories-page,attribute-editor,brands-page}.tsx`, `app.tsx` · **Quy mô:** M

Quyết định (2026-10-06):
Route công khai `GET /api/categories` chỉ trả thuộc tính `filterable` và tự gộp thuộc tính cha vào con, nên không phù hợp cho màn hình quản trị chỉnh sửa danh mục. Hệ thống bổ sung route `GET /api/admin/categories` (`products:read`) trả danh sách phẳng toàn bộ danh mục kèm đầy đủ thuộc tính riêng của từng danh mục.

**File (.NET và frontend):** `apps/backend/src/Pickory.Modules.Catalog/Features/Categories/{CategoriesEndpoints.cs, CategoryManagementHandler.cs}`, `apps/backend/tests/Pickory.IntegrationTests/Api/Catalog/CategoryManagementTests.cs`, `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`, `apps/frontend/src/features/catalog/admin/{categories-page.tsx, attribute-editor.tsx, brands-page.tsx, admin-form-dialog.tsx, attribute-helpers.ts, catalog-errors.ts}`, `apps/frontend/src/shared/catalog/attributes.ts`, `apps/frontend/src/app.tsx`, `apps/frontend/src/features/identity/admin/admin-layout.tsx`, `apps/frontend/test/unit/features/catalog/admin/{attribute-editor.test.tsx, attribute-helpers.test.ts}`, `apps/frontend/test/unit/shared/catalog/attributes.test.ts`, `apps/frontend/e2e/{staff-invite.spec.ts, environment.ts, outbox.ts, catalog-admin.spec.ts}`

**Bằng chứng (2026-10-06):**

- `dotnet build apps/backend/Pickory.sln` báo 0 cảnh báo và 0 lỗi.
- `dotnet test apps/backend/Pickory.sln` xanh 896 test trên 3 project: 3 kiến trúc, 359 unit, 534 integration.
- `dotnet test` bỏ qua 4 test gọi AI thật. Review lượt 2 không đổi backend, nên số liệu backend lấy từ lượt 1.
- `PermissionMatrixTests.cs` có thêm route `GET /api/admin/categories` với quyền `products:read`.
- Trong `apps/frontend`, `pnpm lint` và `pnpm typecheck` xanh.
- `pnpm test` xanh 13 file và 132 test.
- `pnpm test:e2e` xanh 3 test Playwright. Spec `catalog-admin.spec.ts` ghi 0 lỗi và 0 cảnh báo console.
- Spec chụp 7 ảnh ở 1280px và 375px. Ở 375px, spec kiểm tra trang và hộp thoại không tràn ngang.
- Phép thử đột biến: 11/11 đột biến bị test bắt.
- Script kiểm tra comment báo 0 vi phạm. File đã đổi không có em dash hay en dash.

### CAT-05: Tạo và sửa sản phẩm, biến thể
**Mô tả:** `POST/PATCH /api/admin/products[/:id]` (`products:write`), `GET /api/admin/products[/:id]` (`products:read`, mọi trạng thái, tìm theo tên hoặc mã SKU). Validate thông số theo danh mục lá. Validate mô tả bằng schema của CAT-13. Sinh SKU từ tùy chọn, giữ SKU cũ khi tổ hợp không đổi. Sinh slug, lưu slug cũ vào lịch sử khi đổi. Ghi audit khi đổi giá. Phát `onProductChanged` sau commit.
**Tiêu chí nghiệm thu:**
- [x] Sản phẩm gắn vào danh mục không phải lá trả 400
  Cách hiểu: Danh mục lá không có danh mục con. Danh mục có con nhận 400 `CATEGORY_NOT_LEAF`. Danh mục không tồn tại nhận 400 `INVALID_CATEGORY`. Thương hiệu không tồn tại nhận 400 `INVALID_BRAND`.
- [x] Thông số sai trả 400 `SPECS_INVALID` kèm danh sách `key` lỗi, không có gì được lưu
  Cách hiểu: Trường `specs` được thẩm định theo danh mục lá qua `SpecValidator`. Lỗi sai định dạng hoặc thiếu trường bắt buộc làm API trả 400 `SPECS_INVALID`. Danh sách lỗi chi tiết nằm trong mảng `details` với `key` và `message`. Khi xảy ra lỗi này, toàn bộ thay đổi bị hủy và không ghi cơ sở dữ liệu.
- [x] Mô tả có nút lạ (ví dụ `script`, `image`) hoặc vượt giới hạn trả 400 `DESCRIPTION_INVALID`, không có gì được lưu
  Cách hiểu: Tài liệu `description` được thẩm định qua `DescriptionSchema.Validate`. Nút lạ hoặc tài liệu vượt giới hạn trả 400 `DESCRIPTION_INVALID`. Khi thẩm định thất bại, transaction hoàn nguyên và cơ sở dữ liệu giữ nguyên.
- [x] Thêm giá trị tùy chọn sinh thêm SKU, SKU cũ giữ giá và tồn kho
  Cách hiểu: Hệ thống hỗ trợ tối đa 3 trục tùy chọn và 100 tổ hợp biến thể. Tập `variants` phải khớp đúng tích Descartes từ `VariantCombinations.Build(options)`. Sản phẩm không tùy chọn có đúng 1 biến thể với `optionValues` rỗng. Tổ hợp mới tạo SKU với `stock = 0`.

  SKU cũ giữ `Id` và `Stock`. Mã `Code` chỉ đổi khi request gửi mã mới. Giá và kích thước cập nhật theo request. Giá cũ giữ nguyên khi request không đổi giá.
- [x] Mã SKU trùng trả 409
  Cách hiểu: Khi quản trị viên nhập mã `code` bị trùng SKU khác, API trả 409 `SKU_CODE_ALREADY_EXISTS`. Khi bỏ trống `code`, hệ thống tự sinh mã từ slug và giá trị tùy chọn. Nếu mã tự sinh bị trùng, hệ thống tự động gắn hậu tố số tăng dần.
- [x] Bỏ một giá trị tùy chọn thì SKU tương ứng bị tắt (`enabled = false`), không bị xóa, tồn kho và movement giữ nguyên (Decision 18)
  Cách hiểu: Khi bỏ bớt giá trị tùy chọn, các SKU không còn trong danh sách mới nhận `enabled = false`. Dữ liệu SKU và lịch sử `InventoryMovement` cũ vẫn được lưu giữ. Khi thêm lại giá trị đã bỏ, SKU cũ được bật lại và giữ nguyên `Id`, `Code`, `Stock`.
- [x] Tên chỉ gồm ký hiệu cho ra slug rỗng thì dùng slug dự phòng `san-pham`, rồi thêm hậu tố số nếu trùng
  Cách hiểu: Khi `SlugGenerator.ToSlug` trả về chuỗi rỗng do tên chỉ chứa ký hiệu, handler dùng slug dự phòng `san-pham`. Nếu slug `san-pham` đã tồn tại, hệ thống bổ sung hậu tố số tự tăng.
- [x] Slug mới không trùng slug hiện tại của sản phẩm khác và không trùng `oldSlug` trong `product_slug_history`
  Cách hiểu: Slug mới không được trùng với slug hiện tại hoặc `oldSlug` của sản phẩm khác. Nếu slug mới trùng `oldSlug` của chính sản phẩm này thì cho phép và xóa dòng lịch sử cũ.
- [x] Đổi giá 1 SKU tạo đúng 1 dòng audit `catalog.sku.price_change` có giá trước và sau
  Cách hiểu: Khi giá bán `price` của một SKU thay đổi trong `PATCH`, hệ thống ghi đúng 1 dòng nhật ký `catalog.sku.price_change`. Nhật ký ghi nhận cả giá trước và giá sau trong cùng transaction qua `IAuditLogWriter`. Tạo mới sản phẩm hoặc chỉ đổi `compareAtPrice` thì không ghi nhật ký giá.
- [x] Đổi tên với slug mới lưu slug cũ vào `product_slug_history`
  Cách hiểu: Khi cập nhật tên sản phẩm làm thay đổi slug, slug cũ được lưu vào bảng `product_slug_history`. Nếu tên mới không làm thay đổi slug thì hệ thống không lưu thêm bản ghi lịch sử.
- [x] `onProductChanged` phát đúng 1 lần sau lưu thành công, 0 lần khi validate lỗi
  Cách hiểu: Sự kiện `ProductChangedEvent` được phát đúng 1 lần sau khi transaction commit thành công. Hành động phát ra là `Created` cho `POST` và `Updated` cho `PATCH`. Bất kỳ lỗi thẩm định hoặc lỗi transaction nào đều phát 0 sự kiện.
- [x] `sales` xem được sản phẩm `draft`, gọi `PATCH` nhận 403
  Cách hiểu: Người dùng có quyền `products:read` như vai trò `sales` xem được danh sách và chi tiết sản phẩm `draft`. Khi người dùng thiếu `products:write` gọi `POST` hoặc `PATCH`, API trả mã lỗi 403 Forbidden.

Cách hiểu thêm: Sản phẩm mới luôn có trạng thái `draft`. SKU mới có `stock = 0`. API từ chối trường `status` hoặc `stock` trong `POST` và `PATCH` với 400 `VALIDATION_ERROR`.

`PATCH` đổi `options` mà không gửi `variants` bị từ chối nếu tập tổ hợp hiện tại không khớp tùy chọn mới. SKU cũ giữ mã khi request không gửi `code` mới. API tính lại `MinPrice` và `InStock` sau mỗi lần lưu.

Các số đo đóng gói (`weightGrams`, `lengthCm`, `widthCm`, `heightCm`) không bắt buộc khi lưu nháp sản phẩm. Nếu yêu cầu gửi số đo, giá trị phải lớn hơn 0. Tiêu chí kiểm tra bắt buộc đủ số đo đối với biến thể đang bật được thực hiện khi đăng bán sản phẩm trong CAT-07.

`ProductWriteLockKey` là khóa advisory dùng chung cho transaction ghi sản phẩm. Khóa tuần tự hóa `POST` và `PATCH`, tránh cấp trùng slug hoặc tổ hợp SKU khi ghi đồng thời. Cách này giảm khả năng ghi song song, nhưng không đổi schema hoặc thêm retry.
**Kiểm tra:** integration test
**Phụ thuộc:** CAT-04, CAT-13 · **File:** `modules/catalog/services/products.ts`, `modules/catalog/data/product-repository.ts`, `modules/catalog/routes/admin-products.ts`, `modules/catalog/schemas.ts` · **Quy mô:** M
**File (.NET):** `apps/backend/src/Pickory.Modules.Catalog/Features/Products/{ProductModels.cs, ProductValidators.cs, ProductManagementHandler.cs, ProductsEndpoints.cs}`, `apps/backend/src/Pickory.Modules.Catalog/CatalogModule.cs`, `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`, `apps/backend/tests/Pickory.IntegrationTests/Infrastructure/PickoryWebApplicationFactory.cs`, `apps/backend/tests/Pickory.IntegrationTests/Api/Catalog/ProductManagementTests.cs`

**Bằng chứng (2026-10-06):**

- `dotnet build apps/backend/Pickory.sln` thành công, đạt 0 cảnh báo và 0 lỗi.
- `dotnet test apps/backend/Pickory.sln` chạy xanh 889 test trên 3 project (3 kiến trúc, 359 unit, 527 integration). Có 4 test gọi AI thật được bỏ qua.
- Thêm 4 endpoint sản phẩm vào `PermissionMatrixTests.cs`, đạt 140 test (5 theory x 28 endpoint) cho toàn bộ các route back-office.
- Bộ test integration gồm 79 test sản phẩm và biến thể trong `ProductManagementTests.cs` đều vượt qua.
- Phép thử đột biến: 41/41 đột biến bị test bắt trên toàn bộ các điểm kiểm tra.
- Smoke test qua HTTP thật trên cổng 3095: POST 201, PATCH 200, sales GET 200, list 200, sales PATCH 403, mô tả độc hại 400 `DESCRIPTION_INVALID`, tồn kho 0, lưu lịch sử slug và audit giá đầy đủ.
- Không sửa frontend nên không chạy lại test frontend.
- Script kiểm tra comment báo 0 vi phạm. Toàn bộ file không chứa ký tự em dash hoặc en dash.

### UI-C5: Danh sách và form sản phẩm
**Mô tả:** Danh sách lọc theo trạng thái, danh mục, sắp hết hàng. Form sản phẩm: ô thông số sinh theo danh mục, trình soạn mô tả (UI-C8), trình soạn tùy chọn, bảng biến thể sinh từ tổ hợp. Form dùng `validateSpecs` của `packages/shared` để báo lỗi trước khi gửi.
**Tiêu chí nghiệm thu:**
- [x] Đổi danh mục thì ô thông số đổi theo bộ thuộc tính của danh mục mới
- [x] Thêm 2 trục tùy chọn thì bảng biến thể hiện đủ tổ hợp để nhập giá, cân nặng, kích thước; tồn kho hiện chỉ đọc
- [x] Lỗi validate hiện ngay dưới ô sai, có nhãn truy cập được
- [x] `sales` mở form ở chế độ chỉ xem, không có nút lưu
- [x] Trang admin sản phẩm tải theo kiểu lazy (`React.lazy`), để Tiptap không nằm trong bundle chính của trang khách; ghi kích thước bundle chính trước và sau
**Kiểm tra:** test component; Playwright trên `pnpm dev`
**Phụ thuộc:** CAT-05, UI-C4, UI-C8 · **File:** `features/catalog/admin/{products-page,product-form,spec-fields,variant-table}.tsx`, `app.tsx` · **Quy mô:** M
**Quyết định (2026-10-06):**
- Tồn kho trong bảng biến thể chỉ đọc. CAT-05 từ chối `stock` trong `POST` và `PATCH`. UI-C7 cập nhật tồn kho với lý do và nhật ký.
- Sắp hết hàng gồm sản phẩm có SKU đang bật với `stock <= 5`, kể cả 0. Backend và frontend cùng dùng ngưỡng 5.

**File (.NET và frontend):**
- `apps/backend/src/Pickory.Modules.Catalog/Features/Products/{ProductManagementHandler,ProductModels,ProductsEndpoints}.cs`
- `apps/backend/src/Pickory.Modules.Catalog/Products/CatalogInventoryDefaults.cs`
- `apps/backend/tests/Pickory.IntegrationTests/Api/Catalog/ProductManagementTests.cs`
- `apps/frontend/src/features/catalog/admin/{product-form,product-options-editor,products-page,spec-fields,variant-table}.tsx`
- `apps/frontend/src/features/catalog/admin/product-logic.ts`, `apps/frontend/src/features/catalog/admin/products.css`
- `apps/frontend/src/shared/catalog/inventory.ts`, `apps/frontend/src/app.tsx`, `apps/frontend/src/shared/api.ts`
- `apps/frontend/e2e/catalog-products.spec.ts`, `apps/frontend/test/unit/features/catalog/product-logic.test.ts`
- `apps/frontend/test/unit/features/catalog/admin/product-form.test.tsx`

**Bằng chứng (2026-10-06):** Playwright có giao diện và chế độ ẩn đều đạt 5/5. Vitest đạt 169/169.
Backend đạt 3 test kiến trúc, 359 test đơn vị và 543 test tích hợp; 4 test tích hợp bỏ qua.
Build backend có 0 cảnh báo và 0 lỗi. Năm đột biến ở vòng sửa này đều bị test phát hiện. Script kiểm tra comment báo 0 vi phạm.

Đo kích thước bundle bằng lệnh `env -u NODE_ENV pnpm exec vite build` trong `apps/frontend`.
Mốc trước khi tách lazy trên `HEAD`: bundle chính `index-*.js` là 443.14 kB (gzip 115.03 kB).
Mốc sau khi tách lazy: bundle chính `index-*.js` là 444.71 kB (gzip 115.56 kB).
Bundle form sản phẩm `product-form-*.js` là 432.02 kB (gzip 130.40 kB), nằm ngoài bundle chính.
Bundle trang danh sách `products-page-*.js` là 4.42 kB (gzip 1.56 kB).
Bundle chính không chứa chuỗi `@tiptap` hoặc `prosemirror`.

### CAT-06: Ảnh và Cloudinary
**Mô tả:** `POST /api/admin/uploads/signature` (`products:write`) cấp chữ ký Cloudinary. API lưu `publicId`, alt, thứ tự, SKU gắn với ảnh. Bỏ ảnh thì ghi `image_deletion_jobs` trong cùng transaction. Worker xóa ảnh thử lại với backoff. Thiếu `CLOUDINARY_*` ở dev và test thì dùng provider giả; production thiếu khóa thì config lỗi.
**Tiêu chí nghiệm thu:**
- [ ] Chữ ký khớp giá trị tính tay với secret giả; response không chứa `CLOUDINARY_API_SECRET`
- [ ] Chữ ký giới hạn định dạng JPG, PNG, WebP và kích thước 5 MB
- [ ] Ảnh thứ 11 của một sản phẩm bị từ chối
- [ ] Bỏ ảnh tạo 1 job; job lỗi thì tăng số lần thử và hẹn giờ thử lại; quá 8 lần thì dừng và ghi log lỗi
- [ ] Không test nào gọi Cloudinary thật
**Kiểm tra:** unit chữ ký và backoff; integration API và job với provider giả
**Phụ thuộc:** CAT-05 · **File:** `modules/catalog/services/{images,image-deletion}.ts`, `modules/catalog/data/cloudinary.ts`, `shared/config.ts`, `server.ts` · **Quy mô:** M

### UI-C6: Tải ảnh và sắp xếp ảnh
**Mô tả:** Trong form sản phẩm: lấy chữ ký, tải ảnh thẳng lên Cloudinary, sắp xếp thứ tự bằng kéo thả và bằng nút lên xuống, gắn ảnh cho màu, sửa alt text.
**Tiêu chí nghiệm thu:**
- [ ] File sai định dạng hoặc trên 5 MB bị chặn trước khi tải, có thông báo tiếng Việt
- [ ] Sắp xếp làm được bằng bàn phím (nút lên, xuống), không chỉ bằng chuột
- [ ] Ảnh có ô alt text bắt buộc
**Kiểm tra:** test component với provider giả; thủ công với Cloudinary thật nếu có khóa
**Phụ thuộc:** CAT-06, UI-C5 · **File:** `features/catalog/admin/{image-manager,use-image-upload}.tsx`, `features/catalog/shared/image-url.ts` · **Quy mô:** S

### CAT-07: Đổi trạng thái và xóa cứng
**Mô tả:** `POST /api/admin/products/:id/status` và `DELETE /api/admin/products/:id` (`products:write`). Dùng `checkPublishable` và `canTransition` của CAT-02. Ghi audit và phát `onProductChanged` với `change: 'status'` hoặc `'deleted'`.
**Tiêu chí nghiệm thu:**
- [ ] Chuyển `active` khi thiếu ảnh, thiếu thông số bắt buộc hoặc SKU bật thiếu cân nặng, kích thước trả 422 `PRODUCT_NOT_PUBLISHABLE` kèm danh sách lý do
- [ ] Mỗi lần đổi trạng thái có đúng 1 dòng audit `catalog.product.status_change`
- [ ] Xóa cứng sản phẩm có SKU từng có movement `order_reserve` trả 409 `PRODUCT_HAS_ORDERS`
- [ ] Xóa cứng thành công thì tạo job xóa ảnh cho mọi ảnh của sản phẩm
- [ ] Xóa cứng sản phẩm chỉ có movement `manual` hoặc `return` thành công; movement của sản phẩm bị xóa trong cùng transaction (Decision 18)
- [ ] `checkPublishable` dùng `validatePrice` cho từng SKU bật (giá nguyên dương, `compareAtPrice` hợp lệ); mỗi lý do lỗi của SKU ghi mã SKU
**Kiểm tra:** integration test
**Phụ thuộc:** CAT-06 · **File:** `modules/catalog/services/product-status.ts`, `modules/catalog/routes/admin-products.ts` · **Quy mô:** S

### CAT-08: Tồn kho, giữ hàng và trả hàng
**Mô tả:** `applyStockMovement(tx, movement)` là hàm duy nhất đổi `stock`. `POST /api/admin/skus/:id/adjust-stock` (`inventory:write`, bắt buộc lý do). `GET /api/admin/skus/:id/movements` và `GET /api/admin/inventory/low-stock` (`products:read`). `stock.reserve` và `stock.release` cho `commerce`. Phát `onStockChanged` sau commit.
**Tiêu chí nghiệm thu:**
- [ ] 20 `reserve` đồng thời cho SKU còn 5 cái: đúng 5 thành công, tồn kho về 0, không âm
- [ ] `reserve` thiếu 1 SKU thì không giữ SKU nào, trả `shortages` đúng số
- [ ] Gọi `reserve` hoặc `release` 2 lần cùng `orderId` không đổi tồn kho lần thứ 2
- [ ] `reserve` và `release` nhận `tx` của bên gọi kèm collector; `onStockChanged` không phát khi transaction của bên gọi rollback, phát sau khi bên gọi `flush()` (Decision 4)
- [ ] `stock` của mọi SKU bằng tổng `delta` của movement sau mỗi file test
- [ ] Điều chỉnh tay thiếu lý do trả 400; thành công tạo đúng 1 dòng audit `catalog.sku.stock_adjust`
- [ ] Tồn kho đổi thì `minPrice` và `inStock` của sản phẩm cập nhật trong cùng transaction
- [ ] `sales` gọi `adjust-stock` nhận 403
**Kiểm tra:** integration test, gồm test đồng thời bằng `Promise.all` trên DB thật
**Phụ thuộc:** CAT-05 · **File:** `modules/catalog/services/inventory.ts`, `modules/catalog/data/stock.ts`, `modules/catalog/routes/admin-inventory.ts`, `modules/catalog/index.ts` · **Quy mô:** M

### UI-C7: Màn tồn kho
**Mô tả:** Danh sách SKU sắp hết hàng (ngưỡng 5), form điều chỉnh có ô lý do bắt buộc, bảng lịch sử movement của SKU.
**Tiêu chí nghiệm thu:**
- [ ] Điều chỉnh thành công thì tồn kho và lịch sử cập nhật không cần tải lại trang
- [ ] Không có `inventory:write` thì không thấy form điều chỉnh
**Kiểm tra:** test component; Playwright trên `pnpm dev`
**Phụ thuộc:** CAT-08, UI-C5 · **File:** `features/catalog/admin/{inventory-page,adjust-stock-dialog}.tsx`, `app.tsx` · **Quy mô:** S

### Checkpoint C2
- [ ] Admin tạo danh mục, sản phẩm 2 tùy chọn, tải ảnh (provider giả), đăng bán trên `pnpm dev`
- [ ] Ma trận quyền có mọi endpoint back-office của `catalog`
- [ ] Review với người dùng

---

## Phase 3: Đọc công khai và giao diện khách

### CAT-09: Danh sách sản phẩm có lọc và facets
**Mô tả:** `GET /api/products` theo bảng tham số trong spec và Decision 11, 12 của `plan-catalog.md`. Chỉ trả sản phẩm `active` và `discontinued`. `catalogReader.listProducts` dùng cùng bộ lọc.
**Tiêu chí nghiệm thu:**
- [ ] Lọc kết hợp danh mục, thương hiệu, khoảng giá, `inStock` và 3 thuộc tính trả đúng tập sản phẩm
- [ ] `facets` đếm đúng theo kiểu loại trừ chính nó
- [ ] Thuộc tính không `filterable` hoặc không tồn tại trả 400 `UNKNOWN_FILTER`
- [ ] `pageSize` trên 48 trả 400; ba kiểu `sort` đúng thứ tự
- [ ] Danh mục cha trả cả sản phẩm của danh mục con
**Kiểm tra:** integration test với bộ dữ liệu cố định
**Phụ thuộc:** CAT-08, CAT-07 · **File:** `modules/catalog/services/product-search.ts`, `modules/catalog/data/product-query.ts`, `modules/catalog/routes/public-catalog.ts` · **Quy mô:** M

### UI-C1: Trang danh mục
**Mô tả:** Lưới sản phẩm, bộ lọc sinh từ thuộc tính `filterable`, sắp xếp, phân trang. Trạng thái lọc nằm trên URL. Ảnh dùng URL biến đổi Cloudinary (`f_auto,q_auto`, chiều rộng 200/400/800/1200).
**Tiêu chí nghiệm thu:**
- [ ] Mở lại URL đã chia sẻ thì thấy đúng bộ lọc và trang
- [ ] Facet hiện số đếm, ví dụ "RAM 16GB (12)"
- [ ] Không cuộn ngang ở viewport 375 px; bộ lọc dùng được bằng bàn phím
**Kiểm tra:** test component; Playwright trên `pnpm dev`
**Phụ thuộc:** CAT-09, UI-C6 · **File:** `features/catalog/{category-page,product-filters,product-card}.tsx`, `app.tsx` · **Quy mô:** M

### CAT-10: Chi tiết, slug cũ, so sánh và catalogReader
**Mô tả:** `GET /api/products/:slug`, `GET /api/products/compare?ids=`. Hoàn thiện `catalogReader`: `getProduct`, `getProducts`, `getSkus`, `listForIndexing`, `getAttributeDefinitions`, `countLowStock`.
**Tiêu chí nghiệm thu:**
- [ ] Slug cũ trả 301 sang slug mới
- [ ] Sản phẩm `draft` và `hidden` trả 404; `discontinued` trả 200 có trạng thái để hiện "Ngừng kinh doanh"
- [ ] So sánh 4 sản phẩm cùng danh mục trả đủ thông số; khác danh mục trả 400 `COMPARE_CATEGORY_MISMATCH`; trên 4 sản phẩm trả 400
- [ ] `getProducts` giữ thứ tự `ids` và bỏ qua id không hợp lệ
- [ ] `listForIndexing` duyệt hết sản phẩm bằng cursor, không lặp, không sót
**Kiểm tra:** integration test
**Phụ thuộc:** CAT-09 · **File:** `modules/catalog/services/{product-detail,catalog-reader}.ts`, `modules/catalog/routes/public-catalog.ts`, `modules/catalog/index.ts` · **Quy mô:** M

### UI-C2: Trang sản phẩm
**Mô tả:** Gallery ảnh đổi theo màu, chọn biến thể, giá và tồn kho theo biến thể, bảng thông số chia nhóm, mô tả chi tiết (đổi tài liệu JSON thành thẻ React), nút thêm vào so sánh. Nút thêm giỏ ở trạng thái tắt tới khi có `commerce`. Chừa vị trí cho khối gợi ý và tóm tắt review.
**Tiêu chí nghiệm thu:**
- [ ] Chọn biến thể khác thì giá, giá niêm yết, tồn kho và ảnh đổi đúng
- [ ] Tiêu đề cấp 1, 2, 3 của mô tả hiện thành thẻ H2, H3, H4; trang chỉ có đúng một H1
- [ ] Chữ đậm và chữ nghiêng hiện đúng
- [ ] Tài liệu chứa nút lạ không hiện nội dung của nút đó và không chạy script (test với payload `script` và `onerror`)
- [ ] Quy tắc ESLint chặn `dangerouslySetInnerHTML` trong `features/catalog/`; fixture lỗi chứng minh quy tắc hoạt động
- [ ] Sản phẩm `discontinued` hiện "Ngừng kinh doanh" và không cho chọn mua
- [ ] Không cuộn ngang ở viewport 375 px
**Kiểm tra:** test component; Playwright trên `pnpm dev`
**Phụ thuộc:** CAT-10, UI-C1, CAT-13 · **File:** `features/catalog/{product-page,variant-picker,spec-table,product-gallery,description-view}.tsx`, `eslint.config.js` · **Quy mô:** M

### UI-C3: Trang so sánh
**Mô tả:** Danh sách so sánh lưu ở `localStorage`, tối đa 4 sản phẩm cùng danh mục. Bảng thông số đặt cạnh nhau, đánh dấu dòng khác nhau.
**Tiêu chí nghiệm thu:**
- [ ] Thêm sản phẩm thứ 5 hoặc khác danh mục thì hiện thông báo, không thêm
- [ ] Dòng có giá trị khác nhau được đánh dấu bằng cả màu và chữ, không chỉ bằng màu
- [ ] `localStorage` không dùng được thì trang vẫn hoạt động trong phiên
**Kiểm tra:** test component
**Phụ thuộc:** CAT-10, UI-C2 · **File:** `features/catalog/{compare-page,use-compare-list}.tsx`, `app.tsx` · **Quy mô:** S

### CAT-11: Thẻ meta SEO
**Mô tả:** Hàm `renderPageMeta(template, meta)` chèn `title`, `description`, Open Graph (`og:title`, `og:description`, `og:image`, `og:url`) vào `index.html`. Route trả HTML cho `/products/:slug` và `/categories/:slug`. Bước gắn vào server production chờ quyết định deploy.
**Tiêu chí nghiệm thu:**
- [ ] Tên và mô tả sản phẩm được escape HTML trong thẻ meta
- [ ] Slug không tồn tại trả 404 với thẻ meta mặc định
- [ ] Slug cũ trả 301 giống API
**Kiểm tra:** unit `renderPageMeta`; integration route bằng Supertest
**Phụ thuộc:** CAT-10 · **File:** `modules/catalog/services/page-meta.ts`, `modules/catalog/routes/page-meta.ts` · **Quy mô:** S

### CAT-12: Dữ liệu seed và đo hiệu năng
**Mô tả:** `pnpm db:seed` tạo khoảng 30 sản phẩm thật trong 5 danh mục: Laptop, Điện thoại, Tai nghe, Màn hình, Phụ kiện. Có sản phẩm nhiều biến thể, hết hàng và ngừng kinh doanh. Script đo p95 sinh 500 sản phẩm trên DB riêng.
**Tiêu chí nghiệm thu:**
- [ ] Chạy seed 2 lần không tạo bản ghi trùng
- [ ] Mọi sản phẩm `active` trong seed qua được `checkPublishable`
- [ ] Mọi thay đổi tồn kho trong seed đi qua `applyStockMovement`
- [ ] Mô tả của sản phẩm seed được tạo bằng `plainTextToDescription` và qua được schema mô tả
- [ ] Script đo báo p95 của `GET /api/products` dưới 300 ms với 500 sản phẩm trên máy dev
**Kiểm tra:** chạy seed trên DB sạch; chạy script đo và ghi kết quả vào task
**Phụ thuộc:** CAT-10 · **File:** `modules/catalog/data/seed.ts`, `prisma/seed.ts`, `scripts/catalog-benchmark.ts` · **Quy mô:** M

### Checkpoint C3
- [ ] Khách lọc, xem chi tiết, so sánh trên dữ liệu seed
- [ ] Review với người dùng

---

## Phase 4: E2E

### E2E-C1: Luồng E2E của catalog
**Mô tả:** Playwright trên DB `pickory_e2e`: admin tạo danh mục có bộ thuộc tính, tạo sản phẩm 2 tùy chọn, soạn mô tả có tiêu đề và chữ đậm, tải ảnh qua provider giả, đăng bán. Khách lọc thấy sản phẩm, chọn biến thể thấy đúng giá, mô tả hiện đúng định dạng.
**Tiêu chí nghiệm thu:**
- [ ] `pnpm test:e2e` chạy sạch cả luồng identity cũ và luồng catalog mới
- [ ] Chạy lại lần hai vẫn PASS, không dựa vào dữ liệu lần trước
**Kiểm tra:** `pnpm test:e2e` hai lần liên tiếp
**Phụ thuộc:** UI-C1, UI-C2, UI-C5, UI-C6, UI-C8, CAT-07 · **File:** `e2e/catalog.spec.ts`, `apps/backend/scripts/e2e-start.ts` · **Quy mô:** S

---

## Checkpoint cuối
- [ ] Mọi dòng Traceability trong `plan-catalog.md` đạt
- [ ] Coverage `services/*.ts` và `domain/*.ts` của `catalog` ít nhất 80% dòng
- [ ] `pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `shipping` hoặc `commerce`
