# Implementation Plan: activity-tracking

## Overview

Module `activity-tracking` ghi hành vi mua sắm của khách đã đồng ý cá nhân hóa. Module dựng hồ sơ sở thích cho từng khách. Module cũng đo tỉ lệ click (CTR) và tỉ lệ chuyển đổi theo `source` của từng tính năng AI.

`activity-tracking` phụ thuộc `identity`, `catalog` và `commerce`. `smart-search`, `recommendations`, `personalized-content`, `shopping-assistant` và `admin-console` đọc dữ liệu của module này. Vì vậy module đi ngay sau `commerce`.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-activity-tracking.md`. Spec còn viết interface theo bản Node. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/activity-tracking/todo.md`.

## Phần đã phủ bởi plan cũ

Các phần dưới đây đã có. Plan này dùng lại, không tạo task làm lại.

1. **Lưu lựa chọn đồng ý.** ID-08 trong `tasks/identity-ai-gateway/todo.md` đã có bảng `consent_records` và `PUT /api/me/consent`. `Actor.Consent` đã có `Personalization` ở mỗi request.
2. **Banner đồng ý và trang quyền riêng tư.** UI-03 đã có `consent-banner.tsx` và `privacy-settings.tsx` ở route `/privacy`. Mục "Quyền riêng tư" của spec mở rộng trang `/privacy`, không dựng trang mới.
3. **Hàm che PII.** `PiiRedactor` đã có ở `apps/backend/src/Pickory.Shared/Privacy/PiiRedactor.cs`. Module dùng lại hàm này cho câu tìm và bản tóm tắt hồ sơ.
4. **Sự kiện của `identity`.** `UserLinkedEvent` và `UserDeletedEvent` đã có trong `Pickory.Modules.Identity.Contracts/Events/`.
5. **Mẫu job nền và lệnh benchmark.** Job theo mẫu `ImageDeletionWorker` của catalog. Lệnh đo hiệu năng theo mẫu `catalog:benchmark` trong `Pickory.Api/Benchmark/CatalogBenchmark.cs`.
6. **Giới hạn tần suất.** SHIP-08 chuyển `RateLimiter` sang `Pickory.Shared`. Module dùng lại lớp này cho `POST /api/events`.
7. **Màn báo cáo.** Spec của module không có giao diện back-office. Dashboard CTR và phễu thuộc `admin-console` (`tasks/admin-console/plan.md`). Plan này chỉ làm API và `IActivityMetrics`.

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
ACT-01 logic thuần: điểm, bán rã, tầm giá, thuộc tính, gộp hồ sơ
ACT-02 logic thuần: CTR, quy chuyển đổi 7 ngày, phễu
ACT-03 logic thuần: bản tóm tắt hồ sơ, không có PII
 └── ACT-04 khung module, Contracts, schema, migration
      ├── ACT-05 ConsentChangedEvent và IConsentReader ở identity (chờ duyệt Câu hỏi mở 1, 2)
      ├── ACT-06 POST /api/events: lọc, đồng ý, bộ đếm, ghi nền ─── ACT-UI1 tracker.ts, useImpression
      │    └── ACT-07 IServerEventTracker, nghe sự kiện commerce (cần ACT-05)
      │         └── ACT-08 xóa dữ liệu, gộp khi đăng nhập, job lưu 12 tháng (cần ACT-05)
      │              └── ACT-09 job hồ sơ 5 phút, IProfileReader
      │                   └── ACT-10 GET /api/me/recently-viewed, /api/me/interests ─── ACT-UI2, ACT-UI3
      │                        └── ACT-11 IActivityMetrics, API phân tích admin
      │                             └── ACT-12 lệnh activity:benchmark
      └──────────────────────────────────────────────── ACT-E2E1 → ACT-13 nghiệm thu
```

`ACT-07` cần `ACT-05`, vì sự kiện của `commerce` không mang trạng thái đồng ý. `ACT-08` cần `ACT-05`, vì rút đồng ý chỉ được biết qua `ConsentChangedEvent`.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.ActivityTracking` và `Pickory.Modules.ActivityTracking.Contracts`. `ActivityTrackingModule.cs` có `AddActivityTrackingModule` và `MapActivityTrackingEndpoints`. `Pickory.ArchitectureTests` thêm luật cho module mới.
2. **Chuyển interface từ TypeScript sang .NET.** Spec viết `profileReader`, `activityMetrics`, `trackServerEvent` theo bản Node. Plan đổi như sau:
   - `profileReader` thành `IProfileReader` với `GetProfileAsync` và `GetProfileSummaryAsync`.
   - `activityMetrics` thành `IActivityMetrics` với `GetFeatureMetricsAsync` và `GetFunnelAsync`.
   - `trackServerEvent` thành `IServerEventTracker.TrackAsync`.
   - `InterestProfile`, `FeatureMetrics`, `Funnel`, `DateRange` là `record` trong Contracts.
   - Mọi hàm trả `Task` và nhận `CancellationToken`.
3. **Contracts không tham chiếu `Actor`.** Hiện không project Contracts nào tham chiếu Contracts khác. `IProfileReader` nhận `TrackingSubject(AnonymousId, UserId, Personalization)`. Bên gọi dựng giá trị này từ `Actor`. Cách này giữ ranh giới hiện có.
4. **Khóa hồ sơ có tiền tố.** `actorKey` là `user:<id>` khi có tài khoản, ngược lại là `anon:<id>`. Quy ước này giống `CallContext.ActorKey` của `ai-gateway`. Tiền tố chặn trùng khóa giữa hai loại id.
5. **Validate bằng FluentValidation.** Spec ghi Zod. Mỗi loại sự kiện có một validator. Sự kiện sai bị bỏ, các sự kiện đúng trong cùng lô vẫn được ghi.
6. **`POST /api/events` trả 202 rồi ghi nền.** Endpoint kiểm tra lô, rồi đưa sự kiện vào hàng đợi `Channel` trong bộ nhớ. Một `BackgroundService` ghi DB theo lô. MVP chạy 1 instance, nên hàng đợi trong bộ nhớ đủ dùng. API dừng đột ngột có thể mất sự kiện chưa ghi (xem Rủi ro).
7. **Backend tự lấy định danh.** Endpoint đọc `AnonymousId`, `UserId` và `Consent` từ `Actor`. Trường định danh trong body bị bỏ qua.
8. **Không ghi sự kiện của nhân viên và bot.** `Actor.Kind` là `Staff` thì endpoint trả 202 và không ghi gì. User agent của crawler phổ biến khớp danh sách trong code thì cũng không ghi.
9. **Giới hạn 10 lô mỗi phút theo `AnonymousId`.** Vượt giới hạn trả 429 với mã `TOO_MANY_REQUESTS`, giống identity. Spec ghi "luôn trả 202" và "vượt giới hạn bị chặn". Plan chọn 429 để client biết bị chặn (Câu hỏi mở 4).
10. **Bộ đếm tổng hợp ghi cho mọi khách.** `daily_counters` tăng cho cả khách đồng ý và khách không đồng ý. Ngày tính theo giờ `Asia/Ho_Chi_Minh`. Tên chỉ số có hậu tố nhóm, ví dụ `purchase.personalized` và `purchase.anonymous`, để phễu so sánh được hai nhóm. Khóa unique là `(date, metric, source, productId)` theo spec.
11. **Sự kiện tiền ghi ở backend.** `add_to_cart` và `remove_from_cart` đến từ sự kiện giỏ của `commerce`. `purchase` đến từ `OrderPaidEvent` với SePay và từ `OrderPlacedEvent` với COD. Mỗi đơn chỉ tạo 1 sự kiện `purchase`, khóa theo `orderId`.
12. **Trường `variant`.** `rec_impression` và `rec_click` nhận `variant` tùy chọn: `control`, `algo`, `algo_llm`, `none`. Bộ đếm và số đo tách theo `variant` khi có.
13. **Danh sách `source` cố định trong code.** Ví dụ `recommendations.for-you`, `recommendations.similar`, `recommendations.bought-together`, `recommendations.cart`, `shopping-assistant`, `smart-search`, `category`. `source` lạ bị bỏ cùng sự kiện đó.
14. **Hồ sơ là JSON trong `interest_profiles`.** Đọc hồ sơ là 1 truy vấn theo `actorKey`. `recentlyViewed` và `cartProductIds` cập nhật ngay khi ghi sự kiện. Phần còn lại do job 5 phút tính lại, chỉ cho khách có sự kiện mới.
15. **Dữ liệu sản phẩm đọc qua `ICatalogReader`.** `ProductDetail` có `Category`, `Brand`, `MinPrice` và `Specs`. `GetAttributeDefinitionsAsync` cho biết thuộc tính nào `Filterable`. Job gọi `GetProductAsync` cho từng sản phẩm và giữ kết quả trong bộ nhớ của một lần chạy. Hàm đọc theo lô sẽ nhanh hơn (Câu hỏi mở 3).
16. **Điểm theo trọng số và bán rã.** Xem 1, xem từ gợi ý 1, thêm giỏ 3, mua 5, bỏ giỏ trừ 1. Chu kỳ bán rã 30 ngày. Tầm giá là khoảng phân vị 25 đến 75. Các hàm này là hàm thuần, nhận `TimeProvider` để test được.
17. **Quy chuyển đổi cho lần click gần nhất trong 7 ngày.** Lần mua được tính cho `source` của lần `rec_click` hoặc `chat_product_click` gần nhất vào sản phẩm đó. Phần quy chuyển đổi chỉ tính trên khách đã đồng ý, theo spec.
18. **Bản tóm tắt hồ sơ không có định danh.** `GetProfileSummaryAsync` chỉ dùng tên danh mục, tên thương hiệu, tầm giá, thuộc tính và tên sản phẩm. Kết quả đi qua `PiiRedactor` lần cuối. Test dùng câu tìm có SĐT và email để kiểm.
19. **Xóa dữ liệu chạy ngay.** Rút đồng ý, `DELETE /api/me/activity` và `UserDeletedEvent` xóa sự kiện và hồ sơ trong cùng transaction. Bộ đếm tổng hợp không có định danh nên giữ lại.
20. **Gộp khi liên kết tài khoản.** Khi nhận `UserLinkedEvent`, module gắn `userId` cho sự kiện của `anonymousId`. Module gộp hồ sơ `anon:<id>` vào `user:<id>`. Phía chưa đồng ý không có dữ liệu, nên chỉ dữ liệu đã có mới được gộp.
21. **Job theo mẫu `ImageDeletionWorker`.** Hai job: tính hồ sơ mỗi 5 phút, xóa sự kiện cũ hơn 12 tháng mỗi ngày. Test gọi thẳng processor, không chờ timer. MVP 1 instance nên chưa cần khóa giữa nhiều worker (Open Question 3 của spec).
22. **Route theo REST mức tài nguyên.** Mọi route của spec đã là danh từ: `POST /api/events`, `GET /api/me/recently-viewed`, `GET /api/me/interests`, `DELETE /api/me/activity`, `GET /api/admin/analytics/features`, `GET /api/admin/analytics/funnel`. Plan giữ nguyên các route này.
23. **Đo hiệu năng bằng lệnh riêng.** Lệnh `activity:benchmark` gửi 100 lô mỗi giây trong 1 phút rồi in p95. Lệnh chỉ chạy khi tên DB chứa `bench`, giống `catalog:benchmark`. Mỗi lô dùng `AnonymousId` khác nhau, vì giới hạn 10 lô mỗi phút áp theo `AnonymousId`.

## Task List

Chi tiết từng task ở `tasks/activity-tracking/todo.md`.

### Phase 1: Nền tảng
- [ ] ACT-01 · [ ] ACT-02 · [ ] ACT-03 · [ ] ACT-04 · [ ] ACT-05

### Checkpoint A1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `ActivityTracking` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

### Phase 2: Thu thập và quyền riêng tư
- [ ] ACT-06 · [ ] ACT-UI1 · [ ] ACT-07 · [ ] ACT-08

### Checkpoint A2
- [ ] Khách chưa đồng ý duyệt web: không có dòng `activity_events` gắn với họ, bộ đếm vẫn tăng
- [ ] Rút đồng ý hoặc xóa lịch sử: sự kiện và hồ sơ bị xóa ngay
- [ ] Review với người dùng

### Phase 3: Hồ sơ sở thích
- [ ] ACT-09 · [ ] ACT-10 · [ ] ACT-UI2 · [ ] ACT-UI3

### Checkpoint A3
- [ ] Khách đồng ý xem 3 sản phẩm cùng danh mục: hồ sơ có danh mục đó ở vị trí đầu trong 5 phút
- [ ] Trang `/privacy` hiện sở thích và xóa được lịch sử
- [ ] Review với người dùng

### Phase 4: Đo hiệu quả
- [ ] ACT-11 · [ ] ACT-12

### Checkpoint A4
- [ ] API phân tích trả đúng CTR và chuyển đổi theo `source` và `variant` với bộ dữ liệu dựng sẵn
- [ ] `POST /api/events` p95 dưới 100 ms ở 100 lô mỗi giây
- [ ] Review với người dùng

### Phase 5: E2E và nghiệm thu
- [ ] ACT-E2E1 · [ ] ACT-13

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.ActivityTracking` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `smart-search`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Khách chưa đồng ý: không có dòng `activity_events` gắn với họ sau khi duyệt web | ACT-06, ACT-07, ACT-UI1, ACT-13 | Test tích hợp gửi lô và sự kiện `commerce` với khách chưa đồng ý; Vitest `tracker.ts` không gửi khi chưa đồng ý |
| Khách đồng ý xem 3 sản phẩm cùng danh mục: hồ sơ có danh mục đó ở vị trí đầu trong 5 phút | ACT-01, ACT-09, ACT-E2E1 | Unit test điểm; test tích hợp gọi processor; chu kỳ job là 5 phút; E2E |
| Khách vãng lai đã đồng ý đăng nhập: lịch sử xem trước đó có trong hồ sơ tài khoản | ACT-01, ACT-08 | Unit test gộp hồ sơ; test tích hợp phát `UserLinkedEvent` |
| Xóa lịch sử hoặc rút đồng ý: sự kiện và hồ sơ bị xóa ngay | ACT-05, ACT-08, ACT-UI3, ACT-E2E1 | Test tích hợp `DELETE /api/me/activity` và `ConsentChangedEvent`; E2E |
| `getProfileSummary` không bao giờ chứa SĐT, email, tên hay ID | ACT-03, ACT-09 | Unit test với câu tìm có SĐT và email; test tích hợp `GetProfileSummaryAsync` |
| Dashboard trả đúng CTR và chuyển đổi theo `source` với bộ dữ liệu dựng sẵn | ACT-02, ACT-11 | Unit test công thức; test tích hợp API phân tích với bộ dữ liệu dựng sẵn. Màn dashboard thuộc `admin-console` |
| `/api/events` p95 dưới 100 ms ở 100 lô mỗi giây | ACT-06, ACT-12 | Output của `activity:benchmark` |
| Coverage ít nhất 80% | ACT-13 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Hàng đợi trong bộ nhớ mất sự kiện khi API dừng đột ngột | Thiếu vài sự kiện hành vi, không mất tiền | Sự kiện tiền ghi từ backend; `BackgroundService` ghi phần còn lại khi API dừng bình thường |
| Chưa được duyệt `ConsentChangedEvent` và `IConsentReader` | Không xóa được dữ liệu khi rút đồng ý; không biết đồng ý khi nhận sự kiện `commerce` | `ACT-05` đi trước `ACT-07` và `ACT-08`; hỏi người dùng ở Checkpoint A1 |
| Job hồ sơ gọi `GetProductAsync` nhiều lần | Job chậm khi nhiều khách | Giữ kết quả trong bộ nhớ của một lần chạy; đề xuất hàm đọc theo lô (Câu hỏi mở 3) |
| Bản tóm tắt hồ sơ lọt PII từ tên sản phẩm hoặc câu tìm | Gửi PII sang API AI | Chỉ dùng trường đã chọn; qua `PiiRedactor` lần cuối; test có SĐT và email |
| Bảng `activity_events` lớn nhanh | Truy vấn số đo chậm | Ba chỉ mục của spec; job xóa sau 12 tháng; số đo đọc bộ đếm tổng hợp khi được |
| Giới hạn tần suất làm benchmark sai | p95 đo trên request bị 429 | Benchmark dùng `AnonymousId` khác nhau và kiểm 0 phản hồi 429 |

## Câu hỏi mở

1. **Thêm `ConsentChangedEvent(AnonymousId, UserId, Personalization, Marketing)` vào `Pickory.Modules.Identity.Contracts/Events/`?** `SaveConsentEndpoint` phát sự kiện sau khi lưu. Đây là đổi Contracts, thuộc nhóm "Ask first". Không có sự kiện này thì tiêu chí "rút đồng ý xóa ngay" không đạt.
2. **Thêm `IConsentReader.GetPersonalizationAsync(anonymousId, userId, cancellationToken)` vào `Pickory.Modules.Identity.Contracts`?** Sự kiện `OrderPaidEvent` đến từ webhook SePay, không có `Actor`. Phương án khác: `commerce` gắn cờ `Personalization` vào sự kiện giỏ và đơn. Phương án này không đúng khi khách đổi lựa chọn giữa lúc đặt đơn và lúc tiền về. Đề xuất: thêm `IConsentReader`.
3. **Thêm hàm đọc theo lô vào `ICatalogReader`?** Đề xuất `GetProductFactsAsync(productIds, cancellationToken)`. Hàm trả danh mục, thương hiệu, giá thấp nhất và giá trị thuộc tính `Filterable` của nhiều sản phẩm trong 1 truy vấn. Không có hàm này thì job dùng `GetProductAsync` từng sản phẩm.
4. **Mã trả về khi vượt giới hạn tần suất.** Spec ghi endpoint "luôn trả 202" và cũng ghi lô vượt giới hạn "bị chặn". Plan chọn 429 `TOO_MANY_REQUESTS`. Phương án khác là trả 202 và bỏ lô trong im lặng.
5. **Gộp khi liên kết mà lựa chọn sau gộp là không đồng ý.** ID-08 chọn bản ghi đồng ý mới nhất giữa hai phía. Nếu bản mới nhất là không đồng ý, module có xóa dữ liệu của phía đã đồng ý không? Đề xuất: có, xóa ngay, giống rút đồng ý.
6. **Cổng đọc hành vi cho `recommendations`.** Job `co_view` của `recommendations` cần danh sách phiên xem sản phẩm. Spec của module này chưa có hàm đó. Đề xuất thêm `IActivityEventReader.ListProductViewsAsync` vào Contracts khi làm `recommendations`, theo quyết định 13 của `tasks/recommendations/plan.md`. Plan này chưa tạo task cho hàm đó.
7. **Vị trí khối "Đã xem gần đây".** Route `/` hiện là trang kiểm tra sức khỏe, chưa có trang chủ. Plan đặt khối ở trang sản phẩm `/p/:slug`. `REC-UI3` của `tasks/recommendations/plan.md` đề xuất dựng trang chủ tối thiểu.
8. **Danh mục "Laptop gaming" trong seed.** Seed của `catalog` chỉ có danh mục `Laptop` và 1 laptop gaming. `ACT-E2E1` cần 3 laptop gaming và nhãn "Laptop gaming" trong sở thích. Đề xuất: mở rộng seed như Câu hỏi mở 1 của `tasks/smart-search/plan.md`.
9. **Câu hỏi mở của spec giữ mặc định.** Sự kiện thô giữ 12 tháng. Không dùng Google Analytics hay Facebook Pixel. Chưa cần khóa job khi chạy nhiều instance.
