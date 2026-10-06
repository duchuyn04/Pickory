# Spec: catalog

Module id: `catalog` · Phụ thuộc: `identity` · Quy ước chung: `SPEC.md`

## Objective

Quản lý và hiển thị sản phẩm điện tử: danh mục, thương hiệu, thông số kỹ thuật theo từng danh mục, biến thể (SKU), ảnh và tồn kho. Đây là nguồn dữ liệu sản phẩm duy nhất. `commerce` lấy giá và giữ hàng từ đây; `smart-search`, `recommendations`, `personalized-content`, `shopping-assistant` đọc dữ liệu từ đây để AI làm việc trên sản phẩm có thật.

**User stories**

*Khách hàng*
- Tôi duyệt sản phẩm theo danh mục và thương hiệu, lọc theo giá, còn hàng và thông số (RAM, dung lượng, kích thước màn hình...).
- Tôi xem trang sản phẩm có ảnh, giá, bảng thông số chia theo nhóm, chọn biến thể (dung lượng, màu) và thấy giá, tồn kho thay đổi theo biến thể.
- Tôi so sánh tối đa 4 sản phẩm cùng danh mục, thông số đặt cạnh nhau.

*Quản trị viên (hoặc role có `products:write`)*
- Tôi tạo danh mục và định nghĩa bộ thông số cho danh mục đó (laptop có CPU, RAM, GPU; tai nghe có kiểu kết nối, thời lượng pin...).
- Tôi tạo sản phẩm, form tự hiện đúng các ô thông số của danh mục đã chọn.
- Tôi khai báo các tùy chọn (dung lượng, màu), hệ thống sinh sẵn các biến thể để tôi điền giá và tồn kho.
- Tôi tải ảnh lên, sắp xếp thứ tự, gắn ảnh cho từng màu.
- Tôi lưu nháp rồi mới đăng bán, ẩn tạm hoặc ngừng kinh doanh một sản phẩm.

*Role có `inventory:write`*
- Tôi điều chỉnh tồn kho kèm lý do, và xem lịch sử nhập xuất của từng SKU.

*Nhân viên `sales`, `support` (có `products:read`)*
- Tôi xem được cả sản phẩm nháp và đã ẩn để trả lời khách, nhưng không sửa được.

## Thiết kế

### Danh mục và bộ thông số

- Danh mục dạng cây tối đa 2 cấp, ví dụ `Laptop > Laptop gaming`. Sản phẩm chỉ gắn vào danh mục lá.
- Mỗi danh mục có danh sách **thuộc tính thông số**. Danh mục con kế thừa thuộc tính của danh mục cha và thêm của riêng nó.

```ts
// apps/frontend/src/shared/catalog/attributes.ts
export type AttributeDefinition = {
  key: string;                 // 'ram_gb', khóa cố định, không đổi sau khi đã có dữ liệu
  label: string;               // 'RAM'
  group: string;               // 'Bộ nhớ' (để chia nhóm trong bảng thông số)
  type: 'text' | 'number' | 'enum' | 'boolean';
  unit?: string;               // 'GB', 'inch', 'mAh'
  options?: string[];          // với type 'enum'
  required: boolean;
  filterable: boolean;         // hiện thành bộ lọc trên trang danh mục
};
```

- Giá trị thông số của sản phẩm lưu dạng JSON, **validate theo bộ thuộc tính của danh mục** mỗi lần lưu. Sai kiểu hoặc thiếu ô bắt buộc thì không lưu được.
- Đổi bộ thuộc tính khi đã có sản phẩm: được thêm thuộc tính mới (không bắt buộc) và sửa nhãn; xóa thuộc tính hoặc đổi `type` thì bị chặn nếu đang có sản phẩm dùng.

### Sản phẩm và biến thể

- **Sản phẩm**: tên, slug, thương hiệu, danh mục, mô tả ngắn, mô tả chi tiết (tài liệu JSON của trình soạn thảo), thông số, số tháng bảo hành, trạng thái, ảnh.
- **Tùy chọn**: tối đa 3 trục, ví dụ `Dung lượng: 128GB, 256GB` và `Màu: Đen, Trắng`. Sản phẩm không có tùy chọn vẫn có đúng 1 biến thể mặc định.
- **Biến thể (SKU)**: mã SKU duy nhất, tổ hợp giá trị tùy chọn, giá bán, giá niêm yết (để hiện giảm giá), tồn kho, ảnh riêng (tùy chọn), trạng thái bật/tắt.
- **Cân nặng và kích thước đóng gói** của mỗi SKU (gram, cm). Bắt buộc với SKU đang bật, vì `shipping` dùng để tính phí vận chuyển. *(bổ sung khi tách module `shipping`)*
- Thông số chung đặt ở sản phẩm. Khác biệt giữa các biến thể (dung lượng, màu) thể hiện qua tùy chọn.

**Trạng thái sản phẩm:**

| Trạng thái | Khách thấy | Mua được | Ghi chú |
|---|:-:|:-:|---|
| `draft` | | | Mặc định khi tạo |
| `active` | ✓ | ✓ | Cần ít nhất 1 ảnh và 1 biến thể đang bật có giá, cân nặng, kích thước |
| `hidden` | | | Ẩn tạm, link cũ trả 404 |
| `discontinued` | ✓ | | Vẫn xem được (giữ link, SEO) nhưng hiện "Ngừng kinh doanh" |

- Không xóa cứng sản phẩm đã có trong đơn hàng. Sản phẩm chưa từng được đặt mới xóa cứng được.

**Quy tắc giá:**
- Giá là số nguyên VND, lớn hơn 0.
- `compareAtPrice` để trống, hoặc phải lớn hơn giá bán.
- Giá hiển thị trên danh sách là giá thấp nhất trong các biến thể đang bật và còn hàng. Hết hàng toàn bộ thì lấy giá thấp nhất trong các biến thể đang bật.
- Đổi giá được ghi audit log.

**Slug:**
- Sinh từ tên, bỏ dấu tiếng Việt, ví dụ `laptop-asus-rog-strix-g16`. Trùng thì thêm hậu tố số.
- Đổi slug thì slug cũ được lưu lại; truy cập slug cũ trả redirect 301 sang slug mới.

### Tồn kho

- MVP có 1 kho. Tồn kho theo từng SKU.
- Mọi thay đổi tồn kho ghi vào `inventory_movements` (SKU, số lượng thay đổi, loại, người làm hoặc mã đơn, lý do, thời điểm). Tồn kho hiện tại phải bằng tổng các movement.
- Điều chỉnh tay cần quyền `inventory:write` và bắt buộc nhập lý do.
- Ngưỡng sắp hết hàng mặc định là 5, hiện cảnh báo trong trang quản lý.

**Interface giữ hàng cho `commerce`:**

```ts
// apps/backend/src/modules/catalog/index.ts
export const stock: {
  /** Giữ hàng cho đơn. Tất cả hoặc không: thiếu 1 SKU thì không giữ SKU nào. */
  reserve(orderId: string, items: { skuId: string; quantity: number }[], tx?: Tx): Promise<ReserveResult>;
  /** Trả lại hàng đã giữ (đơn hủy, quá hạn thanh toán). Gọi lặp lại không trả hàng 2 lần. */
  release(orderId: string, tx?: Tx): Promise<void>;
};

export type ReserveResult =
  | { ok: true }
  | { ok: false; shortages: { skuId: string; requested: number; available: number }[] };
```

- `reserve` dùng câu lệnh cập nhật có điều kiện (`stock >= quantity`) trong 1 transaction, nên 2 người mua cùng lúc không bán vượt tồn kho.
- `reserve` và `release` idempotent theo `orderId`.
- Hàng đã giao không cần bước riêng: khi đã giữ thì tồn kho đã trừ. Đổi trả hàng nhập lại bằng điều chỉnh tay (loại `return`).

### Interface đọc cho các module khác

```ts
export const catalogReader: {
  getProduct(idOrSlug: string): Promise<ProductDetail | null>;          // chỉ sản phẩm khách thấy được
  getProducts(ids: string[]): Promise<ProductSummary[]>;                 // giữ thứ tự ids, bỏ qua id không hợp lệ
  getSkus(skuIds: string[]): Promise<SkuSnapshot[]>;                     // giá, tên, ảnh, cân nặng, kích thước tại thời điểm gọi, cho commerce
  listForIndexing(cursor?: string): Promise<{ items: ProductDetail[]; next?: string }>; // cho smart-search
  getAttributeDefinitions(categoryId: string): Promise<AttributeDefinition[]>;
  listProducts(filters: ProductFilters & { limit: number }): Promise<ProductSummary[]>;   // cùng bộ lọc với GET /api/products
  countLowStock(): Promise<number>;                                      // cho badge của admin-console
};

export { onProductChanged } from './events';   // { productId, change: 'created' | 'updated' | 'status' | 'deleted' }
export { onStockChanged } from './events';     // { skuId, stock, previousStock }
export { catalogPermissions } from './permissions';
```

- `smart-search` nghe `onProductChanged` để tính lại embedding; `recommendations` nghe để bỏ sản phẩm không còn bán.
- Sự kiện phát sau khi transaction commit, không phát khi transaction lỗi.

### Ảnh (Cloudinary)

- Trình duyệt tải ảnh thẳng lên Cloudinary bằng chữ ký do backend cấp (`POST /api/admin/uploads/signature`, cần `products:write`). Ảnh không đi qua server API.
- Backend chỉ lưu `publicId`, alt text, thứ tự, và biến thể gắn với ảnh (nếu có).
- Giới hạn: JPG, PNG, WebP; tối đa 5 MB mỗi ảnh; tối đa 10 ảnh mỗi sản phẩm.
- Hiển thị qua URL biến đổi của Cloudinary (`f_auto,q_auto`, các chiều rộng 200/400/800/1200) để trình duyệt chọn ảnh vừa kích thước.
- Xóa ảnh khỏi sản phẩm thì xóa luôn trên Cloudinary (qua job nền, lỗi thì thử lại).

### Mô tả sản phẩm

Admin soạn mô tả chi tiết bằng trình soạn thảo trực quan. Backend lưu tài liệu JSON, không lưu HTML.

- **Thư viện:** Tiptap (dựa trên ProseMirror). Người dùng duyệt ngày 2026-10-05. Chỉ cài các extension cần dùng.
- **Chức năng bật:** đoạn văn, tiêu đề 3 cấp, chữ đậm, chữ nghiêng. Danh sách, link và ảnh trong mô tả chưa làm ở MVP.
- **Schema (`apps/frontend/src/shared/catalog/description-schema.ts`):** Zod chỉ nhận các nút `doc`, `paragraph`, `heading` (cấp 1 đến 3) và `text`. Nút `text` chỉ có mark `bold` hoặc `italic`. Nút hoặc mark lạ, hoặc thuộc tính thừa, bị từ chối.
- **Giới hạn:** tối đa 20.000 ký tự text và 500 nút mỗi mô tả. Con số này là mặc định, đổi ở một hằng số.
- **Kiểm tra hai phía:** backend kiểm tra bằng schema mỗi lần lưu. Trình soạn thảo cũng kiểm tra bằng cùng schema, và bỏ nội dung dán vào nếu ngoài danh sách bật.
- **Hiển thị:** frontend đổi từng nút thành thẻ React. Không dùng `dangerouslySetInnerHTML`. Quy tắc ESLint chặn thuộc tính này trong `features/catalog/`.
- **Cấp tiêu đề:** trang sản phẩm đã có H1 là tên sản phẩm. Tiêu đề cấp 1, 2, 3 của trình soạn thảo hiện thành thẻ H2, H3, H4.
- **Text thuần:** hàm `descriptionToPlainText(doc)` trong `apps/frontend/src/shared/catalog/description-text.ts` lấy text từ tài liệu. `smart-search` dùng hàm này để tạo embedding. Hàm `plainTextToDescription(text)` đổi text thường thành tài liệu, mỗi dòng trống tách một đoạn văn, dùng cho dữ liệu seed.

## API

**Công khai**

| Method | Path | Mô tả |
|---|---|---|
| GET | `/api/categories` | Cây danh mục, kèm thuộc tính `filterable` của từng danh mục |
| GET | `/api/brands` | Danh sách thương hiệu |
| GET | `/api/products` | Danh sách có lọc, sắp xếp, phân trang |
| GET | `/api/products/:slug` | Chi tiết sản phẩm; slug cũ trả 301 |
| GET | `/api/products/compare?ids=a,b,c` | So sánh tối đa 4 sản phẩm cùng danh mục |

Tham số `GET /api/products`:

```
category=laptop-gaming        brand=asus,msi
priceMin=15000000             priceMax=30000000
inStock=true                  attr.ram_gb=16,32      attr.screen_inch_min=15
sort=newest|price_asc|price_desc                     page=1  pageSize=24 (tối đa 48)
```

- Lọc thông số chỉ nhận thuộc tính có `filterable: true`. Thuộc tính lạ trả 400.
- Response có `facets`: số sản phẩm theo từng giá trị lọc, để hiện "RAM 16GB (12)".
- Tìm kiếm theo từ khóa và theo ý nghĩa thuộc `smart-search`, không nằm ở đây.

**Back-office**

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/admin/products` | `products:read` | Danh sách mọi trạng thái, tìm theo tên hoặc SKU |
| GET | `/api/admin/products/:id` | `products:read` | Chi tiết để sửa |
| POST · PATCH | `/api/admin/products[/:id]` | `products:write` | Tạo, sửa sản phẩm, tùy chọn, biến thể |
| POST | `/api/admin/products/:id/status` | `products:write` | Đổi trạng thái |
| DELETE | `/api/admin/products/:id` | `products:write` | Xóa cứng (chỉ khi chưa từng có đơn) |
| POST · PATCH · DELETE | `/api/admin/categories[/:id]` | `products:write` | Quản lý danh mục và bộ thuộc tính |
| POST · PATCH · DELETE | `/api/admin/brands[/:id]` | `products:write` | Quản lý thương hiệu |
| POST | `/api/admin/uploads/signature` | `products:write` | Chữ ký tải ảnh lên Cloudinary |
| POST | `/api/admin/skus/:id/adjust-stock` | `inventory:write` | Điều chỉnh tồn kho, bắt buộc lý do |
| GET | `/api/admin/skus/:id/movements` | `products:read` | Lịch sử tồn kho |
| GET | `/api/admin/inventory/low-stock` | `products:read` | SKU dưới ngưỡng |

## Data Model (Prisma, rút gọn)

```prisma
model Category {
  id         String     @id @default(uuid())
  parentId   String?
  name       String
  slug       String     @unique
  position   Int        @default(0)
  attributes Json       // AttributeDefinition[] của riêng danh mục này
  @@map("categories")
}

model Brand { id String @id @default(uuid()); name String; slug String @unique; logoPublicId String?; @@map("brands") }

model Product {
  id             String        @id @default(uuid())
  categoryId     String
  brandId        String
  name           String
  slug           String        @unique
  shortDesc      String
  description    Json          // tài liệu của trình soạn thảo, xem mục Mô tả sản phẩm
  specs          Json          // { ram_gb: 16, cpu: 'Intel Core i7-13650HX', ... }
  options        Json          // [{ name: 'Dung lượng', values: ['512GB', '1TB'] }]
  warrantyMonths Int           @default(12)
  status         ProductStatus @default(draft)
  minPrice       Int?          // tính lại khi biến thể đổi, phục vụ lọc và sắp xếp
  inStock        Boolean       @default(false)
  createdAt      DateTime      @default(now())
  updatedAt      DateTime      @updatedAt
  @@index([categoryId, status])
  @@map("products")
}

model Sku {
  id             String   @id @default(uuid())
  productId      String
  code           String   @unique      // 'ROG-G16-I7-1TB-BLK'
  optionValues   Json                  // { 'Dung lượng': '1TB', 'Màu': 'Đen' }
  price          Int
  compareAtPrice Int?
  stock          Int      @default(0)  // CHECK (stock >= 0)
  enabled        Boolean  @default(true)
  weightGrams    Int?                  // bắt buộc khi enabled = true
  lengthCm       Int?
  widthCm        Int?
  heightCm       Int?
  @@map("skus")
}

model ProductImage      { /* productId, skuId?, publicId, alt, position */ }
model ProductSlugHistory { /* oldSlug @unique, productId */ }
model InventoryMovement { /* skuId, delta, type(manual|order_reserve|order_release|return), orderId?, actorId?, reason, createdAt; @@unique([orderId, skuId, type]) */ }
```

- Lọc theo thông số dùng chỉ mục GIN trên `products.specs`.
- `minPrice` và `inStock` là cột tính sẵn, cập nhật trong cùng transaction khi biến thể hoặc tồn kho đổi.

## Frontend

*Khách hàng* (`features/catalog/`)
- Trang danh mục: lưới sản phẩm, bộ lọc sinh từ thuộc tính `filterable`, sắp xếp, phân trang. Trạng thái lọc nằm trên URL để chia sẻ được.
- Trang sản phẩm: gallery ảnh (đổi theo màu), chọn biến thể, giá và tồn kho theo biến thể, bảng thông số chia nhóm, mô tả chi tiết, nút thêm giỏ (gọi `commerce`), nút thêm vào so sánh.
- Trang so sánh: tối đa 4 sản phẩm, đánh dấu dòng thông số khác nhau.
- Chừa sẵn vị trí cho khối của các module sau: gợi ý (`recommendations`), tóm tắt review (`personalized-content`).

*Back-office* (`features/catalog/admin/`)
- Danh sách sản phẩm có lọc trạng thái, danh mục, sắp hết hàng.
- Form sản phẩm: ô thông số sinh động theo danh mục; trình soạn thảo mô tả có thanh công cụ (tiêu đề 1, 2, 3, đậm, nghiêng); bảng biến thể sinh từ tổ hợp tùy chọn; kéo thả sắp xếp ảnh.
- Quản lý danh mục kèm trình soạn bộ thuộc tính; quản lý thương hiệu.
- Màn hình tồn kho: điều chỉnh có lý do, lịch sử movement.
- Nút và menu ẩn hoặc hiện theo `useCan(...)`.

## Dữ liệu mẫu

`pnpm db:seed` tạo khoảng 30 sản phẩm thật (tên, thông số lấy từ thông tin công khai của hãng) trong 5 danh mục: Laptop, Điện thoại, Tai nghe, Màn hình, Phụ kiện. Có cả sản phẩm nhiều biến thể, sản phẩm hết hàng và sản phẩm ngừng kinh doanh để test.

## Testing

- Unit: validate thông số theo bộ thuộc tính (kể cả kế thừa từ danh mục cha); sinh tổ hợp biến thể; sinh slug bỏ dấu tiếng Việt; tính `minPrice` và `inStock`; quy tắc chuyển trạng thái; schema tài liệu mô tả (nhận nút hợp lệ, từ chối nút lạ, giới hạn kích thước) và `descriptionToPlainText`.
- Integration:
  - Mọi endpoint ở bảng API, kèm 401/403 theo quyền.
  - Lọc kết hợp nhiều điều kiện và `facets` đúng số.
  - Slug cũ trả 301.
  - Không xóa cứng được sản phẩm đã có đơn.
  - Chặn xóa thuộc tính đang được dùng.
- Đồng thời: 20 request `reserve` cùng lúc cho SKU còn 5 cái thì đúng 5 thành công, tồn kho về 0, không âm.
- Idempotent: gọi `reserve` hoặc `release` 2 lần cùng `orderId` không đổi tồn kho lần thứ 2.
- Tồn kho mỗi SKU bằng tổng movement sau mọi test.
- Cloudinary: test chữ ký bằng secret giả; không gọi Cloudinary thật trong test thường.
- E2E: admin tạo danh mục có bộ thuộc tính → tạo sản phẩm 2 tùy chọn → đăng bán → khách lọc thấy sản phẩm → chọn biến thể thấy đúng giá.

## Boundaries (riêng module)

- **Always:** validate thông số theo bộ thuộc tính trước khi lưu; ghi movement cho mọi thay đổi tồn kho; ghi audit log khi đổi giá, đổi trạng thái, điều chỉnh tồn kho.
- **Ask first:** thêm kho thứ hai; thêm trục tùy chọn thứ 4; đổi cách tính giá hiển thị.
- **Never:** sửa trực tiếp cột `stock` mà không có movement; cho tồn kho âm; lưu HTML thô từ người nhập; dùng `dangerouslySetInnerHTML` cho nội dung do người nhập; để Cloudinary API secret ra frontend.

## Success Criteria

- [ ] Khách lọc được theo danh mục, thương hiệu, giá, còn hàng và ít nhất 3 thuộc tính thông số, kết quả và `facets` đúng với dữ liệu seed.
- [ ] Trang sản phẩm đổi giá, tồn kho và ảnh đúng theo biến thể được chọn.
- [ ] So sánh 4 sản phẩm cùng danh mục hiển thị đủ thông số; khác danh mục trả 400.
- [ ] Sản phẩm thiếu thông số bắt buộc, sai kiểu, không có ảnh, hoặc có SKU đang bật thiếu cân nặng và kích thước thì không chuyển sang `active` được.
- [ ] 20 request giữ hàng đồng thời không làm bán vượt tồn kho.
- [ ] Nhân viên `sales` xem được sản phẩm nháp nhưng gọi API sửa nhận 403.
- [ ] Đổi slug thì link cũ redirect 301.
- [ ] Mỗi lần đổi giá, đổi trạng thái hoặc điều chỉnh tồn kho có đúng 1 dòng audit log.
- [ ] `onProductChanged` phát đúng 1 lần sau mỗi lần lưu thành công, không phát khi lưu lỗi.
- [ ] `GET /api/products` có p95 dưới 300 ms với 500 sản phẩm trên máy dev.
- [ ] Mô tả có tiêu đề cấp 1 đến 3, chữ đậm và chữ nghiêng: soạn được trong trình soạn thảo, lưu được, hiện đúng trên trang sản phẩm; tài liệu có nút lạ (ví dụ `script`) bị API từ chối.
- [ ] Coverage `service.ts` ≥ 80%.

## Open Questions

> Đã duyệt ngày 2026-10-03, áp dụng phương án mặc định cho cả 4 câu. SEO chọn hướng (b): server trả sẵn thẻ meta và Open Graph cho trang sản phẩm và danh mục.
>
> Ngày 2026-10-05: mô tả chi tiết đổi từ Markdown sang trình soạn thảo trực quan lưu JSON (Tiptap). Lý do: admin không cần học cú pháp, và whitelist nằm trong schema nên không cần bộ lọc HTML.

1. **SEO cho trang sản phẩm.** React + Vite là SPA, nên Google và khi chia sẻ link lên Facebook, Zalo có thể không thấy tên, giá, ảnh sản phẩm. Cần chọn một trong ba hướng:
   - (a) Prerender trang sản phẩm và danh mục.
   - (b) Server trả sẵn thẻ meta và Open Graph cho từng URL, phần còn lại vẫn là SPA.
   - (c) Chấp nhận SEO yếu ở MVP.

   Mình đề xuất (b), vì ít thay đổi kiến trúc nhất.
2. Danh sách danh mục và bộ thuộc tính chính thức lúc ra mắt? Mặc định: 5 danh mục như dữ liệu mẫu.
3. Có cần giá khuyến mãi theo thời gian (giảm giá từ ngày đến ngày) trong MVP không? Mặc định: chưa, chỉ có giá bán và giá niêm yết.
4. Thông tin bảo hành có cần chi tiết hơn số tháng không (nơi bảo hành, bảo hành chính hãng hay cửa hàng)? Mặc định: chỉ số tháng.
