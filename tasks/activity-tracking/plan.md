# Implementation Plan: activity-tracking

## Thay đổi phạm vi ngày 2026-10-18

Người dùng chỉ giữ phần đo chuyển đổi. Pickory không còn tính năng AI nào đọc hồ sơ sở thích, vì chatbot chuyển sang dự án riêng `chatbot-rag`.

Module giữ các phần sau:
- ghi sự kiện xem, tìm, thêm giỏ, mua;
- kiểm tra đồng ý, xóa dữ liệu, thời gian lưu 12 tháng;
- bộ đếm theo ngày và phễu cho `admin-console`.

Module bỏ các phần sau:
- hồ sơ sở thích, job tính hồ sơ, `IProfileReader` và bản tóm tắt hồ sơ;
- khối "Đã xem gần đây" và mục sở thích trong `/privacy`;
- CTR, quy chuyển đổi 7 ngày, sự kiện `rec_impression`, `rec_click` và hook `useImpression`.

Task bỏ: ACT-01, ACT-03, ACT-09, ACT-10, ACT-UI2. Task thu hẹp: ACT-02, ACT-04, ACT-06, ACT-UI1, ACT-07, ACT-08, ACT-UI3, ACT-11, ACT-E2E1. Task bỏ vẫn giữ trong `todo.md`, có dòng "Trạng thái: đã bỏ ngày 2026-10-18".

## Overview

Module `activity-tracking` ghi hành vi mua sắm của khách đã đồng ý. Module đếm lượt xem, thêm giỏ và mua theo ngày để `admin-console` dựng phễu chuyển đổi.

`activity-tracking` phụ thuộc `identity`, `catalog` và `commerce`. `admin-console` đọc dữ liệu của module này. Vì vậy module đi ngay sau `commerce`. Ngày 2026-10-14 người dùng bỏ `smart-search`, `recommendations` và `personalized-content`, nên ba module này không còn đọc dữ liệu. Ngày 2026-10-18 người dùng bỏ `shopping-assistant`, vì chatbot chuyển sang dự án riêng `chatbot-rag`.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-activity-tracking.md`. Spec còn viết interface theo bản Node. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/activity-tracking/todo.md`.

## Phần đã phủ bởi plan cũ

Các phần dưới đây đã có. Plan này dùng lại, không tạo task làm lại.

1. **Lưu lựa chọn đồng ý.** ID-08 trong `tasks/identity-ai-gateway/todo.md` đã có bảng `consent_records` và `PUT /api/me/consent`. `Actor.Consent` đã có `Personalization` ở mỗi request.
2. **Banner đồng ý và trang quyền riêng tư.** UI-03 đã có `consent-banner.tsx` và `privacy-settings.tsx` ở route `/privacy`. Mục "Quyền riêng tư" của spec mở rộng trang `/privacy`, không dựng trang mới.
3. **Hàm che PII.** `PiiRedactor` đã có ở `apps/backend/src/Pickory.Shared/Privacy/PiiRedactor.cs`. Module dùng lại hàm này cho câu tìm. `tasks/ai-gateway-removal/` giữ hàm này khi gỡ `ai-gateway`.
4. **Sự kiện của `identity`.** `UserLinkedEvent` và `UserDeletedEvent` đã có trong `Pickory.Modules.Identity.Contracts/Events/`.
5. **Mẫu job nền và lệnh benchmark.** Job theo mẫu `ImageDeletionWorker` của catalog. Lệnh đo hiệu năng theo mẫu `catalog:benchmark` trong `Pickory.Api/Benchmark/CatalogBenchmark.cs`.
6. **Giới hạn tần suất.** SHIP-08 chuyển `RateLimiter` sang `Pickory.Shared`. Module dùng lại lớp này cho `POST /api/events`.
7. **Màn báo cáo.** Spec của module không có giao diện back-office. Dashboard phễu thuộc `admin-console` (`tasks/admin-console/plan.md`). Plan này chỉ làm API và `IActivityMetrics`.

## Điều kiện trước

1. **`commerce` đã xong và phát đủ sự kiện.** Module cần `CartItemAddedEvent`, `CartItemRemovedEvent`, `OrderPlacedEvent`, `OrderPaidEvent` trong `Pickory.Modules.Commerce.Contracts/Events/`. Mỗi sự kiện cần các trường sau:
   - `CartItemAddedEvent`: `AnonymousId`, `UserId`, `SkuId`, `ProductId`, `Quantity`, `Source`.
   - `CartItemRemovedEvent`: `AnonymousId`, `UserId`, `SkuId`, `ProductId`.
   - `OrderPlacedEvent` và `OrderPaidEvent`: `OrderId`, `AnonymousId`, `UserId`, `PaymentMethod`, danh sách dòng hàng có `ProductId`, `UnitPrice`, `Quantity`.
   Nếu sự kiện của `commerce` thiếu trường, reviewer dừng task liên quan và hỏi người dùng.
2. **`identity` phát `ConsentChangedEvent`.** Code hiện tại của `SaveConsentEndpoint` chỉ lưu bản ghi, không phát sự kiện. Spec yêu cầu xóa ngay dữ liệu khi khách rút đồng ý. Thêm sự kiện vào Contracts thuộc nhóm "Ask first" (Câu hỏi mở 1).
3. **`identity` có cổng đọc đồng ý.** Sự kiện của `commerce` đến từ webhook SePay hoặc job, không có `Actor`. Module cần biết khách có đồng ý hay không theo `AnonymousId` và `UserId` (Câu hỏi mở 2).
4. **SHIP-08 xong.** `RateLimiter` đã nằm ở `Pickory.Shared`.

## Đồ thị phụ thuộc

```
ACT-02 logic thuần: phễu
ACT-04 khung module, Contracts, schema, migration
 ├── ACT-05 ConsentChangedEvent và IConsentReader ở identity (chờ duyệt Câu hỏi mở 1, 2)
 ├── ACT-06 POST /api/events: lọc, đồng ý, bộ đếm, ghi nền ─── ACT-UI1 tracker.ts
 │    ├── ACT-12 lệnh activity:benchmark
 │    └── ACT-07 IServerEventTracker, nghe sự kiện commerce (cần ACT-05)
 │         ├── ACT-08 xóa dữ liệu, gắn sự kiện khi đăng nhập, job lưu 12 tháng ─── ACT-UI3 nút xóa lịch sử
 │         └── ACT-11 IActivityMetrics.GetFunnelAsync, API phễu (cần ACT-02)
 └──────────────────────────────────────────────── ACT-E2E1 → ACT-13 nghiệm thu
```

Ngày 2026-10-18 đồ thị bỏ ACT-01, ACT-03, ACT-09, ACT-10 và ACT-UI2.

`ACT-07` cần `ACT-05`, vì sự kiện của `commerce` không mang trạng thái đồng ý. `ACT-08` cần `ACT-05`, vì rút đồng ý chỉ được biết qua `ConsentChangedEvent`.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.ActivityTracking` và `Pickory.Modules.ActivityTracking.Contracts`. `ActivityTrackingModule.cs` có `AddActivityTrackingModule` và `MapActivityTrackingEndpoints`. `Pickory.ArchitectureTests` thêm luật cho module mới.
2. **Chuyển interface từ TypeScript sang .NET.** Spec viết `activityMetrics` và `trackServerEvent` theo bản Node. Plan đổi như sau:
   - `activityMetrics` thành `IActivityMetrics` với `GetFunnelAsync`.
   - `trackServerEvent` thành `IServerEventTracker.TrackAsync`.
   - `Funnel` và `DateRange` là `record` trong Contracts.
   - Ngày 2026-10-18 plan bỏ `IProfileReader`, `GetFeatureMetricsAsync`, `InterestProfile` và `FeatureMetrics`.
   - Mọi hàm trả `Task` và nhận `CancellationToken`.
3. **Contracts không tham chiếu `Actor`.** Hiện không project Contracts nào tham chiếu Contracts khác. `IServerEventTracker` nhận `TrackingSubject(AnonymousId, UserId, Personalization)`. Bên gọi dựng giá trị này từ `Actor`. Cách này giữ ranh giới hiện có.
4. Đã bỏ ngày 2026-10-18. Hồ sơ sở thích không còn. **Khóa hồ sơ có tiền tố.** `actorKey` là `user:<id>` khi có tài khoản, ngược lại là `anon:<id>`. Quy ước này giống `CallContext.ActorKey` của `ai-gateway`. Tiền tố chặn trùng khóa giữa hai loại id.
5. **Validate bằng FluentValidation.** Spec ghi Zod. Mỗi loại sự kiện có một validator. Sự kiện sai bị bỏ, các sự kiện đúng trong cùng lô vẫn được ghi.
6. **`POST /api/events` trả 202 rồi ghi nền.** Endpoint kiểm tra lô, rồi đưa sự kiện vào hàng đợi `Channel` trong bộ nhớ. Một `BackgroundService` ghi DB theo lô. MVP chạy 1 instance, nên hàng đợi trong bộ nhớ đủ dùng. API dừng đột ngột có thể mất sự kiện chưa ghi (xem Rủi ro).
7. **Backend tự lấy định danh.** Endpoint đọc `AnonymousId`, `UserId` và `Consent` từ `Actor`. Trường định danh trong body bị bỏ qua.
8. **Không ghi sự kiện của nhân viên và bot.** `Actor.Kind` là `Staff` thì endpoint trả 202 và không ghi gì. User agent của crawler phổ biến khớp danh sách trong code thì cũng không ghi.
9. **Giới hạn 10 lô mỗi phút theo `AnonymousId`.** Vượt giới hạn trả 429 với mã `TOO_MANY_REQUESTS`, giống identity. Spec ghi "luôn trả 202" và "vượt giới hạn bị chặn". Plan chọn 429 để client biết bị chặn (Câu hỏi mở 4).
10. **Bộ đếm tổng hợp ghi cho mọi khách.** `daily_counters` tăng cho cả khách đồng ý và khách không đồng ý. Ngày tính theo giờ `Asia/Ho_Chi_Minh`. Khóa unique là `(date, metric, source, productId)` theo spec. Ngày 2026-10-18 tên chỉ số bỏ hậu tố nhóm như `purchase.personalized`, vì phễu không còn so sánh nhóm có cá nhân hóa.
11. **Sự kiện tiền ghi ở backend.** `add_to_cart` và `remove_from_cart` đến từ sự kiện giỏ của `commerce`. `purchase` đến từ `OrderPaidEvent` với SePay và từ `OrderPlacedEvent` với COD. Mỗi đơn chỉ tạo 1 sự kiện `purchase`, khóa theo `orderId`.
12. **Không có trường `variant` (đổi ngày 2026-10-14).** Bản trước nhận `variant` cho nhóm thử nghiệm của `recommendations`. Module đó đã bỏ. Sự kiện không có trường `variant`, và số đo không tách theo nhóm thử nghiệm.
13. **Danh sách `source` cố định trong code.** Ví dụ `keyword-search`, `category`. `source` lạ bị bỏ cùng sự kiện đó. Ngày 2026-10-14 danh sách bỏ các `source` của `recommendations` và `smart-search`. Ngày 2026-10-18 danh sách bỏ `source` `shopping-assistant`.
14. Đã bỏ ngày 2026-10-18. Không còn bảng `interest_profiles`. **Hồ sơ là JSON trong `interest_profiles`.** Đọc hồ sơ là 1 truy vấn theo `actorKey`. `recentlyViewed` và `cartProductIds` cập nhật ngay khi ghi sự kiện. Phần còn lại do job 5 phút tính lại, chỉ cho khách có sự kiện mới.
15. Đã bỏ ngày 2026-10-18. Job hồ sơ không còn. ACT-06 vẫn dùng `ICatalogReader.GetProductsAsync` để bỏ sự kiện trỏ tới sản phẩm không có. **Dữ liệu sản phẩm đọc qua `ICatalogReader`.** `ProductDetail` có `Category`, `Brand`, `MinPrice` và `Specs`. `GetAttributeDefinitionsAsync` cho biết thuộc tính nào `Filterable`. Job gọi `GetProductAsync` cho từng sản phẩm và giữ kết quả trong bộ nhớ của một lần chạy. Hàm đọc theo lô sẽ nhanh hơn (Câu hỏi mở 3).
16. Đã bỏ ngày 2026-10-18. Hồ sơ sở thích không còn. **Điểm theo trọng số và bán rã.** Xem 1, xem từ gợi ý 1, thêm giỏ 3, mua 5, bỏ giỏ trừ 1. Chu kỳ bán rã 30 ngày. Tầm giá là khoảng phân vị 25 đến 75. Các hàm này là hàm thuần, nhận `TimeProvider` để test được.
17. Đã bỏ ngày 2026-10-18. Không còn tính năng AI để quy chuyển đổi, và `rec_click` đã bỏ. **Quy chuyển đổi cho lần click gần nhất trong 7 ngày.** Lần mua được tính cho `source` của lần `rec_click` gần nhất vào sản phẩm đó. Ngày 2026-10-18 bỏ sự kiện `chat_product_click`, vì Pickory không còn chatbot riêng. Phần quy chuyển đổi chỉ tính trên khách đã đồng ý, theo spec.
18. Đã bỏ ngày 2026-10-18. Không còn chatbot đọc bản tóm tắt. **Bản tóm tắt hồ sơ không có định danh.** `GetProfileSummaryAsync` chỉ dùng tên danh mục, tên thương hiệu, tầm giá, thuộc tính và tên sản phẩm. Kết quả đi qua `PiiRedactor` lần cuối. Test dùng câu tìm có SĐT và email để kiểm.
19. **Xóa dữ liệu chạy ngay.** Rút đồng ý, `DELETE /api/me/activity` và `UserDeletedEvent` xóa sự kiện trong cùng transaction. Bộ đếm tổng hợp không có định danh nên giữ lại.
20. **Gắn sự kiện khi liên kết tài khoản.** Khi nhận `UserLinkedEvent`, module gắn `userId` cho sự kiện của `anonymousId`. Phía chưa đồng ý không có dữ liệu, nên không có gì để gắn. Ngày 2026-10-18 bỏ bước gộp hồ sơ.
21. **Job theo mẫu `ImageDeletionWorker`.** Một job xóa sự kiện cũ hơn 12 tháng mỗi ngày. Ngày 2026-10-18 bỏ job tính hồ sơ mỗi 5 phút. Test gọi thẳng processor, không chờ timer. MVP 1 instance nên chưa cần khóa giữa nhiều worker (Open Question 3 của spec).
22. **Route theo REST mức tài nguyên.** Route còn lại đều là danh từ: `POST /api/events`, `DELETE /api/me/activity`, `GET /api/admin/analytics/funnel`. Ngày 2026-10-18 plan bỏ `GET /api/me/recently-viewed`, `GET /api/me/interests` và `GET /api/admin/analytics/features`.
23. **Đo hiệu năng bằng lệnh riêng.** Lệnh `activity:benchmark` gửi 100 lô mỗi giây trong 1 phút rồi in p95. Lệnh chỉ chạy khi tên DB chứa `bench`, giống `catalog:benchmark`. Mỗi lô dùng `AnonymousId` khác nhau, vì giới hạn 10 lô mỗi phút áp theo `AnonymousId`.
24. **Sự kiện `search` đến từ frontend (đổi ngày 2026-10-14).** Tìm theo từ khóa nằm ở `catalog`. `catalog` không gọi `activity-tracking`, vì `activity-tracking` phụ thuộc `catalog`.
    - Frontend gửi `search` (câu tìm, số kết quả) qua `POST /api/events` khi trang kết quả có `q`.
    - Backend che câu tìm bằng `PiiRedactor` trước khi ghi.
    - Trang kết quả tìm theo từ khóa chưa có task giao diện (Open Question 5 của `spec/SPEC-catalog.md`).

## Task List

Chi tiết từng task ở `tasks/activity-tracking/todo.md`.

### Phase 1: Nền tảng
- [ ] ACT-02 · [ ] ACT-04 · [ ] ACT-05
- Đã bỏ ngày 2026-10-18: ACT-01, ACT-03

### Checkpoint A1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `ActivityTracking` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

### Phase 2: Thu thập và quyền riêng tư
- [ ] ACT-06 · [ ] ACT-UI1 · [ ] ACT-07 · [ ] ACT-08 · [ ] ACT-UI3

### Checkpoint A2
- [ ] Khách chưa đồng ý duyệt web: không có dòng `activity_events` gắn với họ, bộ đếm vẫn tăng
- [ ] Rút đồng ý hoặc xóa lịch sử: sự kiện bị xóa ngay
- [ ] Review với người dùng

### Phase 3: Hồ sơ sở thích
Đã bỏ ngày 2026-10-18, gồm ACT-09, ACT-10, ACT-UI2 và Checkpoint A3. ACT-UI3 chuyển sang Phase 2 với phạm vi nhỏ hơn.

### Phase 4: Đo hiệu quả
- [ ] ACT-11 · [ ] ACT-12

### Checkpoint A4
- [ ] API phân tích trả đúng phễu với bộ dữ liệu dựng sẵn
- [ ] `POST /api/events` p95 dưới 100 ms ở 100 lô mỗi giây
- [ ] Review với người dùng

### Phase 5: E2E và nghiệm thu
- [ ] ACT-E2E1 · [ ] ACT-13

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.ActivityTracking` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `support-desk`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Khách chưa đồng ý: không có dòng `activity_events` gắn với họ sau khi duyệt web | ACT-06, ACT-07, ACT-UI1, ACT-13 | Test tích hợp gửi lô và sự kiện `commerce` với khách chưa đồng ý; Vitest `tracker.ts` không gửi khi chưa đồng ý |
| Khách đồng ý xem 3 sản phẩm cùng danh mục: hồ sơ có danh mục đó ở vị trí đầu trong 5 phút | Đã bỏ ngày 2026-10-18 | Không kiểm. Hồ sơ sở thích đã bỏ |
| Khách vãng lai đã đồng ý đăng nhập: sự kiện trước đó được gắn vào tài khoản (đổi ngày 2026-10-18) | ACT-08 | Test tích hợp phát `UserLinkedEvent` |
| Xóa lịch sử hoặc rút đồng ý: sự kiện bị xóa ngay | ACT-05, ACT-08, ACT-UI3, ACT-E2E1 | Test tích hợp `DELETE /api/me/activity` và `ConsentChangedEvent`; E2E |
| `getProfileSummary` không bao giờ chứa SĐT, email, tên hay ID | Đã bỏ ngày 2026-10-18 | Không kiểm. Bản tóm tắt hồ sơ đã bỏ. Câu tìm vẫn qua `PiiRedactor` ở ACT-06 |
| Dashboard trả đúng phễu xem, thêm giỏ, mua với bộ dữ liệu dựng sẵn (đổi ngày 2026-10-18) | ACT-02, ACT-11, ACT-E2E1 | Unit test công thức; test tích hợp API phễu với bộ dữ liệu dựng sẵn; E2E. Màn dashboard thuộc `admin-console` |
| `/api/events` p95 dưới 100 ms ở 100 lô mỗi giây | ACT-06, ACT-12 | Output của `activity:benchmark` |
| Coverage ít nhất 80% | ACT-13 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Hàng đợi trong bộ nhớ mất sự kiện khi API dừng đột ngột | Thiếu vài sự kiện hành vi, không mất tiền | Sự kiện tiền ghi từ backend; `BackgroundService` ghi phần còn lại khi API dừng bình thường |
| Chưa được duyệt `ConsentChangedEvent` và `IConsentReader` | Không xóa được dữ liệu khi rút đồng ý; không biết đồng ý khi nhận sự kiện `commerce` | `ACT-05` đi trước `ACT-07` và `ACT-08`; hỏi người dùng ở Checkpoint A1 |
| Câu tìm lưu kèm SĐT hoặc email khách gõ vào | Lưu PII trong `activity_events` | Câu tìm qua `PiiRedactor` trước khi ghi; test có SĐT và email ở ACT-06 |
| Bảng `activity_events` lớn nhanh | Truy vấn số đo chậm | Ba chỉ mục của spec; job xóa sau 12 tháng; số đo đọc bộ đếm tổng hợp khi được |
| Giới hạn tần suất làm benchmark sai | p95 đo trên request bị 429 | Benchmark dùng `AnonymousId` khác nhau và kiểm 0 phản hồi 429 |

## Câu hỏi mở

1. **Thêm `ConsentChangedEvent(AnonymousId, UserId, Personalization, Marketing)` vào `Pickory.Modules.Identity.Contracts/Events/`?** `SaveConsentEndpoint` phát sự kiện sau khi lưu. Đây là đổi Contracts, thuộc nhóm "Ask first". Không có sự kiện này thì tiêu chí "rút đồng ý xóa ngay" không đạt.
2. **Thêm `IConsentReader.GetPersonalizationAsync(anonymousId, userId, cancellationToken)` vào `Pickory.Modules.Identity.Contracts`?** Sự kiện `OrderPaidEvent` đến từ webhook SePay, không có `Actor`. Phương án khác: `commerce` gắn cờ `Personalization` vào sự kiện giỏ và đơn. Phương án này không đúng khi khách đổi lựa chọn giữa lúc đặt đơn và lúc tiền về. Đề xuất: thêm `IConsentReader`.
3. Đã bỏ ngày 2026-10-18. Job hồ sơ không còn, nên không cần hàm này. **Thêm hàm đọc theo lô vào `ICatalogReader`?** Đề xuất `GetProductFactsAsync(productIds, cancellationToken)`. Hàm trả danh mục, thương hiệu, giá thấp nhất và giá trị thuộc tính `Filterable` của nhiều sản phẩm trong 1 truy vấn. Không có hàm này thì job dùng `GetProductAsync` từng sản phẩm.
4. **Mã trả về khi vượt giới hạn tần suất.** Spec ghi endpoint "luôn trả 202" và cũng ghi lô vượt giới hạn "bị chặn". Plan chọn 429 `TOO_MANY_REQUESTS`. Phương án khác là trả 202 và bỏ lô trong im lặng.
5. **Liên kết tài khoản mà lựa chọn sau liên kết là không đồng ý.** ID-08 chọn bản ghi đồng ý mới nhất giữa hai phía. Nếu bản mới nhất là không đồng ý, module có xóa sự kiện của phía đã đồng ý không? Đề xuất: có, xóa ngay, giống rút đồng ý.
6. ~~Cổng đọc hành vi cho `recommendations`.~~ Bỏ ngày 2026-10-14. `recommendations` đã bỏ, nên không cần `IActivityEventReader.ListProductViewsAsync`.
7. Đã bỏ ngày 2026-10-18. Khối "Đã xem gần đây" không còn. **Vị trí khối "Đã xem gần đây".** Route `/` hiện là trang kiểm tra sức khỏe, chưa có trang chủ. Plan đặt khối ở trang sản phẩm `/p/:slug`. `REC-UI3` của `tasks/recommendations/plan.md` từng đề xuất dựng trang chủ tối thiểu. Module đó đã bỏ ngày 2026-10-14, nên chưa có task dựng trang chủ.
8. Đã bỏ ngày 2026-10-18. ACT-E2E1 không còn luồng hồ sơ sở thích. **Danh mục "Laptop gaming" trong seed.** Seed của `catalog` chỉ có danh mục `Laptop` và 1 laptop gaming. `ACT-E2E1` cần 3 laptop gaming và nhãn "Laptop gaming" trong sở thích. Đề xuất: mở rộng seed như Câu hỏi mở 1 của `tasks/smart-search/plan.md` (module đã bỏ ngày 2026-10-14).
   - **Đã chốt (2026-10-12):** spec E2E tự tạo danh mục "Laptop gaming" và 3 sản phẩm qua API admin, như spec checkout của `commerce`. Luồng không phụ thuộc seed dùng chung.
9. **Câu hỏi mở của spec giữ mặc định.** Sự kiện thô giữ 12 tháng. Không dùng Google Analytics hay Facebook Pixel. Chưa cần khóa job khi chạy nhiều instance.
10. **`rec_impression`, `rec_click` và `useImpression` sau khi bỏ `recommendations`.** Đã chốt ngày 2026-10-18: bỏ cả hai sự kiện cùng `useImpression`, vì khối "Đã xem gần đây" cũng bỏ.
11. **Phễu có tách theo `source` không?** Đã chốt ngày 2026-10-18: sự kiện giữ trường `source`, phễu chỉ trả số tổng. Tách theo `source` khi có nhu cầu báo cáo.
12. **Còn cần `activity_events` theo từng khách không?** Đã chốt ngày 2026-10-18: giữ bảng, vì luồng đồng ý và xóa dữ liệu đã thiết kế quanh bảng này.
13. **Nhóm đồng ý cho phép ghi sự kiện theo khách.** Đã chốt ngày 2026-10-18: giữ `Personalization` trong code. Đổi chữ trên banner là task riêng của `identity`, cần người dùng duyệt.
14. **Màn hiện phễu.** Đã chốt ngày 2026-10-18: ADM-UI9 của `tasks/admin-console/todo.md` hiện phễu trên trang báo cáo doanh thu.
