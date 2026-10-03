# Spec: personalized-content

Module id: `personalized-content` · Phụ thuộc: `catalog`, `commerce`, `activity-tracking`, `ai-gateway` · Quy ước chung: `SPEC.md`

## Objective

Dùng LLM viết nội dung giúp khách quyết định nhanh hơn, và nhấn đúng điều mỗi người quan tâm:
1. **Tóm tắt đánh giá:** khách không phải đọc hết review.
2. **Điểm nổi bật theo nhu cầu:** cùng một laptop, game thủ thấy GPU và màn hình tần số quét cao, dân văn phòng thấy pin và cân nặng.
3. **Banner trang chủ theo nhóm khách.**

Yêu cầu cốt lõi là **không bịa**: mọi nội dung chỉ dựa trên thông số và review có thật, và được kiểm tra tự động trước khi hiển thị. Nội dung AI viết được gắn nhãn rõ ràng.

**User stories**
- Là khách, ở trang sản phẩm có từ 3 review trở lên, tôi đọc tóm tắt ưu điểm, nhược điểm và những điều người mua hay nhắc tới.
- Là khách đã đồng ý cá nhân hóa, tôi thấy "Điểm nổi bật cho nhu cầu của bạn" nhấn vào thông số hợp với cách tôi dùng.
- Là khách, ở trang chủ tôi thấy banner và bộ sưu tập hợp với nhóm sản phẩm tôi quan tâm.
- Là admin, tôi xem mọi nội dung AI đã viết, ẩn hoặc tạo lại nội dung sai, và duyệt banner trước khi lên trang chủ.

## Thiết kế

### Nguyên tắc chung

- **Không tạo nội dung trong lúc khách tải trang.** Mọi nội dung được tạo bằng job nền và lưu trong `generated_contents`. Trang chỉ đọc nội dung đã có; chưa có thì hiện phương án dự phòng và đưa vào hàng đợi tạo.
- **Tạo theo nhóm, không theo từng người**, để chi phí không tăng theo số khách. Cá nhân hóa bằng cách chọn đúng phiên bản nội dung cho người đó.
- Ưu tiên thấp trong ngân sách AI. Hết ngân sách thì dừng tạo mới, nội dung đã có vẫn hiển thị.
- Nội dung AI có nhãn "Tóm tắt bởi AI" hoặc "Gợi ý bởi AI", kèm link giải thích ngắn.
- Mỗi lần tạo lưu `promptVersion` và hash của dữ liệu đầu vào, để biết khi nào cần tạo lại và truy được nội dung sinh ra từ đâu.

### Nhóm nhu cầu (persona)

- Mỗi danh mục có danh sách nhóm nhu cầu khai báo trong `personalized-content/personas.ts`, ví dụ:
  - Laptop: `gaming`, `office`, `creator`, `student`.
  - Điện thoại: `camera`, `battery`, `gaming`, `budget`.
  - Tai nghe: `commute`, `gaming`, `audiophile`, `sport`.
- Mỗi nhóm có luật ánh xạ từ hồ sơ sở thích (thuộc tính quan tâm, danh mục con, tầm giá). Ví dụ hồ sơ quan tâm GPU rời và Laptop gaming thì thuộc `gaming`.
- Khách chưa đồng ý hoặc không khớp nhóm nào thì dùng nhóm `general`.

### 1. Tóm tắt đánh giá (`personalized-content.review-summary`)

- Tạo khi sản phẩm có ≥ 3 review đang hiện. Tạo lại khi có thêm 5 review, khi số review tăng 20%, hoặc khi có review bị ẩn.
- Đầu vào: các review đang hiện (tối đa 100 review mới nhất, đã qua `redactPII`, không có tên người viết), cùng điểm trung bình.
- Đầu ra (`chatJson`):

```ts
const reviewSummarySchema = z.object({
  overview: z.string().max(300),
  pros: z.array(z.object({ text: z.string().max(120), mentions: z.number().int(), aspect: z.string() })).max(4),
  cons: z.array(z.object({ text: z.string().max(120), mentions: z.number().int(), aspect: z.string() })).max(3),
  aspects: z.array(z.string()),     // 'pin', 'màn hình', 'tản nhiệt', 'giao hàng'...
});
```

- **Kiểm tra tự động:** `mentions` không vượt số review; mỗi ý ưu hoặc nhược phải có ít nhất 1 review chứa từ khóa của `aspect`. Không đạt thì tạo lại 1 lần, vẫn không đạt thì đánh dấu `failed` và không hiển thị.
- **Cá nhân hóa không tốn thêm lời gọi AI:** khi hiển thị, các ý có `aspect` khớp sở thích của khách (ví dụ khách quan tâm pin) được đưa lên đầu và in đậm.

### 2. Điểm nổi bật theo nhu cầu (`personalized-content.description`)

- Tạo cho mỗi cặp (sản phẩm `active`, nhóm nhu cầu của danh mục), kể cả `general`. Tạo lại khi `contentHash` của sản phẩm đổi (qua `onProductChanged`).
- Đầu vào: tên, danh mục, thông số (nhãn, giá trị, đơn vị), tùy chọn, mô tả ngắn, mô tả nhóm nhu cầu. **Không** đưa giá vào, vì giá đổi thường xuyên.
- Đầu ra: 3 hoặc 4 ý, mỗi ý tối đa 140 ký tự, kèm `basedOn`: danh sách khóa thông số mà ý đó dựa vào.
- **Kiểm tra chống bịa:**
  - Mọi khóa trong `basedOn` phải tồn tại trong thông số của sản phẩm.
  - Mọi con số trong câu (16GB, 165Hz, 5000mAh) phải xuất hiện trong giá trị của các thông số được nhắc tới.
  - Câu không được chứa các từ khẳng định tuyệt đối trong danh sách chặn ("tốt nhất", "số 1", "rẻ nhất", "bảo hành trọn đời"...).
  - Không đạt thì tạo lại 1 lần, vẫn không đạt thì `failed`.
- Hiển thị: khách thuộc nhóm `gaming` thấy bản `gaming`; chưa có bản đó thì dùng `general`; chưa có `general` thì ẩn khối.

### 3. Banner trang chủ (`personalized-content.banner`)

- Tạo cho mỗi cặp (danh mục chính, nhóm nhu cầu) mỗi tuần 1 lần.
- Hệ thống tự chọn bộ sưu tập sản phẩm cho banner bằng truy vấn `catalog`: đúng danh mục, hợp nhóm nhu cầu, còn hàng, tối đa 8 sản phẩm.
- LLM chỉ viết tiêu đề (tối đa 60 ký tự) và câu phụ (tối đa 120 ký tự) dựa trên đặc điểm chung của bộ sưu tập. Ví dụ: "Góc chiến game: laptop RTX từ 25 triệu". Giá trong câu được hệ thống chèn từ dữ liệu thật, LLM không tự viết giá.
- **Banner phải được duyệt** (`products:write`) trước khi hiển thị, vì đây là nội dung marketing nổi bật nhất. Banner chờ duyệt quá 7 ngày thì hết hạn.
- Hiển thị: khách đã đồng ý thấy banner của nhóm phù hợp nhất; khách khác thấy banner `general` hoặc banner tĩnh do admin đặt.

### Mở rộng thêm vào `AiFeature`

`personalized-content.banner` được thêm vào danh sách `AiFeature` của `ai-gateway`, cùng mức ưu tiên thấp như các feature `personalized-content.*` khác.

### Interface public

```ts
// apps/api/src/modules/personalized-content/index.ts
export const contentReader: {
  getReviewSummary(productId: string, actor: Actor): Promise<ReviewSummaryView | null>;
  getHighlights(productId: string, actor: Actor): Promise<HighlightsView | null>;
  getHomeBanners(actor: Actor): Promise<BannerView[]>;
  resolvePersona(categoryId: string, actor: Actor): Promise<string>;   // 'gaming' | ... | 'general'
  pendingApprovalCount(): Promise<number>;                              // banner chờ duyệt, cho badge của admin-console
};
```

`shopping-assistant` dùng `getReviewSummary` và `getHighlights` khi khách hỏi "máy này có tốt không", để trả lời dựa trên dữ liệu đã kiểm tra.

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/products/:id/review-summary` | công khai | Tóm tắt đánh giá (đã sắp theo sở thích nếu có) |
| GET | `/api/products/:id/highlights` | công khai | Điểm nổi bật theo nhóm nhu cầu của khách |
| GET | `/api/home/banners` | công khai | Banner trang chủ |
| GET | `/api/admin/generated-contents` | `products:read` | Danh sách nội dung AI, lọc theo loại, trạng thái, sản phẩm |
| POST | `/api/admin/generated-contents/:id/hide` · `/regenerate` | `products:write` | Ẩn, tạo lại |
| POST | `/api/admin/generated-contents/:id/approve` | `products:write` | Duyệt banner |
| PUT | `/api/admin/home/static-banners` | `products:write` | Banner tĩnh dự phòng |

## Data Model (Prisma, rút gọn)

```prisma
model GeneratedContent {
  id            String   @id @default(uuid())
  kind          String   // review_summary | highlights | banner
  productId     String?
  categoryId    String?
  persona       String?  // null với review_summary
  inputHash     String
  promptVersion String
  content       Json
  status        String   // active | pending_approval | hidden | failed | expired
  failReason    String?
  generatedAt   DateTime @default(now())
  approvedBy    String?
  @@unique([kind, productId, persona])
  @@map("generated_contents")
}

model ContentJob { /* kind, productId?, categoryId?, persona?, enqueuedAt, attempts, lastError */ }
model StaticBanner { /* imagePublicId, title, link, position, activeFrom, activeTo */ }
```

## Frontend

- Trang sản phẩm: khối "Khách hàng nói gì" (tóm tắt, ưu nhược, nhãn AI) đặt trên danh sách review; khối "Điểm nổi bật cho nhu cầu của bạn" cạnh bảng thông số. Không có nội dung thì ẩn khối.
- Trang chủ: carousel banner, banner tĩnh làm dự phòng.
- Back-office: danh sách nội dung AI; xem nội dung kèm dữ liệu đầu vào; ẩn, tạo lại, duyệt banner.

## Testing

- Unit:
  - Ánh xạ hồ sơ sang nhóm nhu cầu.
  - Bộ kiểm tra chống bịa: số không có trong thông số, khóa `basedOn` lạ, từ khẳng định tuyệt đối, `mentions` vượt số review, `aspect` không có trong review nào.
  - Điều kiện tạo lại (số review tăng, `contentHash` đổi).
  - Sắp xếp ý theo sở thích.
- Integration:
  - Đọc khi chưa có nội dung thì trả `null` và tạo đúng 1 job.
  - Job dùng fake provider tạo nội dung `active`.
  - Fake provider trả nội dung bịa thì nội dung thành `failed` sau 2 lần.
  - Banner chưa duyệt không hiển thị.
  - Hết ngân sách thì job dừng, nội dung cũ vẫn trả.
- Kiểm tra với AI thật (`AI_LIVE_TESTS=1`): tạo điểm nổi bật cho toàn bộ sản phẩm seed với mọi nhóm, in tỉ lệ đạt kiểm tra tự động. Xuất 20 mẫu ngẫu nhiên ra file để người duyệt chấm theo thang "đúng, hữu ích, tự nhiên".
- E2E: khách thuộc nhóm `gaming` và khách nhóm `general` xem cùng 1 laptop thấy 2 bộ điểm nổi bật khác nhau.

## Boundaries (riêng module)

- **Always:** kiểm tra tự động trước khi hiển thị; gắn nhãn nội dung AI; lưu dữ liệu đầu vào dạng hash và `promptVersion`.
- **Ask first:** tạo nội dung theo từng người (chi phí tăng theo số khách); bỏ bước duyệt banner; dùng nội dung AI cho email hay mạng xã hội.
- **Never:** để LLM viết giá, khuyến mãi hay chính sách bảo hành; tạo nội dung trong lúc khách chờ tải trang; đưa tên người viết review vào prompt.

## Success Criteria

- [ ] Sản phẩm seed có ≥ 3 review đều có tóm tắt đánh giá; sản phẩm dưới 3 review thì không hiện khối.
- [ ] Mọi điểm nổi bật đang `active` qua được bộ kiểm tra chống bịa (chạy lại kiểm tra trên toàn bộ dữ liệu, 0 vi phạm).
- [ ] Ở lần chạy với AI thật trên dữ liệu seed, ít nhất 90% nội dung đạt kiểm tra ngay lần đầu; 20 mẫu người duyệt chấm có ít nhất 18 mẫu đạt.
- [ ] Hai khách thuộc nhóm nhu cầu khác nhau thấy điểm nổi bật khác nhau cho cùng 1 sản phẩm.
- [ ] Trang sản phẩm không bao giờ chờ LLM: p95 của 2 endpoint đọc dưới 150 ms.
- [ ] Banner chưa duyệt không xuất hiện; admin duyệt xong thì xuất hiện cho đúng nhóm.
- [ ] Tắt API AI hoặc hết ngân sách: trang vẫn hiển thị nội dung đã có, không lỗi.
- [ ] Coverage `service.ts` ≥ 80%.

## Open Questions

1. Danh sách nhóm nhu cầu cho 5 danh mục seed ở trên có hợp với khách của shop không?
2. Có làm email cá nhân hóa (ví dụ nhắc giỏ hàng bị bỏ quên, kèm gợi ý) trong MVP không? Cần dịch vụ email và khách đồng ý nhóm `marketing`. Mặc định: chưa.
3. Có cho admin sửa tay nội dung AI (thay vì chỉ ẩn hoặc tạo lại) không? Mặc định: chưa, để tránh nội dung sửa tay bị ghi đè khi tạo lại.
