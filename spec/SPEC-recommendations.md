# Spec: recommendations

Module id: `recommendations` · Phụ thuộc: `catalog`, `activity-tracking`, `smart-search`, `ai-gateway` · Quy ước chung: `SPEC.md`

## Objective

Gợi ý sản phẩm phù hợp từng người ở những vị trí khách hay quyết định mua: trang chủ, trang sản phẩm, giỏ hàng. Theo phạm vi dự án, AI chỉ ở mức gọi API, nên module **không train model**. Gợi ý được tạo bằng 3 thứ: embedding sẵn có của `smart-search`, thống kê hành vi (xem cùng, mua cùng), và LLM sắp xếp lại kèm lý do gợi ý.

Module phải **chứng minh được hiệu quả**: có nhóm đối chứng để so sánh tỉ lệ click và chuyển đổi giữa có AI và không có AI.

**User stories**
- Là khách đã đồng ý cá nhân hóa, ở trang chủ tôi thấy "Dành cho bạn" gồm sản phẩm hợp danh mục, hãng và tầm giá tôi quan tâm, kèm một dòng lý do ngắn.
- Là khách, ở trang sản phẩm tôi thấy "Sản phẩm tương tự" (cùng loại, giá gần) và "Thường mua kèm" (phụ kiện hợp).
- Là khách, ở giỏ hàng tôi thấy gợi ý mua kèm phù hợp với những gì đã có trong giỏ.
- Là khách chưa đồng ý, tôi vẫn thấy gợi ý chung (bán chạy, tương tự) nhưng không dựa trên lịch sử của tôi.
- Là admin, tôi xem gợi ý có tăng click và đơn hàng so với nhóm đối chứng không, và cấu hình các cặp danh mục mua kèm.

## Thiết kế

### Vị trí gợi ý

| Vị trí (`source`) | Trang | Cá nhân hóa | Cách tạo |
|---|---|:-:|---|
| `recommendations.for-you` | Trang chủ | ✓ | Hồ sơ sở thích → ứng viên → lọc → đa dạng hóa → LLM sắp xếp lại |
| `recommendations.similar` | Trang sản phẩm | | Gần nhất theo embedding, cùng danh mục, giá trong ±30% |
| `recommendations.bought-together` | Trang sản phẩm | | Mua cùng từ đơn hàng + cặp danh mục bổ trợ |
| `recommendations.cart` | Giỏ hàng | ✓ nhẹ | Gộp "mua kèm" của các sản phẩm trong giỏ, bỏ thứ đã có |

Khách chưa đồng ý: `for-you` thay bằng "Bán chạy" (theo danh mục phổ biến), `cart` không dùng hồ sơ.

### Nguồn ứng viên

1. **Tương tự theo nội dung:** `productSearch.similarTo(productIds, k, filters)` (bổ sung vào interface của `smart-search`), dùng embedding đã có. Không phát sinh lời gọi AI.
2. **Thống kê hành vi**, tính lại mỗi đêm vào bảng `product_associations`:
   - `co_view`: hai sản phẩm được xem trong cùng phiên (cách nhau dưới 30 phút).
   - `co_purchase`: có trong cùng đơn đã thanh toán, hoặc cùng người mua trong 30 ngày.
   - Điểm chuẩn hóa theo độ phổ biến (dạng lift), để sản phẩm bán chạy không áp đảo mọi gợi ý.
3. **Cặp danh mục bổ trợ** (khởi đầu khi chưa có dữ liệu mua): ví dụ Laptop → Chuột, Balo, Tai nghe; Điện thoại → Ốp lưng, Sạc, Tai nghe. Admin cấu hình được.
4. **Bán chạy:** top theo danh mục trong 30 ngày, dùng làm phương án dự phòng và cho người dùng mới.

### Luồng "Dành cho bạn"

1. Lấy hồ sơ (`profileReader.getProfile`). Không có hồ sơ thì trả "Bán chạy".
2. Lấy khoảng 60 ứng viên:
   - Tương tự các sản phẩm đã xem gần đây.
   - `co_view` của các sản phẩm đã xem.
   - Bán chạy trong top danh mục quan tâm, trong tầm giá của khách.
3. Loại sản phẩm:
   - Không ở trạng thái `active`, hoặc hết hàng.
   - Đã mua. Riêng phụ kiện và đồ tiêu hao (theo cấu hình danh mục) vẫn được gợi ý lại.
   - Đã có trong giỏ.
   - Vừa xem trong 10 phút gần nhất. Khách vừa xem rồi, gợi ý lại không có ích.
4. Chấm điểm bằng tổng có trọng số: độ hợp danh mục và hãng, độ hợp tầm giá, điểm hành vi, độ mới.
5. Đa dạng hóa top 12: tối đa 4 sản phẩm mỗi danh mục, 3 sản phẩm mỗi hãng.
6. **Sắp xếp lại bằng LLM** (`recommendations.rerank`):
   - Gửi bản tóm tắt hồ sơ (không có thông tin định danh) cùng 24 ứng viên đầu (tên, danh mục, giá, 4 thông số chính).
   - LLM trả 12 sản phẩm theo thứ tự, mỗi cái kèm lý do tối đa 80 ký tự.
   - Validate: chỉ nhận id có trong danh sách ứng viên, đủ 12 cái, không trùng.
   - Lý do chỉ được nhắc tới sở thích có trong hồ sơ.
7. **Không bắt khách chờ LLM.** Lần đầu trả thứ tự thuật toán với lý do theo mẫu (ví dụ "Vì bạn quan tâm Laptop gaming"), đồng thời chạy sắp xếp lại ở nền. Kết quả lưu cache 6 giờ cho lần xem sau, hoặc tới khi hồ sơ đổi đáng kể (top danh mục đổi).

### Nhóm đối chứng

Để đo AI có tác dụng thật không, mỗi khách đã đồng ý được gán ổn định vào 1 nhóm theo hash của `anonymousId`:

| Nhóm | Tỉ lệ mặc định | `for-you` hiển thị |
|---|---|---|
| `control` | 10% | Bán chạy, không cá nhân hóa |
| `algo` | 45% | Thuật toán, lý do theo mẫu |
| `algo_llm` | 45% | Thuật toán + LLM sắp xếp lại, lý do do LLM viết |

- Tên nhóm (`variant`) gửi kèm `rec_impression` và `rec_click`. `activityMetrics` tách số liệu theo `variant`. *(bổ sung vào spec `activity-tracking`)*
- Tỉ lệ chia nhóm admin chỉnh được (`ai-settings:manage`). Đặt `control` = 0% để tắt thử nghiệm.
- Khi hết ngân sách AI, nhóm `algo_llm` tự hiển thị như `algo`, và số liệu thời gian đó được đánh dấu để không làm sai kết quả so sánh.

### Interface public

```ts
// apps/backend/src/modules/recommendations/index.ts
export const recommender: {
  recommend(input: {
    placement: 'for-you' | 'similar' | 'bought-together' | 'cart';
    actor: Actor;
    productId?: string;          // bắt buộc với similar, bought-together
    cartProductIds?: string[];   // với cart
    limit?: number;              // mặc định 12, tối đa 24
  }): Promise<{
    items: { product: ProductSummary; reason?: string }[];
    variant: 'control' | 'algo' | 'algo_llm' | 'none';
    source: string;              // ví dụ 'recommendations.for-you'
  }>;
};
```

`shopping-assistant` dùng `recommend` làm tool khi khách hỏi "có gì hợp với tôi" hoặc "mua kèm gì".

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/recommendations?placement=&productId=&limit=` | công khai | Gợi ý cho vị trí. Giỏ hàng lấy từ server, không nhận từ client |
| GET · PUT | `/api/admin/recommendations/complements` | `products:read` · `products:write` | Cặp danh mục bổ trợ |
| GET · PUT | `/api/admin/recommendations/experiment` | `reports:read` · `ai-settings:manage` | Tỉ lệ chia nhóm |
| GET | `/api/admin/recommendations/report` | `reports:read` | CTR, chuyển đổi theo vị trí và nhóm |

## Data Model (Prisma, rút gọn)

```prisma
model ProductAssociation {
  productId        String
  relatedProductId String
  type             String   // co_view | co_purchase
  score            Float
  computedAt       DateTime
  @@id([productId, relatedProductId, type])
  @@map("product_associations")
}

model RecommendationCache  { /* actorKey, placement, variant, items Json, profileFingerprint, expiresAt; @@unique([actorKey, placement]) */ }
model CategoryComplement   { /* categoryId, complementCategoryId, weight, repurchasable Boolean */ }
model RecommendationSettings { /* controlPct, algoPct, algoLlmPct */ }
```

- Gợi ý `similar` và `bought-together` không phụ thuộc người dùng, được tính sẵn và cache theo sản phẩm. Làm mới khi `onProductChanged` hoặc sau job đêm.

## Frontend

- Component `RecommendationRail`: tiêu đề, dải sản phẩm cuộn ngang, dòng lý do dưới mỗi thẻ (nếu có). Hiện khung xương khi đang tải. Lỗi thì ẩn cả khối, không hiện lỗi cho khách.
- Đặt ở trang chủ (`for-you`), trang sản phẩm (`similar`, `bought-together`), giỏ hàng (`cart`).
- Ghi `rec_impression` qua `useImpression` và `rec_click` khi click, kèm `source`, `variant`, vị trí.
- Thêm vào giỏ từ khối gợi ý truyền `source` để `commerce` ghi nhận.

## Testing

- Unit:
  - Chấm điểm và đa dạng hóa (giới hạn theo danh mục và hãng).
  - Bộ lọc loại trừ (đã mua, phụ kiện mua lại được, hết hàng, trong giỏ).
  - Chia nhóm ổn định theo hash.
  - Tính lift cho `co_view` và `co_purchase`.
  - Validate kết quả LLM: id lạ, thiếu, trùng thì dùng thứ tự thuật toán.
- Integration:
  - Job đêm tạo đúng `product_associations` từ dữ liệu sự kiện và đơn hàng dựng sẵn.
  - Khách chưa đồng ý nhận "Bán chạy" và `variant: 'none'`.
  - Lần gọi đầu không chờ LLM; lần sau nhận kết quả đã sắp xếp lại.
  - Hết ngân sách AI thì `algo_llm` rơi về `algo`.
- Kiểm tra bằng hồ sơ dựng sẵn: hồ sơ "laptop gaming, 25 đến 35 triệu" nhận ít nhất 8/12 sản phẩm thuộc Laptop gaming hoặc phụ kiện gaming, giá hợp tầm.
- E2E: khách đồng ý → xem vài laptop gaming → về trang chủ thấy "Dành cho bạn" có laptop gaming và lý do → click → sự kiện có đúng `source` và `variant`.

## Boundaries (riêng module)

- **Always:** chỉ gợi ý sản phẩm `active` còn hàng; giữ nhóm đối chứng khi đang đo; ghi `variant` vào mọi sự kiện hiển thị và click.
- **Ask first:** đổi tỉ lệ nhóm khi thử nghiệm đang chạy; gợi ý dựa trên dữ liệu ngoài hệ thống; thêm vị trí gợi ý mới (ví dụ email).
- **Never:** bắt trang chờ LLM mới hiển thị gợi ý; đưa sản phẩm ngoài danh sách ứng viên vào kết quả LLM; cá nhân hóa cho người chưa đồng ý.

## Success Criteria

- [ ] 4 vị trí gợi ý hoạt động với dữ liệu seed; khách chưa đồng ý không nhận gợi ý dựa trên lịch sử.
- [ ] `similar` của 1 laptop chỉ trả laptop cùng danh mục, giá trong ±30%.
- [ ] Hồ sơ dựng sẵn "laptop gaming 25 đến 35 triệu" nhận ít nhất 8/12 sản phẩm phù hợp.
- [ ] Không bao giờ gợi ý sản phẩm ẩn, hết hàng, đã có trong giỏ, hoặc đã mua (trừ danh mục mua lại được).
- [ ] p95 của `/api/recommendations` dưới 300 ms, kể cả lần đầu (không chờ LLM).
- [ ] Tắt API AI: mọi vị trí vẫn có gợi ý.
- [ ] Báo cáo admin hiện CTR và tỉ lệ chuyển đổi theo vị trí và theo nhóm `control`, `algo`, `algo_llm`.
- [ ] Coverage `service.ts` ≥ 80%.

## Open Questions

1. Tỉ lệ nhóm đối chứng 10% có ổn không? Đối chứng càng lớn thì so sánh càng đáng tin, nhưng càng nhiều khách không được cá nhân hóa.
2. Danh sách cặp danh mục bổ trợ ban đầu, và danh mục nào là "mua lại được" (ví dụ cáp sạc, phụ kiện)? Mặc định: mình soạn theo 5 danh mục seed.
3. Có gửi email gợi ý sản phẩm không? Mặc định: không trong MVP (xem câu hỏi mở của `personalized-content`).
