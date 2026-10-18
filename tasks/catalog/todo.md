# Task List: catalog

Plan và quyết định thiết kế: `tasks/catalog/plan.md`. Spec: `spec/SPEC-catalog.md`. Quy ước chung: `spec/SPEC.md`, `AGENTS.md`.

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
- [x] Chữ ký khớp giá trị tính tay với secret giả; response không chứa `CLOUDINARY_API_SECRET`
- [x] Chữ ký giới hạn định dạng JPG, PNG, WebP; API từ chối ảnh trên 5 MB hoặc sai định dạng khi gắn vào sản phẩm
- [x] Ảnh thứ 11 của một sản phẩm bị từ chối
- [x] Bỏ ảnh tạo 1 job; job lỗi thì tăng số lần thử và hẹn giờ thử lại; quá 8 lần thì dừng và ghi log lỗi
- [x] Không test nào gọi Cloudinary thật
**Kiểm tra:** unit chữ ký và backoff; integration API và job với provider giả
**Phụ thuộc:** CAT-05 · **File:** `modules/catalog/services/{images,image-deletion}.ts`, `modules/catalog/data/cloudinary.ts`, `shared/config.ts`, `server.ts` · **Quy mô:** M
**Quyết định (2026-10-07):**
- Cloudinary không giới hạn kích thước file qua chữ ký (tài liệu "Upload Presets"). Người dùng chọn cách backend kiểm lại khi gắn ảnh.
- Câu tiêu chí thứ hai được sửa theo quyết định này. Câu cũ: "Chữ ký giới hạn định dạng JPG, PNG, WebP và kích thước 5 MB".
- Ảnh gửi qua trường `images` của `POST` và `PATCH` sản phẩm. Spec không có endpoint ảnh riêng.

**File (.NET):**
- `apps/backend/src/Pickory.Modules.Catalog/Images/*`
- `apps/backend/src/Pickory.Modules.Catalog/Features/Uploads/UploadSignatureEndpoint.cs`
- `apps/backend/src/Pickory.Modules.Catalog/Features/Products/{ProductImageSync,ProductManagementHandler,ProductModels,ProductValidators}.cs`
- `apps/backend/src/Pickory.Shared/Configuration/AppConfig.cs`, `apps/backend/src/Pickory.Api/Program.cs`, `.env.example`
- `apps/backend/tests/Pickory.UnitTests/Modules/Catalog/Images/*`, `apps/backend/tests/Pickory.UnitTests/Shared/Configuration/AppConfigCloudinaryTests.cs`
- `apps/backend/tests/Pickory.IntegrationTests/Api/Catalog/ProductImageTests.cs`, `apps/backend/tests/Pickory.IntegrationTests/Database/Catalog/ImageDeletionProcessorTests.cs`

**Bằng chứng (2026-10-07):** Unit test `CloudinarySignatureTests` dùng ví dụ của tài liệu Cloudinary: secret `abcd` cho chữ ký `bfd09f95f331f558cbd1320e67aa8d488770583e`.
Integration test `ProductImageTests` đạt 19/19: chữ ký, 403, ảnh thứ 11, ảnh 5 MB và trên 5 MB, GIF, ảnh không tồn tại, alt, SKU, job khi bỏ ảnh.
Integration test `ImageDeletionProcessorTests` đạt 5/5: xóa xong, lỗi lần đầu chờ 1 phút, lỗi lần 7 còn thử, lỗi lần 8 dừng và ghi log Error.
Factory của test luôn thay kho ảnh bằng `FakeImageStorage`. Test `TestHost_UsesFakeImageStorage` kiểm điều này.
Sáu đột biến đều làm test đỏ. Backend đạt kiến trúc 3/3, unit 389/389, tích hợp 572/576 (bỏ qua 4). Ma trận quyền có thêm `POST /api/admin/uploads/signature`.

### UI-C6: Tải ảnh và sắp xếp ảnh
**Mô tả:** Trong form sản phẩm: lấy chữ ký, tải ảnh thẳng lên Cloudinary, sắp xếp thứ tự bằng kéo thả và bằng nút lên xuống, gắn ảnh cho màu, sửa alt text.
**Tiêu chí nghiệm thu:**
- [x] File sai định dạng hoặc trên 5 MB bị chặn trước khi tải, có thông báo tiếng Việt
- [x] Sắp xếp làm được bằng bàn phím (nút lên, xuống), không chỉ bằng chuột
- [x] Ảnh có ô alt text bắt buộc
**Kiểm tra:** test component với provider giả; thủ công với Cloudinary thật nếu có khóa
**Phụ thuộc:** CAT-06, UI-C5 · **File:** `features/catalog/admin/{image-manager,use-image-upload}.tsx`, `features/catalog/shared/image-url.ts` · **Quy mô:** S
**Quyết định (2026-10-08):**
- Ảnh gắn màu bằng cách chọn một biến thể đã lưu (`skuId`). Trang sản phẩm hiện ảnh này cho các biến thể cùng màu. Không đổi backend.
- Kho ảnh giả ở dev và E2E chạy ở chế độ dễ dãi. E2E chặn request tới Cloudinary và trả kết quả giả. Test tích hợp vẫn dùng kho giả nghiêm ngặt.
- Phản hồi sản phẩm có `url` cho từng ảnh, để người chỉ có quyền xem cũng thấy ảnh thu nhỏ.

**File (frontend và .NET):**
- `apps/frontend/src/features/catalog/admin/{image-manager.tsx,image-upload.ts,product-form.tsx,products.css}`
- `apps/frontend/src/features/catalog/shared/image-url.ts`, `apps/frontend/src/shared/icons.tsx`
- `apps/frontend/test/unit/features/catalog/{image-url.test.ts,admin/image-upload.test.ts,admin/image-manager.test.tsx,admin/product-form.test.tsx}`
- `apps/frontend/e2e/catalog-images.spec.ts`
- `apps/backend/src/Pickory.Modules.Catalog/Images/{FakeImageStorage,ImageStorageRegistration}.cs`, `apps/backend/src/Pickory.Modules.Catalog/Features/Products/{ProductModels,ProductManagementHandler}.cs`

**Bằng chứng (2026-10-08):** Vitest đạt 306/306, gồm test của `image-upload`, `image-manager`, `image-url` và 2 test ảnh của form sản phẩm.
Playwright đạt 8/8 ở chế độ có giao diện và chế độ ẩn. Spec `catalog-images.spec.ts` chặn GIF và file trên 5 MB mà không gọi Cloudinary.
Spec này cũng kiểm alt bắt buộc, sắp xếp bằng phím Enter trên nút "Đưa ảnh 2 lên", thứ tự giữ sau khi tải lại, và 375 px không tràn trang.
Bốn đột biến đều làm test đỏ. Test focus khi đưa ảnh xuống cuối được thêm sau khi đột biến bỏ phần đưa focus vẫn xanh.
Backend đạt kiến trúc 3/3, unit 391/391, tích hợp 572/576 (bỏ qua 4).

### CAT-07: Đổi trạng thái và xóa cứng
**Mô tả:** `POST /api/admin/products/:id/status` và `DELETE /api/admin/products/:id` (`products:write`). Dùng `checkPublishable` và `canTransition` của CAT-02. Ghi audit và phát `onProductChanged` với `change: 'status'` hoặc `'deleted'`.
**Tiêu chí nghiệm thu:**
- [x] Chuyển `active` khi thiếu ảnh, thiếu thông số bắt buộc hoặc SKU bật thiếu cân nặng, kích thước trả 422 `PRODUCT_NOT_PUBLISHABLE` kèm danh sách lý do
- [x] Mỗi lần đổi trạng thái có đúng 1 dòng audit `catalog.product.status_change`
- [x] Xóa cứng sản phẩm có SKU từng có movement `order_reserve` trả 409 `PRODUCT_HAS_ORDERS`
- [x] Xóa cứng thành công thì tạo job xóa ảnh cho mọi ảnh của sản phẩm
- [x] Xóa cứng sản phẩm chỉ có movement `manual` hoặc `return` thành công; movement của sản phẩm bị xóa trong cùng transaction (Decision 18)
- [x] `checkPublishable` dùng `validatePrice` cho từng SKU bật (giá nguyên dương, `compareAtPrice` hợp lệ); mỗi lý do lỗi của SKU ghi mã SKU
**Kiểm tra:** integration test
**Phụ thuộc:** CAT-06 · **File:** `modules/catalog/services/product-status.ts`, `modules/catalog/routes/admin-products.ts` · **Quy mô:** S
**File (.NET):** `apps/backend/src/Pickory.Modules.Catalog/Products/{ProductPublishing,SkuPublishableInput}.cs`, `apps/backend/src/Pickory.Modules.Catalog/Features/Products/{ProductModels,ProductValidators,ProductManagementHandler,ProductImageSync,ProductsEndpoints}.cs`, `apps/backend/tests/Pickory.UnitTests/Modules/Catalog/Products/ProductPublishingTests.cs`, `apps/backend/tests/Pickory.IntegrationTests/Api/Catalog/ProductStatusTests.cs`, `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`, `spec/SPEC-catalog.md`

**Quyết định (2026-10-08):**
- Chuyển trạng thái mà `CanTransition` từ chối (ví dụ `active` về `draft`) trả 409 `INVALID_STATUS_TRANSITION`.
- Chuyển sang đúng trạng thái hiện tại trả 200, không ghi audit và không phát sự kiện, vì không có thay đổi.
- `PRODUCT_NOT_PUBLISHABLE` trả danh sách lý do trong `details` của lỗi, cùng dạng với `SPECS_INVALID`.

**Bằng chứng (2026-10-08):**
- `dotnet build apps/backend/Pickory.sln` đạt 0 cảnh báo và 0 lỗi.
- `dotnet test apps/backend/Pickory.sln` xanh: kiến trúc 3/3, unit 398/398, tích hợp 606 đạt và 4 bỏ qua (test gọi AI thật).
- Unit: `ProductPublishingTests.cs` có 28 test, gồm 7 test mới cho `ValidatePrice` và mã SKU trong lý do.
- Tích hợp: `ProductStatusTests.cs` có 24 test cho đổi trạng thái và xóa cứng. Ma trận quyền có thêm 2 endpoint, đạt 160 test.
- Phép thử đột biến: 13/13 đột biến bị test bắt (bỏ kiểm ảnh, bỏ kiểm `order_reserve`, bỏ job xóa ảnh, audit hai lần, phát sự kiện hai lần, bỏ mã SKU, bỏ khóa ghi, bỏ bảng chuyển trạng thái, bỏ nhánh cùng trạng thái, bỏ xóa movement, kiểm giá SKU tắt, bỏ `compareAtPrice`, bỏ sự kiện `Deleted`). Lần thử đầu của nhánh cùng trạng thái lỗi biên dịch, nên đổi cách đột biến rồi chạy lại.
- Test khóa ghi giữ khóa ở kết nối riêng và chờ 1,5 giây. Mốc chờ chỉ ảnh hưởng độ nhạy, không làm test đỏ giả.
- Script kiểm tra comment báo 0 vi phạm. Các file đã đổi không chứa em dash hoặc en dash.

### CAT-08: Tồn kho, giữ hàng và trả hàng
**Mô tả:** `applyStockMovement(tx, movement)` là hàm duy nhất đổi `stock`. `POST /api/admin/skus/:id/adjust-stock` (`inventory:write`, bắt buộc lý do). `GET /api/admin/skus/:id/movements` và `GET /api/admin/inventory/low-stock` (`products:read`). `stock.reserve` và `stock.release` cho `commerce`. Phát `onStockChanged` sau commit.
**Tiêu chí nghiệm thu:**
- [x] 20 `reserve` đồng thời cho SKU còn 5 cái: đúng 5 thành công, tồn kho về 0, không âm
- [x] `reserve` thiếu 1 SKU thì không giữ SKU nào, trả `shortages` đúng số
- [x] Gọi `reserve` hoặc `release` 2 lần cùng `orderId` không đổi tồn kho lần thứ 2
- [x] `reserve` và `release` nhận `tx` của bên gọi kèm collector; `onStockChanged` không phát khi transaction của bên gọi rollback, phát sau khi bên gọi `flush()` (Decision 4)
- [x] `stock` của mọi SKU bằng tổng `delta` của movement sau mỗi file test
- [x] Điều chỉnh tay thiếu lý do trả 400; thành công tạo đúng 1 dòng audit `catalog.sku.stock_adjust`
- [x] Tồn kho đổi thì `minPrice` và `inStock` của sản phẩm cập nhật trong cùng transaction
- [x] `sales` gọi `adjust-stock` nhận 403
**Kiểm tra:** integration test, gồm test đồng thời bằng `Promise.all` trên DB thật
**Phụ thuộc:** CAT-05 · **File:** `modules/catalog/services/inventory.ts`, `modules/catalog/data/stock.ts`, `modules/catalog/routes/admin-inventory.ts`, `modules/catalog/index.ts` · **Quy mô:** M
**File (.NET):** `apps/backend/src/Pickory.Shared/Events/DomainEventCollector.cs` (chuyển từ `CatalogEventCollector`), `apps/backend/src/Pickory.Modules.Catalog.Contracts/Stock/{IStockService,StockItem,StockShortage,ReserveResult,StockCallerTransaction}.cs`, `apps/backend/src/Pickory.Modules.Catalog/Stock/{StockLedger,StockService,StockRequests,StockShortages,MovementTypeNames}.cs`, `apps/backend/src/Pickory.Modules.Catalog/Features/Inventory/{AdjustStockRequest,AdjustStockRequestValidator,InventoryHandler,InventoryModels,InventoryEndpoints}.cs`, `apps/backend/src/Pickory.Modules.Catalog/Features/Products/ProductManagementHandler.cs`, `apps/backend/src/Pickory.Modules.Catalog/CatalogModule.cs`, `apps/backend/tests/Pickory.UnitTests/Modules/Catalog/{Stock,Inventory}/`, `apps/backend/tests/Pickory.IntegrationTests/Api/Catalog/{StockServiceTests,InventoryTests,ProductStockConcurrencyTests}.cs`, `apps/backend/tests/Pickory.IntegrationTests/Infrastructure/{StockInvariant,StockTestData,LockWait}.cs`, `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`, `spec/SPEC-catalog.md`

**Quyết định (2026-10-08):**
- `IStockService` nằm trong `Pickory.Modules.Catalog.Contracts` (thư mục `Stock/`), cùng `StockItem`, `ReserveResult`, `StockShortage`.
- Bên gọi truyền `StockCallerTransaction(DbTransaction, DomainEventCollector)`, có thể null. Khi null, catalog tự mở transaction và tự phát sự kiện sau commit.
- `CatalogEventCollector` chuyển thành `DomainEventCollector` trong `Pickory.Shared`, không đổi hành vi.
- Đầu `ReserveAsync` và `ReleaseAsync` lấy `pg_advisory_xact_lock` theo `orderId`. Không dựa vào việc bắt lỗi unique.
- Thiếu hàng thì ledger kiểm đủ hàng trước khi ghi, nên không để lại thay đổi nào. Không dùng savepoint.
- `StockLedger` là nơi duy nhất đổi `stock`: khóa dòng sản phẩm rồi dòng SKU theo `id`, UPDATE có điều kiện, ghi movement, tính lại `minPrice` và `inStock`.
- Thứ tự khóa duy nhất cho mọi đường ghi: dòng `products` trước, dòng `skus` sau. Sửa sản phẩm (`PATCH`) và xóa cứng cũng khóa dòng `products` trước khi đọc, để không ghi đè `minPrice` bằng tồn kho cũ.
- `reserve` lại cùng `orderId` sau `release` nhận `Ok` nhưng không giữ hàng (Decision 7). `commerce` dùng `orderId` mới khi đặt lại.
- Điều chỉnh tay nhận `delta`, `reason`, `type` (`manual` hoặc `return`). Làm tồn kho âm trả 409 `INSUFFICIENT_STOCK`. SKU lạ trả 404 `SKU_NOT_FOUND`.
- Audit `catalog.sku.stock_adjust`: `before` là `{ stock }`, `after` là `{ stock, delta, type, reason }`.
- `movements` và `low-stock` phân trang theo `PageQuery`. `low-stock` dùng điều kiện của bộ lọc `LowStock` hiện có.
- Mỗi SKU đổi tồn kho phát đúng 1 `StockChangedEvent` sau commit. Lời gọi lặp không đổi gì thì không phát.

**Bằng chứng (2026-10-08):**
- `dotnet build apps/backend/Pickory.sln` đạt 0 cảnh báo và 0 lỗi.
- `dotnet test apps/backend/Pickory.sln` xanh: kiến trúc 3/3, unit 425/425, tích hợp 666 đạt và 4 bỏ qua (test gọi AI thật).
- Test 20 `reserve` đồng thời cho SKU còn 5 cái chạy riêng 5 lần liên tiếp, cả 5 lần đạt: đúng 5 thành công, tồn kho về 0, đúng 5 movement `order_reserve`.
- Bất biến `stock` bằng tổng `delta` được kiểm trong `DisposeAsync` của mọi lớp test tích hợp catalog (10 lớp), trên toàn DB test.
- Phép thử đột biến: 13/15 điểm bị test bắt. Hai đột biến sống sót đều tương đương về hành vi: bỏ điều kiện `stock + @delta >= 0` trong UPDATE (dòng SKU đã khóa và đã kiểm đủ hàng, nên điều kiện chỉ là lớp dự phòng cùng `CHECK` của DB), và bỏ khóa `products` trong `DeleteAsync` (khóa ngoại `RESTRICT` vẫn chặn xóa và API vẫn trả 409).
- Review lượt 1: test `Patch_WhileReserveSellsOutCheapVariant_KeepsMinPriceOfInStockVariant` đỏ 3 lần trên code cũ (`minPrice` 150000 thay vì 200000), xanh sau khi thống nhất thứ tự khóa. 4 test trong `ProductStockConcurrencyTests` chạy 5 lần liên tiếp đều đạt.
- Script kiểm tra comment báo 0 vi phạm. Các file đã đổi không chứa em dash hoặc en dash.

### UI-C7: Màn tồn kho
**Mô tả:** Danh sách SKU sắp hết hàng (ngưỡng 5), form điều chỉnh có ô lý do bắt buộc, bảng lịch sử movement của SKU.
**Tiêu chí nghiệm thu:**
- [x] Điều chỉnh thành công thì tồn kho và lịch sử cập nhật không cần tải lại trang
- [x] Không có `inventory:write` thì không thấy form điều chỉnh
**Kiểm tra:** test component; Playwright trên `pnpm dev`
**Phụ thuộc:** CAT-08, UI-C5 · **File:** `features/catalog/admin/{inventory-page,adjust-stock-dialog}.tsx`, `app.tsx` · **Quy mô:** S

**Quyết định (2026-10-08):**
- Hai route: `/admin/inventory` (danh sách SKU sắp hết hàng, có phân trang) và `/admin/products/:productId/skus/:skuId/inventory` (trang tồn kho một SKU, lấy SKU từ `GET /api/admin/products/:productId` và lịch sử từ `GET /api/admin/skus/:skuId/movements`).
- Lối vào trang SKU: liên kết ở mỗi dòng danh sách sắp hết hàng, và liên kết "Tồn kho" cho mỗi SKU đã lưu trong bảng biến thể của form sản phẩm. SKU chưa lưu không có liên kết.
- Menu: thêm mục "Tồn kho" vào nhóm "Sản phẩm", quyền `products:read`, icon mới `inventory` cùng kiểu nét. Trang SKU đánh dấu mục "Tồn kho" thay cho "Sản phẩm".
- Form điều chỉnh là dialog `adjust-stock-dialog.tsx`: loại (Điều chỉnh hoặc Khách trả hàng), số lượng thay đổi, lý do tối đa 500 ký tự. Kiểm ở client cùng luật backend, lỗi dưới ô, lỗi 409 hiện trong dialog và dialog không đóng, nút gửi khóa khi chờ API, hiện tồn kho hiện tại và sau điều chỉnh.
- Sau khi điều chỉnh: đóng dialog, trả focus về nút "Điều chỉnh tồn kho", ghi số tồn và movement mới vào cache, làm mới các query liên quan, hiện thông báo `role="status"`. Không tải lại trang.
- Không có `inventory:write` thì không render nút và dialog. Trang vẫn hiện số tồn và lịch sử cho người có `products:read`.
- Bảng lịch sử: thời điểm giờ Việt Nam, loại bằng nhãn tiếng Việt (4 loại), thay đổi có dấu `+` hoặc `-`, lý do, mã đơn hoặc mã người làm rút gọn 8 ký tự. Mới nhất trước, có phân trang.

**File:**
- `apps/frontend/src/features/catalog/admin/{inventory-logic.ts,adjust-stock-dialog.tsx,inventory-page.tsx,sku-inventory-page.tsx,inventory.css,variant-table.tsx,product-form.tsx}`
- `apps/frontend/src/app.tsx`, `apps/frontend/src/shared/icons.tsx`, `apps/frontend/src/shared/styles/components.css`
- `apps/frontend/src/features/identity/admin/{admin-navigation.ts,admin-layout.tsx}`
- `apps/frontend/test/unit/features/catalog/admin/{inventory-logic.test.ts,adjust-stock-dialog.test.tsx,inventory-page.test.tsx,sku-inventory-page.test.tsx,variant-table.test.tsx}`, `apps/frontend/test/unit/features/identity/admin/admin-layout.test.tsx`
- `apps/frontend/e2e/{catalog-inventory.spec.ts,environment.ts,staff-invite.spec.ts}`

**Bằng chứng (2026-10-08):**
- `pnpm typecheck` và `pnpm lint` đạt 0 lỗi. `pnpm test` đạt 349/349 (30 file).
- `env -u NODE_ENV pnpm build` thành công. Bundle chính `index-*.js` là 401.76 kB (gzip 116.91 kB), form sản phẩm `product-form-*.js` là 436.85 kB (gzip 132.07 kB), `products-page-*.js` là 4.78 kB. Hai trang tồn kho nằm trong bundle chính vì nhỏ, không kéo Tiptap.
- Playwright `--headed`: `catalog-inventory.spec.ts` đạt, cả bộ 9/9 đạt. Chế độ ẩn cũng 9/9 đạt. Console 0 lỗi và 0 cảnh báo, ngoại trừ một dòng "Failed to load resource" của phản hồi 409 mà spec chủ động gây ra (spec đếm và khẳng định đúng 1 dòng).
- Phép thử đột biến: 10/10 điểm bị test bắt (bỏ kiểm lý do rỗng, hiện nút khi thiếu quyền, bỏ ghi lịch sử vào cache, bỏ làm mới lịch sử, bỏ chặn gửi hai lần, bỏ khóa nút gửi, SKU chưa lưu có liên kết, mục menu sai, mất dấu `+`, liên kết danh sách sai SKU).
- Ảnh chụp 1280 px và 375 px của danh sách, trang SKU và dialog. Ở 375 px trang không tràn ngang, bảng cuộn trong khung của nó.
- Script kiểm tra comment báo 0 vi phạm. Các file đã đổi không chứa em dash hoặc en dash.

### UI-C5b: Đổi trạng thái và xóa sản phẩm trên giao diện
**Mô tả:** Form sửa sản phẩm có khối trạng thái. Khối này hiện trạng thái hiện tại và nút cho từng trạng thái chuyển được. Nút xóa mở hộp thoại xác nhận. Giao diện gọi 2 endpoint của CAT-07. Task thêm ngày 2026-10-08: Checkpoint C2 cần đăng bán trên giao diện, nhưng UI-C5 không có phần này.
**Tiêu chí nghiệm thu:**
- [x] Chỉ hiện nút cho trạng thái chuyển được từ trạng thái hiện tại, theo bảng `CanTransition` của backend
- [x] API trả 422 `PRODUCT_NOT_PUBLISHABLE` thì hiện từng lý do trong `details`; sản phẩm giữ trạng thái cũ
- [x] Đổi trạng thái thành công thì nhãn trạng thái cập nhật mà không tải lại trang
- [x] Xóa cần xác nhận; xóa thành công thì về danh sách sản phẩm; 409 `PRODUCT_HAS_ORDERS` hiện thông báo tiếng Việt
- [x] Không có `products:write` thì không thấy nút đổi trạng thái và nút xóa
**Kiểm tra:** test component; Playwright `--headed`
**Phụ thuộc:** CAT-07, UI-C5 · **File:** `features/catalog/admin/{product-status-panel,delete-product-dialog}.tsx`, `product-form.tsx` · **Quy mô:** S

**Quyết định (2026-10-08):**
- Khối trạng thái chỉ có ở trang sửa `/admin/products/:id`. Trang tạo mới không có, vì sản phẩm mới luôn là `draft`.
- Nhãn và màu badge của trạng thái gom vào `product-status.ts`, dùng chung với danh sách sản phẩm.
- Bảng chuyển trạng thái ở frontend chép từ `AllowedTransitions` của `ProductPublishing.cs`, bỏ cặp chuyển sang chính nó. Unit test liệt kê đủ 4 trạng thái.
- Nhãn nút theo trạng thái đích: "Đăng bán" (từ `draft`), "Bán lại" (từ `hidden` hoặc `discontinued`), "Ẩn sản phẩm", "Ngừng kinh doanh".
- Form theo dõi thay đổi chưa lưu bằng ảnh chụp các ô nhập so với ảnh chụp lúc tải hoặc lúc lưu. Có thay đổi thì khóa nút trạng thái và hiện "Lưu thay đổi trước khi đổi trạng thái."
- Lỗi 422 hiện thông báo của API và danh sách lý do trong `role="alert"`. `details` của lỗi này là mảng chuỗi, khác dạng `{ key, message }` của `SPECS_INVALID`. Sản phẩm giữ trạng thái cũ.
- Lỗi 409 và lỗi mạng hiện thông báo tiếng Việt của API qua `toCatalogErrorMessage`.
- Đổi trạng thái thành công: ghi `ProductResponse` vào cache `['admin-product', id]`, làm mới `['admin-products']`, hiện thông báo `role="status"`.
- Xóa: nút "Xóa sản phẩm" kiểu nguy hiểm, tách khỏi nút trạng thái. Hộp thoại nêu tên sản phẩm và nói không thể hoàn tác, focus ở nút Hủy. Thành công (204) thì về `/admin/products` và làm mới danh sách. Lỗi 409 hiện trong hộp thoại, hộp thoại không đóng.
- Hộp thoại xóa là `div` riêng, không dùng `AdminFormDialog`, vì khung đó có thẻ `form` và sẽ lồng trong form sản phẩm.
- Không có `products:write` thì không render nút trạng thái và nút xóa. Nhãn hiện tại vẫn hiện.
- Nút đang chờ API bị khóa cùng các nút khác trong khối, kể cả nút xóa.
- `apiRequest` coi phản hồi 204 là thành công và trả `undefined`, vì endpoint xóa không có thân phản hồi.

**File:**
- `apps/frontend/src/features/catalog/admin/{product-status.ts,product-status-panel.tsx,delete-product-dialog.tsx,product-form.tsx,products-page.tsx,products.css}`, `apps/frontend/src/shared/api.ts`
- `apps/frontend/test/unit/features/catalog/product-status.test.ts`, `apps/frontend/test/unit/features/catalog/admin/{product-status-panel,product-form-page-status,product-form}.test.tsx`, `apps/frontend/test/unit/shared/api.test.ts`
- `apps/frontend/e2e/{catalog-product-status.spec.ts,environment.ts}`

**Bằng chứng (2026-10-08):**
- `pnpm typecheck` và `pnpm lint` đạt 0 lỗi. `pnpm test` đạt 419/419 (35 file).
- `env -u NODE_ENV pnpm build` thành công. Bundle chính `index-*.js` 401.89 kB (gzip 116.95 kB), `product-form-*.js` 440.77 kB (gzip 133.09 kB), `products-page-*.js` 4.64 kB, chunk mới `product-status-*.js` 0.70 kB.
- Playwright `--headed`: `catalog-product-status.spec.ts` đạt, cả bộ 10/10 đạt. Chế độ ẩn cũng 10/10 đạt. Console 0 lỗi và 0 cảnh báo, ngoại trừ 2 dòng "Failed to load resource" của phản hồi 422 mà spec chủ động gây ra (spec đếm và khẳng định đúng 2 dòng).
- Phép thử đột biến: 13/13 điểm bị test bắt (cho `active` về `draft`, bỏ danh sách lý do 422, hiện nút khi thiếu quyền, không khóa nút khi chưa lưu, xóa không qua hộp thoại, luôn coi là đã lưu, bỏ ghi cache, không mở khóa sau khi lưu, bỏ xử lý 204). Sau review lượt 1, lệnh chặn bấm hai lần dùng cờ `useRef` (bấm hai lần trong cùng lượt vẽ chỉ gọi API một lần), và đột biến bỏ cờ hoặc bỏ mở lại cờ đều bị bắt. Đột biến luôn coi là đã lưu cũng làm spec E2E đỏ ở chế độ `--headed`.
- Ảnh chụp 1280 px và 375 px của khối trạng thái, lỗi 422 và hộp thoại xóa. Ở 375 px trang không tràn ngang.
- Script kiểm tra comment báo 0 vi phạm. Các file đã đổi không chứa em dash hoặc en dash.

### Checkpoint C2
- [ ] Admin tạo danh mục, sản phẩm 2 tùy chọn, tải ảnh (provider giả), đăng bán trên `pnpm dev`
  - Đã kiểm bằng E2E `catalog-checkpoint-c2.spec.ts`, Cloudinary giả lập bằng `page.route`. Chưa kiểm trên `pnpm dev`: thiếu khóa Cloudinary. Người dùng chọn cách này ngày 2026-10-08.
- [x] Ma trận quyền có mọi endpoint back-office của `catalog`
- [x] Review với người dùng

**Bằng chứng (2026-10-08):**

- Playwright có giao diện: spec C2 đạt 1/1, cả bộ đạt 11/11. Chế độ ẩn đạt 11/11. Console của spec C2 có 0 lỗi và 0 cảnh báo.
- `EndpointDataSource` đối chiếu 17 route quản trị catalog với ma trận. Không thiếu route; test đột biến xóa `POST /api/admin/products` báo thiếu đúng route.
- `dotnet build` đạt 0 cảnh báo và 0 lỗi. Ma trận quyền đạt 176/176 test.
- `dotnet test` đạt 3 test kiến trúc, 425 test đơn vị, 667 test tích hợp; 4 test tích hợp bỏ qua.
- `pnpm typecheck`, `pnpm lint` thành công. Script kiểm tra comment báo 0 vi phạm.
- Người dùng xác nhận review Checkpoint C2 ngày 2026-10-08.

---

## Phase 3: Đọc công khai và giao diện khách

### CAT-09: Danh sách sản phẩm có lọc và facets
**Mô tả:** `GET /api/products` theo bảng tham số trong spec và Decision 11, 12 của `plan.md`. Chỉ trả sản phẩm `active` và `discontinued`. `catalogReader.listProducts` dùng cùng bộ lọc.
**Tiêu chí nghiệm thu:**
- [x] Lọc kết hợp danh mục, thương hiệu, khoảng giá, `inStock` và 3 thuộc tính trả đúng tập sản phẩm
- [x] `facets` đếm đúng theo kiểu loại trừ chính nó
- [x] Thuộc tính không `filterable` hoặc không tồn tại trả 400 `UNKNOWN_FILTER`
- [x] `pageSize` trên 48 trả 400; ba kiểu `sort` đúng thứ tự
- [x] Danh mục cha trả cả sản phẩm của danh mục con
**Quyết định (2026-10-08):**
1. Code nằm ở `Pickory.Modules.Catalog/Features/Storefront/`, tách thành route, bộ đọc tham số, dịch vụ tìm kiếm.
2. `catalogReader.listProducts` chưa có interface trong `Contracts`. `ProductSearchService.SearchAsync` dùng lại được cho task sau.
3. Khách chỉ thấy `active` và `discontinued`. `draft` và `hidden` không vào danh sách và không vào facets.
4. `category` là slug và lấy cả danh mục con. Slug lạ trả 404 `CATEGORY_NOT_FOUND`. `brand` là danh sách slug, slug lạ không gây lỗi.
5. `priceMin` và `priceMax` là số nguyên không âm so với `MinPrice`. `inStock` chỉ nhận `true` hoặc `false`. `sort` mặc định `newest`, mọi kiểu thêm `Id` làm khóa phụ.
6. `page` từ 1, `pageSize` từ 1 đến 48 (mặc định 24). Độ lệch tràn `int` trả 400.
7. Tập thuộc tính lọc được là thuộc tính `filterable` của danh mục và các danh mục cha. Không có `category` thì mọi `attr.*` trả 400 `UNKNOWN_FILTER`, `details` nêu tên khóa sai.
8. Response gồm `items`, `page`, `pageSize`, `total`, `facets`. Mỗi sản phẩm có tóm tắt và ảnh đại diện ở vị trí đầu.
9. Facets tính trong DB: một truy vấn cho hãng và một truy vấn cho mỗi thuộc tính, mỗi nhóm bỏ bộ lọc của chính nó. Lọc, sắp xếp, phân trang cũng chạy trong DB.
10. Route công khai, không thêm dòng vào ma trận quyền back-office.

**File:** `apps/backend/src/Pickory.Modules.Catalog/Features/Storefront/{PublicProductsEndpoints,ProductSearchQuery,AttributeFilterReader,AttributeFilter,UnknownFilterException,ProductFilterExpressions,ProductSearchService,StorefrontModels,ProductSort}.cs`, `CatalogModule.cs`; test: `Pickory.UnitTests/Modules/Catalog/Storefront/`, `Pickory.IntegrationTests/Api/Catalog/PublicProductListTests.cs`

**Bằng chứng (2026-10-08):**

- `dotnet build` đạt 0 cảnh báo và 0 lỗi.
- `dotnet test` toàn solution chạy 2 lần, cả hai đạt 3 test kiến trúc, 488 test đơn vị, 721 test tích hợp; 4 test tích hợp bỏ qua, 0 lỗi. Test mới: 63 đơn vị và 54 tích hợp.
- Phép thử đột biến: 22 điểm đều bị test bắt.
  - Lượt đầu có 15 điểm, gồm:
    - bỏ loại `draft` và `hidden`;
    - facets áp bộ lọc của chính nó;
    - `pageSize` 49 và bỏ khóa phụ `Id`;
    - bỏ danh mục con và nhận thuộc tính không `filterable`;
    - đổi chiều sort và bỏ `inStock`;
    - cận số mở và bỏ kiểm khoảng giá.
  - Hai đột biến sống ở lượt đầu: khóa phụ `Id` của `newest` và cận số đóng. Builder đã siết test, và test bắt được cả hai.
  - Lượt review 1 thêm 7 điểm:
    - facets bỏ bộ lọc giá;
    - facets bỏ bộ lọc `inStock`;
    - thứ tự facets `boolean`, `enum`, `text` và hãng;
    - cha mồ côi trong chuỗi danh mục.
- Chưa đo hiệu năng: p95 với 500 sản phẩm thuộc CAT-12.
- Task không đổi giao diện, nên không chạy Playwright.
- Script kiểm tra comment báo 0 vi phạm. Các file đã đổi không chứa em dash hoặc en dash.

**Kiểm tra:** integration test với bộ dữ liệu cố định
**Phụ thuộc:** CAT-08, CAT-07 · **File:** `modules/catalog/services/product-search.ts`, `modules/catalog/data/product-query.ts`, `modules/catalog/routes/public-catalog.ts` · **Quy mô:** M

### UI-C1: Trang danh mục
**Mô tả:** Lưới sản phẩm, bộ lọc sinh từ thuộc tính `filterable`, sắp xếp, phân trang. Trạng thái lọc nằm trên URL. Ảnh dùng URL biến đổi Cloudinary (`f_auto,q_auto`, chiều rộng 200/400/800/1200).
**Tiêu chí nghiệm thu:**
- [x] Mở lại URL đã chia sẻ thì thấy đúng bộ lọc và trang
- [x] Facet hiện số đếm, ví dụ "RAM 16GB (12)"
- [x] Không cuộn ngang ở viewport 375 px; bộ lọc dùng được bằng bàn phím
**Kiểm tra:** test component; Playwright trên `pnpm dev`
**Phụ thuộc:** CAT-09, UI-C6 · **File:** `features/catalog/{category-page,product-filters,product-card}.tsx`, `app.tsx` · **Quy mô:** M

**File:** `apps/frontend/src/features/catalog/customer/{category-page,product-filters,product-card,category-query,facet-labels,product-display,category-tree,range-input,use-product-list,catalog-api}.ts(x)`, `category-page.css`, `apps/frontend/src/app.tsx` (route `/c/:slug`), `apps/frontend/src/shared/icons.tsx`, `apps/frontend/src/features/catalog/shared/image-url.ts`; backend: `Pickory.Modules.Catalog/Images/ProductImageUrl.cs`, `Features/Storefront/{ProductSearchService,StorefrontModels}.cs`, `Features/Products/ProductManagementHandler.cs`; test: `apps/frontend/test/unit/features/catalog/customer/`, `apps/frontend/e2e/catalog-category-page.spec.ts`, `PublicProductListTests.cs`

**Bằng chứng (2026-10-08):**

- Playwright chạy trên Vite dev server (cùng lệnh với `pnpm dev`), với API E2E và DB `pickory_e2e`.
- `GET /api/products` thêm trường `image.url`. Hàm `ProductImageUrl.Build` dùng chung với phản hồi admin.
- Vitest: 46 file, 510 test đạt, gồm 77 test mới của UI-C1. `pnpm typecheck` và `pnpm lint` sạch.
- Spec `catalog-category-page.spec.ts`: 7 test đạt, chạy có giao diện (`--headed`). Bộ E2E ẩn: 18 test đạt. Bộ E2E có giao diện lượt đầu có 2 test ngoài UI-C1 lỗi do máy quá tải, cả hai đạt khi chạy riêng.
- Phép thử đột biến: 8 điểm, đều bị test bắt. Điểm thứ 8 là bỏ bảng tra tên thương hiệu đang chọn có số đếm 0.
- `dotnet test` toàn solution do reviewer chạy lại: 3 test kiến trúc, 488 test đơn vị và 721 test tích hợp đạt; 4 test bỏ qua, 0 lỗi.
- Trong lượt của builder, test Identity `AdminStaffTests.TwoLastAdmins_DemotingEachOtherConcurrentlyKeepsOneAdmin` lỗi 2 lần và đạt khi chạy riêng.

### CAT-10: Chi tiết, slug cũ, so sánh và catalogReader
**Mô tả:** `GET /api/products/:slug`, `GET /api/products/compare?ids=`. Hoàn thiện `catalogReader`: `getProduct`, `getProducts`, `getSkus`, `listForIndexing`, `getAttributeDefinitions`, `countLowStock`.
**Tiêu chí nghiệm thu:**
- [x] Slug cũ trả 301 sang slug mới
- [x] Sản phẩm `draft` và `hidden` trả 404; `discontinued` trả 200 có trạng thái để hiện "Ngừng kinh doanh"
- [x] So sánh 4 sản phẩm cùng danh mục trả đủ thông số; khác danh mục trả 400 `COMPARE_CATEGORY_MISMATCH`; trên 4 sản phẩm trả 400
- [x] `getProducts` giữ thứ tự `ids` và bỏ qua id không hợp lệ
- [x] `listForIndexing` duyệt hết sản phẩm bằng cursor, không lặp, không sót
  - Ghi chú 2026-10-14: `smart-search` đã bỏ, nên hàm này không còn bên dùng. Tiêu chí giữ nguyên vì đã nghiệm thu. Xóa code của hàm ở một task sau.
**Quyết định (2026-10-08):**
1. Route mới nằm trong `PublicProductDetailEndpoints.cs`. `ICatalogReader` và các record nằm trong `Pickory.Modules.Catalog.Contracts/Reading/`. `CatalogReader` đăng ký trong `CatalogModule.cs`.
2. `GET /api/products/{slug}` chỉ nhận slug. Slug hiện tại của sản phẩm khách thấy được trả 200. Slug cũ trả 301 tới slug mới và giữ query string.
3. `draft`, `hidden` và slug lạ cùng trả 404 `PRODUCT_NOT_FOUND`. Slug cũ của sản phẩm khách không thấy cũng trả 404. `discontinued` trả 200 kèm `status`.
4. `GET /api/products/compare?ids=` nhận 1 đến 4 id sau khi bỏ id trùng. Id lạ hoặc khách không thấy bị bỏ qua. Khác danh mục lá trả 400 `COMPARE_CATEGORY_MISMATCH`. Không còn sản phẩm nào thì trả 200 với danh sách rỗng.
5. Thuộc tính hiệu lực dùng chung `CategoryAttributeIndex` cho chi tiết, so sánh và `GetAttributeDefinitionsAsync`. So sánh trả đủ mọi thuộc tính, không chỉ `filterable`.
6. `GetSkusAsync` trả cả SKU tắt và sản phẩm không bán, kèm `Enabled` và `ProductStatus`. Ảnh riêng của SKU thắng, không có thì lấy ảnh đầu của sản phẩm.
7. `ListForIndexingAsync` sắp theo `Id`, cursor là `Id` cuối của trang trước, `pageSize` từ 1 đến 500.
8. `CountLowStockAsync`, danh sách sắp hết hàng và bộ lọc `lowStock` của danh sách sản phẩm quản trị cùng dùng `LowStockExpressions.IsLowStock`.
9. `ListProductsAsync` đi qua `ProductSearchQuery.Read` và `ProductSearchService`, `limit` bị kéo về khoảng 1 đến 48.
10. Route công khai, không thêm dòng vào ma trận quyền back-office.

**File:** `apps/backend/src/Pickory.Modules.Catalog.Contracts/Reading/*.cs`, `apps/backend/src/Pickory.Modules.Catalog/Reading/{CatalogReader,RequestedIdOrder,IndexingPaging,SkuImageChooser}.cs`, `apps/backend/src/Pickory.Modules.Catalog/Features/Storefront/{PublicProductDetailEndpoints,ProductDetailService,ProductDetailAssembler,ProductSummaryReader,ProductComparisonService,CategoryAttributeIndex,CompareIdsReader,AttributeGrouping,CloudinaryImageUrl}.cs`, `Features/Inventory/{LowStockExpressions,InventoryHandler}.cs`, `Features/Products/ProductManagementHandler.cs` (chỉ bộ lọc `lowStock`), `CatalogModule.cs`; test: `Pickory.UnitTests/Modules/Catalog/{Reading,Storefront}/`, `Pickory.IntegrationTests/Api/Catalog/{CatalogReaderTests,PublicProductDetailTests}.cs`, `Pickory.IntegrationTests/Infrastructure/ReadingTestData.cs`

**Bằng chứng (2026-10-08):**

- `dotnet build` đạt 0 cảnh báo và 0 lỗi.
- `dotnet test` toàn solution đạt 3 test kiến trúc, 519 test đơn vị, 771 test tích hợp; 4 test tích hợp bỏ qua, 0 lỗi. Test mới: 31 đơn vị và 50 tích hợp.
- Phép thử đột biến: 23 điểm đều bị test bắt.
  - Lượt đầu có 18 điểm:
    - chi tiết trả `draft`; chi tiết trả SKU tắt;
    - slug cũ trả 302; bỏ query string; chuyển hướng cho sản phẩm `draft`;
    - so sánh bỏ kiểm cùng danh mục; trần 5 id; chỉ lấy thuộc tính `filterable`;
    - `GetProducts` theo thứ tự DB;
    - `GetSkus` bỏ SKU tắt; luôn lấy ảnh đầu; chọn ảnh SKU sai;
    - cursor dùng `>=`; cắt trang luôn trả cursor `null`;
    - sắp hết hàng dùng `<`; đếm cả SKU tắt;
    - bỏ kẹp `limit`; bỏ thuộc tính của danh mục cha.
  - Review lượt 1 thấy 2 đột biến còn xanh: tên tham số `brand` và `priceMin` của `ListProductsAsync`. Builder thêm test lọc hãng và khoảng giá.
  - Lượt review 1 thêm 5 điểm:
    - đổi tên tham số `brand`, `priceMin`, `priceMax`;
    - ngưỡng chung `<=` thành `<`;
    - bộ lọc `lowStock` của danh sách sản phẩm bỏ ngưỡng.
- Task không đổi giao diện, nên không chạy Playwright.
- Script kiểm tra comment báo 0 vi phạm. Các file đã đổi không chứa em dash hoặc en dash.
- Chưa đo hiệu năng trang chi tiết và lập chỉ mục với dữ liệu lớn.

**Kiểm tra:** integration test
**Phụ thuộc:** CAT-09 · **File:** `modules/catalog/services/{product-detail,catalog-reader}.ts`, `modules/catalog/routes/public-catalog.ts`, `modules/catalog/index.ts` · **Quy mô:** M

### UI-C2: Trang sản phẩm
**Mô tả:** Gallery ảnh đổi theo màu, chọn biến thể, giá và tồn kho theo biến thể, bảng thông số chia nhóm, mô tả chi tiết (đổi tài liệu JSON thành thẻ React), nút thêm vào so sánh. Nút thêm giỏ ở trạng thái tắt tới khi có `commerce`. Chừa vị trí cho khối gợi ý và tóm tắt review.
**Tiêu chí nghiệm thu:**
- [x] Chọn biến thể khác thì giá, giá niêm yết, tồn kho và ảnh đổi đúng
- [x] Tiêu đề cấp 1, 2, 3 của mô tả hiện thành thẻ H2, H3, H4; trang chỉ có đúng một H1
- [x] Chữ đậm và chữ nghiêng hiện đúng
- [x] Tài liệu chứa nút lạ không hiện nội dung của nút đó và không chạy script (test với payload `script` và `onerror`)
- [x] Quy tắc ESLint chặn `dangerouslySetInnerHTML` trong `features/catalog/`; fixture lỗi chứng minh quy tắc hoạt động
- [x] Sản phẩm `discontinued` hiện "Ngừng kinh doanh" và không cho chọn mua
- [x] Không cuộn ngang ở viewport 375 px
**Kiểm tra:** test component; Playwright trên `pnpm dev`
**Phụ thuộc:** CAT-10, UI-C1, CAT-13 · **File:** `features/catalog/{product-page,variant-picker,spec-table,product-gallery,description-view}.tsx`, `eslint.config.js` · **Quy mô:** M

**File:** `apps/frontend/src/features/catalog/customer/{product-page,product-view,variant-picker,product-gallery,spec-table,description-view,compare-control}.tsx`, `{variant-selection,gallery-images,spec-format,compare-list,description-blocks,use-compare-list,catalog-api,product-display}.ts`, `product-page.css`, `apps/frontend/src/app.tsx` (route `/p/:slug`), `apps/frontend/eslint.config.js`; test: `apps/frontend/test/unit/features/catalog/customer/`, `apps/frontend/e2e/catalog-product-page.spec.ts`; backend: chỉ sửa một comment trong `Pickory.Modules.Catalog/Images/ProductImageUrl.cs`

**Quyết định (2026-10-08):**

- Route `/p/:slug` nằm trong `CustomerLayout`. Trang gọi `GET /api/products/{slug}`. Slug cũ được `fetch` đi theo 301, rồi `navigate` với `replace` sang slug mới và giữ query string.
- Mã `PRODUCT_NOT_FOUND` hiện "Không tìm thấy sản phẩm" kèm link về trang chủ. Lỗi khác hiện bằng `ErrorAlert`.
- SKU đang chọn nằm trên URL ở `?sku=`, ghi bằng `replace`. Mặc định là SKU trong `?sku=`, rồi SKU còn hàng đầu tiên, rồi SKU đầu tiên.
- Bộ chọn biến thể dùng `fieldset`, `legend` và radio gốc. Chỉ giá trị không có trong SKU đang bật nào mới bị tắt. SKU hết hàng vẫn chọn được và hiện "Hết hàng".
- Giá trị có SKU nhưng không ghép được với lựa chọn hiện tại vẫn bấm được, viền nét đứt và có chữ ẩn cho trình đọc màn hình. Khi bấm, trang chọn SKU giữ được nhiều giá trị đang chọn nhất. Hòa thì ưu tiên SKU còn hàng, rồi SKU đứng trước trong `skus`.
- Khách chỉ thấy "Còn hàng" hoặc "Hết hàng", vì API không trả số tồn. Sản phẩm `discontinued` không hiện nhãn tồn kho. Giá niêm yết cao hơn giá bán thì gạch ngang kèm phần trăm giảm.
- Ảnh riêng của một SKU hiện cho mọi biến thể cùng màu. Tùy chọn màu là tùy chọn có tên bắt đầu bằng "Màu". Không có tùy chọn màu thì ảnh riêng chỉ hiện cho đúng SKU đó.
- Mô tả được đọc bằng hàm tự viết, không dùng schema nghiêm ngặt. Nút và mark lạ bị bỏ cùng nội dung con, phần còn lại vẫn hiện. Chữ đi qua React dưới dạng chuỗi.
- Quy tắc ESLint `no-restricted-syntax` chặn `dangerouslySetInnerHTML` trong `src/features/catalog/`. Fixture lỗi là chuỗi code được lint bằng API `ESLint`, nên `pnpm lint` vẫn sạch.
- Sản phẩm `discontinued` hiện nhãn "Ngừng kinh doanh". Bộ chọn biến thể chỉ để xem. Nút thêm giỏ luôn tắt kèm "Sắp có" theo Open Question 4.
- Nút so sánh ghi "Thêm vào so sánh" hoặc "Bỏ khỏi so sánh". Số đếm "Danh sách so sánh: n/4" nằm ở dòng riêng.
- Danh sách so sánh nằm trong `localStorage` với khóa `pickory.compare`, tối đa 4 sản phẩm cùng danh mục. Mọi lần đọc và ghi bọc `try/catch`. UI-C3 dùng lại `compare-list.ts`.
- Hai vùng `recommendations` và `personalized-content` là thẻ `div` rỗng, không hiện khung giữ chỗ.
  - Ghi chú 2026-10-14: hai module này đã bỏ. Xóa hai vùng `data-slot` trong `product-view.tsx` ở một task sau.
- Repo chưa có mẫu đặt `document.title`, nên trang chưa đặt tiêu đề tab theo tên sản phẩm.

**Bằng chứng (2026-10-08):**

- Playwright chạy trên Vite dev server (cùng lệnh với `pnpm dev`), với API E2E và DB `pickory_e2e`.
- Vitest: 55 file, 606 test đạt. Có 9 file test mới của UI-C2. `pnpm typecheck`, `pnpm lint` và `dotnet build` sạch.
- Spec `catalog-product-page.spec.ts`: 9 test đạt, chạy có giao diện (`--headed`). Sau review lượt 1, spec có 9 test đạt khi chạy có giao diện. Cả bộ E2E: 27 test đạt khi chạy có giao diện và 27 test đạt khi chạy ẩn.
- Lượt đầu chạy cả bộ có 1 test (headed) và 2 test (ẩn) của UI-C2 lỗi. Nguyên nhân: API không đảm bảo thứ tự SKU tạo cùng lúc, còn test giả định SKU mặc định. Test đổi sang mở bằng `?sku=`.
- Phép thử đột biến ở Vitest: 11 điểm, đều bị test bắt.
  - Luôn dùng SKU đầu; ảnh riêng không lọc theo màu; tiêu đề cấp 1 thành `h1`.
  - Nút lạ hiện nội dung con; bỏ quy tắc ESLint; `discontinued` vẫn đổi được biến thể.
  - Danh sách so sánh nhận sản phẩm thứ 5; chuyển slug cũ làm mất query string; giá niêm yết bằng giá bán vẫn hiện.
  - Bảng thông số giữ dòng `null`; giá trị không ghép được không bị tắt.
- Review lượt 1: thêm test cho sự kiện `storage` có khóa `null` (tab khác xóa toàn bộ storage). Đột biến bỏ nhánh này đỏ. Chú thích lý do chặn nút đặt ngay dưới nút.
- Phép thử đột biến ở E2E (`--headed`): 3 điểm, đều bị test bắt. Gồm luôn dùng SKU đầu, nút lạ hiện nội dung con và `discontinued` vẫn đổi được biến thể.
- Review lượt 1: bộ chọn từng chặn khách tới SKU không chung giá trị nào với SKU hiện tại, ví dụ "Đen 256GB" sang "Bạc 512GB". Đã sửa và có test đơn vị, test component và test E2E với đúng ví dụ này.
- Phép thử đột biến sau review: 7 điểm Vitest và 1 điểm E2E, đều bị test bắt.
  - Trả lại quy tắc tắt cũ; nút lạ inline có trường `text` hiện chữ.
  - Chọn SKU bỏ qua số giá trị giữ được; bỏ ưu tiên SKU còn hàng khi hòa.
  - Sản phẩm `discontinued` hiện nhãn tồn kho; nhãn nút so sánh không đổi; bỏ `aria-describedby` của nút giỏ.
- Lỗi builder tự sửa: lớp CSS `product-section` trùng với CSS trang quản trị `products.css`, nên tiêu đề mô tả bị thu về 15 px. Đã đổi thành `product-info-section`.

### UI-C3: Trang so sánh
**Mô tả:** Danh sách so sánh lưu ở `localStorage`, tối đa 4 sản phẩm cùng danh mục. Bảng thông số đặt cạnh nhau, đánh dấu dòng khác nhau.
**Tiêu chí nghiệm thu:**
- [x] Thêm sản phẩm thứ 5 hoặc khác danh mục thì hiện thông báo, không thêm
- [x] Dòng có giá trị khác nhau được đánh dấu bằng cả màu và chữ, không chỉ bằng màu
- [x] `localStorage` không dùng được thì trang vẫn hoạt động trong phiên
**Kiểm tra:** test component
**Phụ thuộc:** CAT-10, UI-C2 · **File:** `features/catalog/{compare-page,use-compare-list}.tsx`, `app.tsx` · **Quy mô:** S

**File:** `apps/frontend/src/features/catalog/customer/{compare-page,compare-rows,compare-list-store,use-compare-list,compare-control}.ts(x)`, `compare-page.css`, `catalog-api.ts`, `product-display.ts`, `apps/frontend/src/app.tsx` (route `/compare`); test: `apps/frontend/test/unit/features/catalog/customer/`, `apps/frontend/e2e/catalog-compare-page.spec.ts`

**Quyết định (2026-10-08):**

- Route `/compare` nằm trong `CustomerLayout`. Trang đọc danh sách từ `useCompareList` và gọi `GET /api/products/compare?ids=` với id theo đúng thứ tự trong danh sách. Danh sách rỗng thì không gọi API.
- Lỗi cũ của `use-compare-list.ts`: khi `localStorage` không dùng được, mỗi lần thêm đọc lại storage rỗng và xóa sản phẩm trước đó. Mỗi component cũng giữ state riêng.
- Cách sửa: `compare-list-store.ts` giữ danh sách trong bộ nhớ, dùng chung cho cả phiên. Storage chỉ là nơi lưu thêm. Component đồng bộ bằng `useSyncExternalStore` và kho nghe sự kiện `storage` của `window`.
- Bảng so sánh có một cột cho mỗi sản phẩm. Đầu cột có ảnh, tên link tới `/p/{slug}`, giá, nhãn trạng thái và nút "Bỏ khỏi so sánh". Thông số chia theo `attributeGroups`, mỗi nhóm một dòng tiêu đề.
- Giá trị `null` hiện "Chưa có thông tin". Dòng mà mọi sản phẩm đều `null` bị ẩn, nhóm không còn dòng nào cũng ẩn.
- Một dòng khác nhau khi các giá trị đã format không giống hệt nhau, `null` khác mọi giá trị có thật. Dòng khác nhau có màu nền `--color-warning-soft` và nhãn chữ "Khác nhau" trong ô tên thông số. Công tắc "Chỉ hiện điểm khác nhau" dùng `input type="checkbox"` gốc.
- API bỏ sản phẩm bị ẩn hoặc không tồn tại. Trang bỏ các id đó khỏi danh sách và hiện dòng thông báo. Lỗi `COMPARE_CATEGORY_MISMATCH` hiện `ErrorAlert` kèm nút "Xóa danh sách so sánh". Lỗi khác hiện `ErrorAlert`.
- `compare-control.tsx` có link "Xem so sánh" khi danh sách có ít nhất một sản phẩm. Thông báo "Đã đủ 4 sản phẩm" và thông báo khác danh mục dùng `role="status"`.
- Dưới 768 px, bảng cuộn ngang trong khung `.table-wrap`. Cột tên thông số dính bên trái bằng `position: sticky`.

**Bằng chứng (2026-10-08):**

- Playwright chạy trên Vite dev server (cùng lệnh với `pnpm dev`), với API E2E và DB `pickory_e2e`.
- Vitest: 58 file, 640 test đạt. Có 3 file test mới của UI-C3 (`compare-rows`, `compare-list-store`, `compare-page`). `pnpm typecheck` và `pnpm lint` sạch.
- Spec `catalog-compare-page.spec.ts`: 6 test đạt, chạy có giao diện (`--headed`). Cả bộ E2E: 33 test đạt khi chạy có giao diện và 33 test đạt khi chạy ẩn ở lượt trước review. Sau review chạy lại hai spec `catalog-compare-page` (6 test) và `catalog-product-page` (9 test), đều đạt ở chế độ `--headed`.
- Phép thử đột biến ở Vitest: 10 điểm, đều bị test bắt.
  - Danh sách nhận sản phẩm thứ 5; danh sách nhận sản phẩm khác danh mục.
  - Dòng khác nhau chỉ đổi màu, bỏ nhãn chữ; coi `null` bằng mọi giá trị.
  - Bỏ kho trong bộ nhớ; không bỏ id mà API không trả; gọi API khi danh sách rỗng.
  - Bỏ link "Xem so sánh"; công tắc không ẩn dòng giống nhau; bỏ đọc lại khi tab khác đổi storage.
- Phép thử đột biến ở E2E (`--headed`): 3 điểm, đều bị test bắt. Gồm danh sách nhận sản phẩm thứ 5, bỏ nhãn chữ "Khác nhau" và bỏ kho trong bộ nhớ.

### CAT-11: Thẻ meta SEO
**Mô tả:** Hàm `renderPageMeta(template, meta)` chèn `title`, `description`, Open Graph (`og:title`, `og:description`, `og:image`, `og:url`) vào `index.html`. Route trả HTML cho `/products/:slug` và `/categories/:slug`. Bước gắn vào server production chờ quyết định deploy.
**Tiêu chí nghiệm thu:**
- [x] Tên và mô tả sản phẩm được escape HTML trong thẻ meta
- [x] Slug không tồn tại trả 404 với thẻ meta mặc định
- [x] Slug cũ trả 301 giống API
**Kiểm tra:** unit `renderPageMeta`; integration route bằng Supertest
**Phụ thuộc:** CAT-10 · **File:** `modules/catalog/services/page-meta.ts`, `modules/catalog/routes/page-meta.ts` · **Quy mô:** S

**File:** `apps/backend/src/Pickory.Modules.Catalog/Features/Storefront/{PageMetaRenderer,PageMetaFactory,WebPageTemplate,PublicPageEndpoints}.cs`, `apps/backend/src/Pickory.Modules.Catalog/CatalogModule.cs`, `apps/backend/src/Pickory.Shared/Configuration/AppConfig.cs` (biến `WEB_INDEX_HTML_PATH`), `.env.example`; test: `apps/backend/tests/Pickory.UnitTests/Modules/Catalog/Storefront/{PageMetaRendererTests,PageMetaFactoryTests}.cs`, `apps/backend/tests/Pickory.IntegrationTests/Api/Catalog/PublicPageMetaTests.cs`

**Quyết định (2026-10-08):**

- Bản .NET dùng route `GET /p/{slug}` và `GET /c/{slug}`, khớp route thật của SPA. Decision 13 ghi `/products/:slug` và `/categories/:slug` vì viết trước khi SPA chốt route. Link khách chia sẻ là `/p/` và `/c/`.
- `PageMetaRenderer.Render(template, meta)` là hàm thuần. Hàm thay nội dung thẻ `<title>`, rồi chèn các thẻ sau trước `</head>`:
  - `description` và `canonical`;
  - `og:type`, `og:title`, `og:description`;
  - `og:url`, `og:image` (nếu có ảnh), `og:site_name`.
- Mọi giá trị đi qua `HtmlEncoder` (cho phép chữ Unicode ra nguyên dạng). Template thiếu `<title>` hoặc `</head>` thì ném `InvalidOperationException`, vì đó là lỗi cấu hình.
- Sản phẩm: title `"{tên} | Pickory"`, description lấy `shortDesc`, rỗng thì lấy text thuần của mô tả, cắt tối đa 160 ký tự ở ranh giới từ. `og:image` là ảnh đầu, chèn `f_auto,q_auto,w_1200`.
- Danh mục: description là câu cố định `"Mua {tên} chính hãng tại Pickory."`, `og:type` là `website`. `canonical` và `og:url` ghép `WebOrigin` với đường dẫn, không kèm query string.
- Meta mặc định cho 404: title "Không tìm thấy | Pickory", description chung, không có `og:image`, kèm `robots noindex`.
- Sản phẩm `active` và `discontinued` trả 200. Slug cũ trả 301 tới `/p/{slug mới}` và giữ query string, dùng lại `ProductDetailService.LookupBySlugAsync`. `draft`, `hidden` và slug lạ trả 404 kèm HTML để SPA hiện trang "Không tìm thấy sản phẩm".
- Template đọc từ biến môi trường `WEB_INDEX_HTML_PATH`, một lần khi khởi động. Không đặt biến thì không gắn hai route, và log `Information` một dòng. Đường dẫn trỏ tới file không tồn tại thì dừng khởi động.
- Hai route công khai, nên không thêm dòng vào ma trận quyền back-office.
- Bước gắn vào server production chờ quyết định deploy: ai phục vụ `index.html` (API hay máy chủ web) và đặt `WEB_INDEX_HTML_PATH` thế nào.

**Bằng chứng (2026-10-08):**

- Unit test `PageMetaRendererTests` và `PageMetaFactoryTests`: 27 test đạt. `Pickory.UnitTests` toàn project: 546 test đạt.
- Integration test `PublicPageMetaTests`: 17 test đạt, trên template tạm trỏ bằng `UseSetting("WEB_INDEX_HTML_PATH")` của `PickoryWebApplicationFactory`.
- `dotnet test` toàn solution: 3 test kiến trúc, 546 test đơn vị và 788 test tích hợp đạt; 4 test bỏ qua, 0 lỗi. `dotnet build`: 0 lỗi, 0 cảnh báo.
- Phép thử đột biến: 8 điểm, đều bị test bắt.
  - Bỏ escape tiêu đề; bỏ escape trong thuộc tính `content`.
  - Slug lạ trả 200; slug cũ trả 302; slug cũ bỏ query string.
  - Bỏ `noindex` ở meta mặc định; không cắt description dài; bỏ biến đổi Cloudinary của `og:image`.
- Task không có UI, nên không chạy Playwright.

### CAT-12: Dữ liệu seed và đo hiệu năng
**Mô tả:** `pnpm db:seed` tạo khoảng 30 sản phẩm thật trong 5 danh mục: Laptop, Điện thoại, Tai nghe, Màn hình, Phụ kiện. Có sản phẩm nhiều biến thể, hết hàng và ngừng kinh doanh. Script đo p95 sinh 500 sản phẩm trên DB riêng.
**Tiêu chí nghiệm thu:**
- [x] Chạy seed 2 lần không tạo bản ghi trùng
- [x] Mọi sản phẩm `active` trong seed qua được `checkPublishable`
- [x] Mọi thay đổi tồn kho trong seed đi qua `applyStockMovement`
- [x] Mô tả của sản phẩm seed được tạo bằng `plainTextToDescription` và qua được schema mô tả
- [x] Script đo báo p95 của `GET /api/products` dưới 300 ms với 500 sản phẩm trên máy dev
**Kiểm tra:** chạy seed trên DB sạch; chạy script đo và ghi kết quả vào task
**Phụ thuộc:** CAT-10 · **File:** `modules/catalog/data/seed.ts`, `prisma/seed.ts`, `scripts/catalog-benchmark.ts` · **Quy mô:** M

**File:** `apps/backend/src/Pickory.Modules.Catalog/Seeding/{CatalogSeedData,SeedModels,CatalogSeeder,CatalogCommandLine,BenchmarkProducts,BenchmarkMath}.cs`, `apps/backend/src/Pickory.Api/Benchmark/CatalogBenchmark.cs`, `apps/backend/src/Pickory.Api/Program.cs`, `apps/backend/src/Pickory.Modules.Catalog/CatalogModule.cs`, `spec/SPEC.md` (lệnh chạy); test: `apps/backend/tests/Pickory.UnitTests/Modules/Catalog/Seeding/`, `apps/backend/tests/Pickory.IntegrationTests/Api/Catalog/CatalogSeedTests.cs`

**Quyết định (2026-10-08):**

- Hai lệnh dòng lệnh thay `pnpm db:seed` và `scripts/catalog-benchmark.ts`, theo mẫu `identity:seed`. Cả hai chạy bằng `dotnet run --project apps/backend/src/Pickory.Api -- <lệnh>`.
- `catalog:seed` ghi 30 sản phẩm thật trong 5 danh mục gốc (Laptop, Điện thoại, Tai nghe, Màn hình, Phụ kiện). Lệnh từ chối chạy khi `ASPNETCORE_ENVIRONMENT` là `Production`. Dữ liệu viết trong `CatalogSeedData.cs`, code ghi DB nằm ở `CatalogSeeder.cs`.
- Dữ liệu có 5 sản phẩm nhiều biến thể trong đó có tùy chọn "Màu", 3 sản phẩm hết hàng, 3 sản phẩm `discontinued` và 2 sản phẩm có `compareAtPrice`.
- Seeder dùng lại `CategoryManagementHandler`, `BrandManagementHandler` và `ProductManagementHandler`, nên luật slug, SKU, giá và đăng bán không bị viết lại. Không phải tách hàm nào của handler.
- Dữ liệu mẫu không do người cụ thể thao tác, nên handler nhận `IAuditLogWriter` rỗng (`NullAuditLogWriter`). Nhật ký quản trị cần người dùng thật.
- Khóa tự nhiên chống trùng: slug với danh mục, thương hiệu và sản phẩm; SKU theo bộ tùy chọn của sản phẩm; ảnh theo `publicId` trong thư mục `pickory/products/seed/`. Bản ghi đã có thì bỏ qua, không ghi đè. Lệnh in số tạo mới và số bỏ qua.
- Seed chạy lại được khi lệnh bị dừng giữa chừng, vì tồn kho và trạng thái được kiểm riêng cho từng sản phẩm đã có.
- Sản phẩm `active` đi qua `ChangeStatusAsync`, nên `ProductPublishing.CheckPublishable` chạy trước khi đổi trạng thái. Không qua thì lệnh dừng với mã lỗi 1 và in lý do. Sản phẩm `discontinued` đi `draft`, `active` rồi `discontinued`.
- SKU tạo với `Stock = 0`. Tồn kho ban đầu ghi bằng `StockLedger.ApplyAsync`, loại `manual`, lý do "Dữ liệu mẫu". Mỗi SKU có tồn kho có đúng một movement.
- Mô tả dựng bằng `DescriptionText.FromPlainText`, hàm này kiểm bằng `DescriptionSchema.Validate`. Lỗi được đổi thành lỗi dữ liệu mẫu kèm tên sản phẩm.
- Ảnh dùng `publicId` giữ chỗ vì chưa có tài khoản Cloudinary (Open Question 3). Trình duyệt không tải được các ảnh này ở dev. Checkpoint C3 sẽ thêm khung giữ chỗ khi ảnh lỗi.
- `catalog:benchmark` chỉ chạy khi tên DB trong `DATABASE_URL` có chữ `bench`. Lệnh kiểm tên trước khi kết nối DB. Lệnh tạo DB nếu chưa có, chạy migration, sinh 500 sản phẩm `active` qua cùng đường ghi với seed, rồi khởi động API trong cùng tiến trình trên `127.0.0.1` với cổng 0.
- Phép đo: 20 request làm nóng không tính, sau đó 400 request tuần tự bằng `HttpClient`, luân phiên 24 kiểu truy vấn. Lệnh in p50, p95, max và trả mã lỗi 1 khi p95 từ 300 ms trở lên. Phân vị tính theo hạng gần nhất, nằm trong hàm thuần có unit test.
- Cách chạy hai lệnh nằm trong `spec/SPEC.md`, ngay dưới lệnh `identity:create-admin`.

**Bằng chứng (2026-10-08):**

- `catalog:seed` trên DB dev, lần 1: tạo mới 5 danh mục, 18 thương hiệu, 30 sản phẩm, 49 SKU, 60 ảnh, 45 movement tồn kho và 33 lần chuyển trạng thái. Lần 2: tạo mới 0 bản ghi, bỏ qua 5 danh mục, 18 thương hiệu, 30 sản phẩm và 45 movement.
- `catalog:benchmark` trên `pickory_bench` với 500 sản phẩm `active`, 24 kiểu truy vấn, 400 request đo:
  - Lần 1: p50 10.8 ms, p95 17.9 ms, max 57.8 ms.
  - Lần 2 (dữ liệu đã có): p50 12.9 ms, p95 19.7 ms, max 88.9 ms.
- Máy đo: Intel Core i5-11300H 3.10 GHz (4 nhân, 8 luồng), 7.8 GB RAM, Windows 11 (10.0.26200), .NET 10.0.9, PostgreSQL trong Docker.
- Chạy `catalog:benchmark` với `DATABASE_URL` trỏ DB dev `pickory`: lệnh từ chối với mã thoát 1, số sản phẩm của DB dev vẫn là 30.
- Unit test thư mục `Seeding`: 32 test đạt (phân vị, tên DB benchmark, danh sách truy vấn, bộ 500 sản phẩm, lệnh dòng lệnh). Integration test `CatalogSeedTests`: 8 test đạt.
- `dotnet test` toàn solution: 3 test kiến trúc, 578 test đơn vị và 796 test tích hợp đạt; 4 test bỏ qua, 0 lỗi. `dotnet build`: 0 lỗi, 0 cảnh báo.
- Phép thử đột biến: 9 điểm, đều bị test bắt.
  - Seed không kiểm sản phẩm đã có; lần chạy thứ hai ghi thêm movement; gán `Stock` trực tiếp.
  - Bỏ `CheckPublishable`; bỏ kiểm schema mô tả; sản phẩm ngừng kinh doanh không được chuyển trạng thái.
  - Kiểm tên DB benchmark luôn đúng; phân vị lệch chỉ số 1; seed không từ chối Production.

### Checkpoint C3
- [x] Khách lọc, xem chi tiết, so sánh trên dữ liệu seed
- [ ] Review với người dùng

**Bằng chứng (2026-10-08):**

- Seed trên DB dev: xóa sản phẩm tên cũ `razer-deathadev-v3` bằng `DELETE /api/admin/products/{id}` (204), rồi chạy `catalog:seed`. Kết quả: tạo mới 1 sản phẩm (Razer DeathAdder V3), 1 SKU, 2 ảnh, 1 lần chuyển trạng thái; bỏ qua 5 danh mục, 18 thương hiệu, 29 sản phẩm và 45 movement.
- Script Playwright tạm (ngoài repo) chạy với `headless: false` trên API dev (:3000) và `pnpm dev` (:5173), ở 1280 x 800 và 375 x 812. Cả 26 bước đạt.
  - Danh mục Laptop mở bằng `/c/laptop` có 6 sản phẩm.
  - Lọc hãng ASUS ra 2 sản phẩm, lọc thêm RAM 16 GB ra 1 sản phẩm, đúng số đếm của facet.
  - Sắp xếp giá tăng dần: 14.990.000 đến 52.990.000.
  - Đổi ổ cứng MacBook Air từ 256GB sang 512GB: giá đổi từ 27.990.000 đ sang 32.990.000 đ. SKU Zephyrus Trắng 2TB hiện "Hết hàng".
  - Sản phẩm `discontinued` hiện "Ngừng kinh doanh", không có nhãn tồn kho, nút giỏ tắt.
  - Thêm 3 laptop vào so sánh: `/compare` có bảng 3 sản phẩm và 5 dòng "Khác nhau". Thêm Google Pixel 8 (khác danh mục) có thông báo, danh sách vẫn 3.
  - Ở 375 px không cuộn ngang ở trang lọc, trang sản phẩm và trang so sánh.
- Lỗi console: 0 lỗi từ `res.cloudinary.com`, 1 lỗi khác là `favicon.ico` 404 của Vite dev. Không có lỗi trang.
- Ảnh seed không tải được ở dev. Component `ProductImage` thay ảnh bằng khung giữ chỗ khi ảnh lỗi. Trên lưới, gallery và bảng so sánh không còn ảnh vỡ.
- Ảnh chụp từng bước nằm trong thư mục `C3-shots/` của scratchpad reviewer.
- Vitest: 59 file, 649 test đạt. `pnpm test:e2e --headed`: 33 test đạt. `pnpm typecheck`, `pnpm lint` và `pnpm build` sạch.

---

## Phase 4: E2E

### E2E-C1: Luồng E2E của catalog
**Mô tả:** Playwright trên DB `pickory_e2e`: admin tạo danh mục có bộ thuộc tính, tạo sản phẩm 2 tùy chọn, soạn mô tả có tiêu đề và chữ đậm, tải ảnh qua provider giả, đăng bán. Khách lọc thấy sản phẩm, chọn biến thể thấy đúng giá, mô tả hiện đúng định dạng.
**Tiêu chí nghiệm thu:**
- [x] `pnpm test:e2e` chạy sạch cả luồng identity cũ và luồng catalog mới
- [x] Chạy lại lần hai vẫn PASS, không dựa vào dữ liệu lần trước
**Kiểm tra:** `pnpm test:e2e` hai lần liên tiếp
**Phụ thuộc:** UI-C1, UI-C2, UI-C5, UI-C6, UI-C8, CAT-07 · **File:** `e2e/catalog.spec.ts`, `apps/backend/scripts/e2e-start.ts` · **Quy mô:** S

**File:** `apps/frontend/e2e/catalog.spec.ts`, `apps/frontend/e2e/catalog-admin-flow.ts` (hàm dùng chung), `apps/frontend/e2e/catalog-checkpoint-c2.spec.ts` (dùng lại hàm chung), `apps/backend/src/Pickory.Api/E2e/E2eStartup.cs` (lệnh `e2e:start` có sẵn)

**Bằng chứng (2026-10-08):**

- Playwright chạy trên Vite dev server (cùng lệnh với `pnpm dev`), với API E2E và DB `pickory_e2e` dựng lại mỗi lần chạy.
- Spec `catalog.spec.ts`: 1 test đi trọn luồng qua giao diện thật, chạy có giao diện (`--headed`) đạt.
- `pnpm test:e2e --headed`, 3 lượt liên tiếp:
  - Lần 1: 34 test đạt, 3.0 phút.
  - Lần 2 (ngay sau lần 1): 34 test đạt, 3.0 phút.
  - Chạy ẩn (`pnpm test:e2e`): 34 test đạt, 2.5 phút.
- Mọi tên, slug và mã SKU của spec mang hậu tố riêng của lần chạy. Spec không đọc thư trong `DEV_OUTBOX_DIR` và không đọc dữ liệu lần trước.
- Phép thử đột biến: 3 điểm, đều bị spec bắt (trang sản phẩm luôn hiện giá SKU đầu; mô tả hiện tiêu đề cấp 1 thành `h1`; bộ lọc danh mục bỏ tham số `attr.*`).

---

## Checkpoint cuối
- [x] Mọi dòng Traceability trong `plan.md` đạt
- [x] Coverage `services/*.ts` và `domain/*.ts` của `catalog` ít nhất 80% dòng
  - Cách đo của bản .NET: coverage dòng của assembly `Pickory.Modules.Catalog`, gộp test đơn vị và test tích hợp, bỏ `Persistence/Migrations/`. Xem đoạn bằng chứng bên dưới.
- [x] `pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `shipping` hoặc `commerce`
  - Ngày 2026-10-09 người dùng cho phép bắt đầu `shipping` trước mục này. Người dùng sẽ tự test catalog sau, nên mục này vẫn mở.

**Bằng chứng (2026-10-08):**

#### 1. Bảng Traceability

| Success Criteria | Bằng chứng | Trạng thái |
| --- | --- | --- |
| Lọc theo danh mục, thương hiệu, giá, còn hàng, ít nhất 3 thuộc tính; kết quả và `facets` đúng với seed | Tích hợp `PublicProductListTests`: `Combined_filters_return_exactly_the_matching_products`, `Each_filter_narrows_the_parent_category_on_its_own`, `Facets_ignore_the_filter_of_their_own_group_and_apply_every_other_filter`, `Facet_entries_carry_key_label_type_unit_and_skip_zero_counts`. Seed: `CatalogSeedTests.Seed_ProductsAppearInThePublicList_WithFacetCounts`. E2E: `catalog-category-page.spec.ts` (mở lại URL đã lọc, facet có số đếm), `catalog.spec.ts`. Checkpoint C3: lọc trên dữ liệu seed khớp số đếm facet | Đạt |
| Trang sản phẩm đổi giá, tồn kho, ảnh theo biến thể | E2E `catalog-product-page.spec.ts`: "chọn biến thể khác thì giá, giá niêm yết, tồn kho và ảnh đổi đúng"; `catalog.spec.ts` kiểm giá của cả 4 SKU. Vitest `product-view.test.tsx`, `gallery-images.test.ts`, `variant-selection.test.ts` | Đạt |
| So sánh 4 sản phẩm cùng danh mục; khác danh mục trả 400 | Tích hợp `PublicProductDetailTests`: `Compare_FourProductsOfSameCategory_ReturnsEverySpecInRequestedOrder`, `Compare_ProductsOfDifferentCategories_Returns400CategoryMismatch`, `Compare_MoreThanFourIds_Returns400ValidationError`. E2E `catalog-compare-page.spec.ts` | Đạt |
| Thiếu thông số bắt buộc, sai kiểu, không có ảnh, SKU bật thiếu cân nặng và kích thước thì không `active` được | Đơn vị `ProductPublishingTests`: `CheckPublishable_RequiresSpecs`, `CheckPublishable_RequiresImage`, `CheckPublishable_RequiresShippingDimensions`, `SpecValidatorTests`. Tích hợp `ProductStatusTests`: `Activate_WithoutImage_Returns422WithReason`, `Activate_MissingRequiredSpec_Returns422WithReason`, `Activate_EnabledSkuWithoutWeight_Returns422WithSkuCode`. Seed: `CatalogSeedTests.Seed_ProductThatCannotBePublished_FailsWithTheReasons` | Đạt |
| 20 request giữ hàng đồng thời không bán vượt tồn kho | Tích hợp `StockServiceTests.Reserve_TwentyConcurrentOrdersForFiveInStock_SucceedsExactlyFiveTimes`, cùng `StockInvariant` kiểm tồn kho bằng tổng movement ở các test tồn kho | Đạt |
| `sales` xem được nháp nhưng sửa nhận 403 | Tích hợp `ProductManagementTests.Sales_CanGetProducts_CannotCreateOrPatchProduct`, `GetProducts_Sales_ReturnsEveryProductStatus`; `ProductStatusTests.Status_Sales_Returns403AndKeepsStatus`, `Delete_Sales_Returns403AndKeepsProduct`; `InventoryTests.Adjust_Sales_Returns403AndChangesNothing`. Đơn vị `CatalogPermissionsTests` | Đạt |
| Mô tả có tiêu đề 1 đến 3, chữ đậm, chữ nghiêng: soạn, lưu, hiện đúng; nút lạ bị API từ chối | Đơn vị `DescriptionSchemaTests`. Tích hợp `ProductManagementTests`: `CreateProduct_DescriptionWithScriptNode_Returns400DescriptionInvalid`, `CreateProduct_DescriptionWithImageNode_Returns400DescriptionInvalid`. Vitest `description-editor.test.tsx`, `description-view.test.tsx`. E2E `catalog.spec.ts` (soạn bằng thanh công cụ, khách thấy `h2`, `h3`, `strong`) và `catalog-product-page.spec.ts` (nút lạ không chạy script) | Đạt |
| Đổi slug thì link cũ trả 301 | Tích hợp `PublicProductDetailTests.Detail_OfOldSlug_Redirects301ToCurrentSlug`, `Detail_OfOldSlug_KeepsTheQueryString`; `PublicPageMetaTests.ProductPage_OfOldSlug_Redirects301ToCurrentSlug`; `ProductManagementTests` về lịch sử slug (`UpdateProduct_SlugSelfHistoryReclaim_AndAvoidsOtherProductsActiveAndHistorySlugs`). E2E `catalog-product-page.spec.ts`: "slug cũ chuyển sang slug mới và giữ query string" | Đạt |
| Đổi giá, đổi trạng thái, điều chỉnh tồn kho có đúng 1 dòng audit | Tích hợp `ProductManagementTests.UpdateProduct_ChangePrice_RecordsAuditPriceChangeWithBeforeAndAfter`, `UpdateProduct_UnchangedPriceOrCompareAtOnly_DoesNotRecordPriceAudit`; `ProductStatusTests.Activate_CompleteProduct_Returns200WithAuditAndEvent`, `Status_SameStatus_Returns200WithoutAuditOrEvent`; `InventoryTests.Adjust_Manual_RecordsMovementAuditAndEvent` | Đạt |
| `onProductChanged` phát đúng 1 lần sau khi lưu thành công, không phát khi lỗi | Tích hợp `ProductManagementTests`: `UpdateProduct_Success_EmitsExactlyOneUpdatedEvent`, `CreateProduct_EmitsEventAfterCommit_HandlerObservesPersistedState`, `UpdateProduct_InvalidInput_PreservesDatabaseAndEmitsZeroEvents`, `UpdateProduct_RollbackOnAuditWriterFailure_PreservesDatabaseAndEmitsZeroEvents`; `ProductStatusTests.Activate_CompleteProduct_Returns200WithAuditAndEvent` | Đạt |
| `GET /api/products` p95 dưới 300 ms với 500 sản phẩm | Output `catalog:benchmark` ghi trong CAT-12: p95 17.9 ms và 19.7 ms với 500 sản phẩm `active` | Đạt |
| Coverage `service.ts` ít nhất 80% | Mục 2: coverage dòng gộp 96.1% | Đạt |
| E2E: tạo danh mục, tạo sản phẩm 2 tùy chọn, đăng bán, khách lọc thấy, chọn biến thể thấy đúng giá | `catalog.spec.ts` (E2E-C1), 34 test của cả bộ đạt | Đạt |

Mọi tên test trong bảng được kiểm là có trong repo và nằm trong các lượt chạy xanh ở mục 3.

#### 2. Coverage

- Cách đo của bản .NET: coverage dòng của assembly `Pickory.Modules.Catalog`, gộp hai lệnh `dotnet test ... --collect:"XPlat Code Coverage"` cho `tests/Pickory.UnitTests` và `tests/Pickory.IntegrationTests`.
- Script Python gộp hai file `coverage.cobertura.xml`. Một dòng tính là đã chạy khi ít nhất một lượt chạm tới dòng đó.
- Phép đo bỏ `Persistence/Migrations/`, vì đó là code sinh tự động.
- Chung: 4470/4650 dòng = **96.1%** (riêng test đơn vị 28.3%, riêng test tích hợp 90.9%).
- Theo thư mục cấp 1:
  - `Features/`: 97.0% (2549/2629)
  - `Products/`: 97.0% (162/167)
  - `Stock/`: 97.3% (249/256)
  - `Reading/`: 100% (174/174)
  - `Seeding/`: 98.3% (526/535)
  - `Specs/`: 100% (50/50)
  - `Descriptions/`: 98.2% (166/169)
  - `Attributes/`: 96.8% (91/94)
  - `Persistence/`: 96.9% (217/224), `Entities/`: 95.4% (83/87), `Events/`: 100% (10/10)
  - `Images/`: 71.1% (140/197)
  - gốc module (`CatalogModule.cs`...): 91.4% (53/58)
- Ba file dưới 60%. Không thêm test cho chúng, vì tổng đã vượt 80% và chúng không chứa luật nghiệp vụ:
  - `Images/CloudinaryImageStorage.cs` (2.6%): gọi Cloudinary thật, nên test dùng kho ảnh giả.
  - `Images/ImageDeletionWorker.cs` (0%): test tích hợp bỏ worker nền và gọi `ImageDeletionProcessor` trực tiếp.
  - `Persistence/CatalogDbContextFactory.cs` (0%): chỉ dùng khi chạy lệnh migration.

#### 3. Lệnh tổng

- `pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e` (chạy ẩn) trong `apps/frontend`: sạch. Vitest 59 file, 649 test đạt; `pnpm build` thành công; E2E 34 test đạt trong 2.6 phút.
- `pnpm test:e2e --headed`: 34 test đạt trong 3.0 phút.
- `dotnet build` trong `apps/backend`: 0 lỗi, 0 cảnh báo.
- `dotnet test` toàn solution: 3 test kiến trúc, 578 test đơn vị và 796 test tích hợp đạt; 4 test bỏ qua, 0 lỗi.

---

## Phase 5: Tìm theo từ khóa (thêm ngày 2026-10-14)

### CAT-14: Tìm sản phẩm theo từ khóa
**Mô tả:** `GET /api/products` nhận tham số `q` theo mục API của `spec/SPEC-catalog.md`. `q` khớp một phần tên sản phẩm hoặc tên thương hiệu. Phép so sánh không phân biệt hoa thường và không phân biệt dấu. Backend dùng `unaccent` và `ILIKE`, giống ô tìm của `ProductManagementHandler`. `ProductFilters` thêm trường tùy chọn `Query`, để `ICatalogReader.ListProductsAsync` nhận cùng từ khóa. Đây là đổi Contracts. Người dùng duyệt ngày 2026-10-14, khi bỏ `smart-search`.
- `SearchTerm.Read` của `Pickory.Shared` chỉ đọc tham số `search`. Task cần cách đọc tham số `q` mà vẫn dùng chung phần chuẩn hóa và escape.
- Task chỉ làm backend. Ô tìm trên giao diện khách chờ người dùng chốt vị trí (Open Question 5 của spec).
**Tiêu chí nghiệm thu:**
- [ ] Từ khóa là một phần tên sản phẩm trả đúng các sản phẩm có chuỗi đó trong tên
- [ ] `q` không dấu khớp tên có dấu. `q` có dấu khớp tên không dấu. Chữ hoa và chữ thường cho cùng kết quả
- [ ] `q` bằng tên thương hiệu trả các sản phẩm của thương hiệu đó
- [ ] `q` kết hợp với từng bộ lọc và từng kiểu `sort` của `GET /api/products` trả đúng phần giao. `facets` tính trên tập đã khớp `q`
- [ ] `q` chứa `%` hoặc `_` chỉ khớp tên có đúng ký tự đó, không khớp mọi sản phẩm
- [ ] `q` rỗng hoặc chỉ có khoảng trắng cho kết quả giống khi không gửi `q`
- [ ] `q` dài hơn 100 ký tự hoặc gửi lặp lại trả 400 `VALIDATION_ERROR`
- [ ] Sản phẩm `draft` và `hidden` không xuất hiện dù khớp `q`
- [ ] `ICatalogReader.ListProductsAsync` với `ProductFilters.Query` trả cùng tập sản phẩm với `GET /api/products?q=` có cùng bộ lọc
- [ ] Bên gọi không truyền `Query` vẫn biên dịch và nhận kết quả như trước
- [ ] `catalog:benchmark` đo thêm lượt có `q`: p95 dưới 300 ms với 500 sản phẩm trên máy dev
**Kiểm tra:** unit test bộ đọc tham số; test tích hợp `PublicProductListTests` và `CatalogReaderTests`; output `catalog:benchmark`
**Phụ thuộc:** CAT-09, CAT-10 · **File:** `apps/backend/src/Pickory.Modules.Catalog.Contracts/Reading/ProductFilters.cs`, `apps/backend/src/Pickory.Modules.Catalog/Features/Storefront/{ProductSearchQuery,ProductFilterExpressions,ProductSearchService}.cs`, `apps/backend/src/Pickory.Modules.Catalog/Reading/CatalogReader.cs`, `apps/backend/src/Pickory.Shared/Http/SearchTerm.cs` · **Quy mô:** M
