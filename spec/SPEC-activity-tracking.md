# Spec: activity-tracking

Module id: `activity-tracking` · Phụ thuộc: `identity`, `catalog`, `commerce` · Quy ước chung: `SPEC.md`

## Objective

Ghi lại hành vi mua sắm của người dùng (đã đồng ý) và dựng **hồ sơ sở thích** cho từng người. Đây là nguồn dữ liệu cho các tính năng cá nhân hóa (`recommendations`, `personalized-content`, `shopping-assistant`) và cho số đo hiệu quả AI trên dashboard.

**User stories**
- Là khách đã đồng ý cá nhân hóa, tôi thấy "Sản phẩm đã xem gần đây" và gợi ý ngày càng đúng sở thích.
- Là khách, tôi xem được hệ thống đang hiểu tôi thích gì (danh mục, thương hiệu, tầm giá), và xóa lịch sử khi muốn.
- Là khách tắt cá nhân hóa, hành vi của tôi không bị ghi gắn với tôi.
- Là admin hoặc `sales` (`reports:read`), tôi biết gợi ý và chatbot có làm tăng đơn hàng không (tỉ lệ click, tỉ lệ chuyển đổi).

## Thiết kế

### Loại sự kiện

| Sự kiện | Ghi từ | Dữ liệu chính |
|---|---|---|
| `product_view` | frontend | productId, skuId?, source |
| `category_view` | frontend | categoryId, filters đang áp dụng |
| `search` | backend (`smart-search` gọi `trackServerEvent`) | câu tìm, số kết quả |
| `add_to_cart` | backend (nghe `onCartItemAdded` của `commerce`) | skuId, quantity, source |
| `remove_from_cart` | backend (nghe `onCartItemRemoved`) | skuId |
| `purchase` | backend (nghe `onOrderPaid`, hoặc `onOrderPlaced` với COD) | orderId, các productId, giá |
| `rec_impression` | frontend | danh sách productId, `source` |
| `rec_click` | frontend | productId, `source`, vị trí |
| `chat_product_click` | frontend | productId, conversationId |

- `source` cho biết sự kiện đến từ đâu, ví dụ `recommendations.for-you`, `recommendations.similar`, `shopping-assistant`, `smart-search`, `category`. Đây là cơ sở để đo hiệu quả từng tính năng AI.
- Sự kiện từ backend đáng tin hơn frontend (không bị chặn bởi ad-block), nên các sự kiện liên quan tiền (thêm giỏ, mua) luôn ghi ở backend.

### Đồng ý và quyền riêng tư

| Trạng thái đồng ý | Xử lý |
|---|---|
| `personalization = true` | Ghi sự kiện đầy đủ, gắn với `anonymousId` và `userId` |
| `personalization = false` | Không ghi sự kiện cá nhân. Chỉ tăng bộ đếm tổng hợp theo ngày (lượt xem sản phẩm, lượt hiển thị và click gợi ý theo `source`), không có định danh |

- Câu tìm kiếm lưu sau khi qua `redactPII()` (hàm dùng chung ở `apps/api/src/shared/privacy.ts`, `ai-gateway` cũng dùng), vì khách có thể gõ SĐT hay email vào ô tìm.
- **Rút lại đồng ý:** xóa ngay sự kiện và hồ sơ của `anonymousId` và `userId` đó.
- **`UserDeletedEvent`:** xóa sự kiện và hồ sơ của người dùng.
- **`UserLinkedEvent`:** gắn sự kiện của `anonymousId` vào `userId` và gộp hồ sơ. Chỉ gộp khi cả hai phía đều đã đồng ý; phía chưa đồng ý không có dữ liệu để gộp.
- Sự kiện thô giữ 12 tháng, job hằng ngày xóa phần cũ hơn. Bộ đếm tổng hợp giữ lâu dài.
- Nhân viên đăng nhập back-office không bị ghi sự kiện.

### Thu thập từ frontend

- `POST /api/events` nhận lô tối đa 50 sự kiện. Frontend gom sự kiện, gửi mỗi 5 giây hoặc khi rời trang (`navigator.sendBeacon`).
- Validate từng sự kiện bằng Zod. Sự kiện sai hoặc trỏ tới sản phẩm không tồn tại thì bỏ qua, không làm hỏng cả lô.
- Giới hạn 10 lô mỗi phút theo `anonymousId`. Bỏ qua request từ bot (user agent phổ biến của crawler).
- Endpoint luôn trả 202 nhanh; ghi DB không chặn response.
- Backend tự lấy `anonymousId`, `userId`, trạng thái đồng ý từ `req.actor`, không tin giá trị client gửi.

### Hồ sơ sở thích

```ts
// apps/api/src/modules/activity-tracking/index.ts
export type InterestProfile = {
  actorKey: string;                                   // userId nếu có, ngược lại anonymousId
  categories: { categoryId: string; score: number }[]; // top 5
  brands: { brandId: string; score: number }[];         // top 5
  priceRanges: { categoryId: string; min: number; max: number }[]; // theo danh mục quan tâm
  attributes: { categoryId: string; key: string; value: string; score: number }[]; // ví dụ ram_gb=16
  recentlyViewed: string[];                           // 20 productId gần nhất
  purchasedProductIds: string[];
  cartProductIds: string[];
  eventCount: number;
  updatedAt: string;
};

export const profileReader: {
  getProfile(actor: Actor): Promise<InterestProfile | null>;   // null nếu chưa đồng ý hoặc chưa có dữ liệu
  getProfileSummary(actor: Actor): Promise<string | null>;     // tóm tắt tiếng Việt, không có thông tin định danh, dùng trong prompt AI
};

export const activityMetrics: {
  getFeatureMetrics(range: DateRange): Promise<FeatureMetrics[]>;  // theo source
  getFunnel(range: DateRange): Promise<Funnel>;                     // xem → thêm giỏ → mua
};

export { trackServerEvent } from './track';   // module khác gọi để ghi sự kiện backend
```

**Cách tính điểm:**
- Mỗi loại sự kiện có trọng số: xem 1, xem từ gợi ý 1, thêm giỏ 3, mua 5. Bỏ giỏ thì trừ 1.
- Điểm giảm dần theo thời gian, chu kỳ bán rã 30 ngày (sự kiện 30 ngày trước chỉ còn một nửa trọng số).
- Tầm giá theo danh mục = khoảng phân vị 25 đến 75 của giá sản phẩm đã xem và mua trong danh mục đó.
- Thuộc tính quan tâm lấy từ các thuộc tính `filterable` của sản phẩm đã xem nhiều và khách đã dùng bộ lọc.

**Cập nhật hồ sơ:**
- `recentlyViewed` và `cartProductIds` cập nhật ngay khi có sự kiện.
- Phần còn lại tính lại bằng job nền mỗi 5 phút, chỉ cho người có sự kiện mới.
- Hồ sơ lưu dạng JSON trong bảng `interest_profiles`, đọc 1 truy vấn.

**Ví dụ `getProfileSummary`:**
> "Quan tâm: Laptop gaming (nhiều nhất), Tai nghe. Thương hiệu hay xem: ASUS, MSI. Tầm giá laptop: 25 đến 35 triệu. Ưu tiên RAM 16GB, màn hình 16 inch. Đã mua: chuột Logitech G502. Đang có trong giỏ: 1 sản phẩm."

Không chứa tên, SĐT, email, địa chỉ, mã đơn hay ID người dùng.

### Đo hiệu quả

- **CTR theo `source`** = `rec_click` / số sản phẩm trong `rec_impression`.
- Mọi số liệu tách được theo `variant` (nhóm thử nghiệm của `recommendations`) nếu sự kiện có trường này. *(bổ sung khi viết spec `recommendations`)*
- **Tỉ lệ chuyển đổi có quy cho AI:** một lần mua được tính cho `source` nếu khách đã click sản phẩm đó từ `source` trong vòng 7 ngày trước khi mua (quy cho lần click gần nhất).
- **Phễu:** xem sản phẩm → thêm giỏ → mua, so sánh giữa khách có cá nhân hóa và không.
- Bộ đếm tổng hợp (cả người không đồng ý) dùng cho CTR toàn cục; phần quy chuyển đổi chỉ tính trên người đã đồng ý.

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| POST | `/api/events` | công khai | Nhận lô sự kiện từ frontend |
| GET | `/api/me/recently-viewed` | công khai | Sản phẩm đã xem gần đây (rỗng nếu chưa đồng ý) |
| GET | `/api/me/interests` | công khai | Hồ sơ sở thích dạng dễ đọc |
| DELETE | `/api/me/activity` | công khai | Xóa lịch sử và hồ sơ của chính mình |
| GET | `/api/admin/analytics/features` | `reports:read` | CTR, chuyển đổi theo `source` |
| GET | `/api/admin/analytics/funnel` | `reports:read` | Phễu mua hàng |

## Data Model (Prisma, rút gọn)

```prisma
model ActivityEvent {
  id          String   @id @default(uuid())
  type        String
  anonymousId String
  userId      String?
  productId   String?
  source      String?
  payload     Json
  occurredAt  DateTime
  receivedAt  DateTime @default(now())
  @@index([userId, occurredAt])
  @@index([anonymousId, occurredAt])
  @@index([type, occurredAt])
  @@map("activity_events")
}

model InterestProfile { /* actorKey @unique, data Json, updatedAt */ }
model DailyCounter    { /* date, metric, source?, productId?, value; @@unique([date, metric, source, productId]) */ }
```

## Frontend

- Thư viện `features/activity-tracking/tracker.ts`: hàm `track(event)`, tự gom lô, tự bỏ qua khi chưa đồng ý, dùng `sendBeacon` khi rời trang. Các feature khác chỉ gọi `track`.
- Hook `useImpression(ref, payload)` ghi `rec_impression` khi khối gợi ý hiện ít nhất 50% trong khung nhìn trên 1 giây.
- Khối "Đã xem gần đây".
- Trong trang tài khoản, mục "Quyền riêng tư": xem sở thích hệ thống đang hiểu, nút xóa lịch sử.

## Testing

- Unit: trọng số và bán rã theo thời gian; tầm giá theo phân vị; gộp hồ sơ khi liên kết tài khoản; quy chuyển đổi 7 ngày; `getProfileSummary` không chứa thông tin định danh (test với dữ liệu có SĐT, email trong câu tìm).
- Integration:
  - Chưa đồng ý: `/api/events` không tạo dòng `activity_events`, chỉ tăng bộ đếm.
  - Rút đồng ý thì sự kiện và hồ sơ bị xóa.
  - Lô có sự kiện sai: các sự kiện đúng vẫn được ghi.
  - Vượt giới hạn tần suất bị chặn.
  - `onOrderPaid` tạo sự kiện `purchase`.
- Hiệu năng: nhận 100 lô/giây trong 1 phút trên máy dev mà p95 của `/api/events` vẫn dưới 100 ms.
- E2E: khách đồng ý → xem 3 laptop gaming → trang tài khoản hiện "Laptop gaming" trong sở thích → xóa lịch sử → sở thích trống.

## Boundaries (riêng module)

- **Always:** kiểm tra đồng ý ở backend trước khi ghi sự kiện cá nhân; lọc thông tin định danh khỏi câu tìm và khỏi bản tóm tắt hồ sơ.
- **Ask first:** thêm loại sự kiện mới chứa dữ liệu nhạy cảm hơn; kéo dài thời gian lưu sự kiện; chia sẻ dữ liệu hành vi ra ngoài hệ thống.
- **Never:** ghi sự kiện cá nhân khi chưa đồng ý; dùng script theo dõi của bên thứ ba (Facebook Pixel, Google Analytics...) mà không qua quyết định riêng; đưa ID người dùng vào prompt AI.

## Success Criteria

- [ ] Khách chưa đồng ý: không có dòng nào trong `activity_events` gắn với họ sau khi duyệt web.
- [ ] Khách đồng ý xem 3 sản phẩm cùng danh mục: hồ sơ có danh mục đó ở vị trí đầu trong vòng 5 phút.
- [ ] Khách vãng lai đã đồng ý đăng nhập: lịch sử xem trước đó xuất hiện trong hồ sơ tài khoản.
- [ ] Xóa lịch sử hoặc rút đồng ý: sự kiện và hồ sơ bị xóa ngay.
- [ ] `getProfileSummary` không bao giờ chứa SĐT, email, tên hay ID (test tự động).
- [ ] Dashboard trả đúng CTR và chuyển đổi theo `source` với bộ dữ liệu test dựng sẵn.
- [ ] `/api/events` p95 dưới 100 ms ở 100 lô/giây.
- [ ] Coverage `service.ts` ≥ 80%.

## Open Questions

1. Thời gian lưu sự kiện thô 12 tháng có phù hợp chính sách quyền riêng tư của shop không?
2. Có muốn dùng thêm Google Analytics hoặc Facebook Pixel cho marketing không? Nếu có thì cần thêm vào nhóm đồng ý `marketing` và cập nhật spec `identity`. Mặc định: không.
3. Khi chạy nhiều instance API, job tính hồ sơ cần cơ chế khóa để không chạy trùng. MVP 1 instance nên chưa cần.
