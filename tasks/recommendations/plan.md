# Implementation Plan: recommendations

## Overview

Module `recommendations` gợi ý sản phẩm ở 4 vị trí: trang chủ, trang sản phẩm (2 khối) và giỏ hàng. Module không train model. Gợi ý dựa trên embedding của `smart-search`, thống kê hành vi và LLM sắp xếp lại kèm lý do.

Module phải đo được hiệu quả. Mỗi khách đã đồng ý thuộc một trong 3 nhóm: `control`, `algo`, `algo_llm`. Báo cáo so sánh tỉ lệ click và tỉ lệ chuyển đổi giữa các nhóm.

`recommendations` phụ thuộc `catalog`, `activity-tracking`, `smart-search`, `ai-gateway`. Module đi sau `smart-search` và song song với `personalized-content`, theo `spec/CAPABILITY-MAP.md`.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-recommendations.md`. Spec còn viết interface theo bản Node. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/recommendations/todo.md`.

## Phần đã phủ bởi plan cũ

Các mục sau đã có. Plan này không tạo task lặp lại.

1. **Mã tính năng AI.** `AiFeature.RecommendationsRerank` và mã `recommendations.rerank` đã có trong `Pickory.Modules.AiGateway.Contracts`.
2. **Bậc ngân sách.** Quyết định 9 của `tasks/identity-ai-gateway/plan.md` đã xếp `recommendations.rerank` vào bậc thấp, dừng ở 100% ngân sách.
3. **Quyền.** `products:read`, `products:write`, `reports:read`, `ai-settings:manage` đã có trong `Permissions.cs`. Module không thêm quyền mới.
4. **Sự kiện catalog.** `ProductChangedEvent` và `StockChangedEvent` đã có trong `Pickory.Modules.Catalog.Contracts` (CAT-03 của `tasks/catalog/todo.md`).
5. **Quy ước `actorKey`.** Dạng `user:<id>` và `anon:<id>` theo quyết định 10 của `tasks/identity-ai-gateway/plan.md`.
6. **Mẫu job nền.** `ImageDeletionWorker` của catalog là mẫu cho worker. Test gọi thẳng processor, không chờ timer.

## Điều kiện trước

1. Checkpoint cuối của `tasks/activity-tracking/plan.md` đạt. Module cần `IProfileReader`, `IActivityMetrics` và trường `variant` trong sự kiện `rec_impression`, `rec_click`.
2. Checkpoint cuối của `tasks/smart-search/plan.md` đạt. Module cần `IProductSearch.SimilarToAsync`.
3. Checkpoint cuối của `tasks/commerce/plan.md` đạt. Module cần `ICommerceReader.GetPurchasedProductIdsAsync` và `ICommerceReports.GetProductSalesAsync`. Trang giỏ hàng (UI của commerce) phải có để gắn khối `cart`.
4. Người dùng đã trả lời Câu hỏi mở 1 đến 5. Các câu này quyết định hợp đồng với module khác.

## Đồ thị phụ thuộc

```
REC-01 logic thuần: chấm điểm, đa dạng hóa, loại trừ, lý do theo mẫu
REC-02 logic thuần: chia nhóm theo hash, lift, kiểm kết quả LLM
 └── REC-03 khung module, Contracts, schema, migration
      ├── REC-04 cặp danh mục bổ trợ (API admin + seed) ─────────── REC-UI4 màn bổ trợ, màn thử nghiệm
      ├── REC-05 job đêm `product_associations` (chờ Câu hỏi mở 1, 2)
      ├── REC-06 bán chạy theo danh mục
      │    └── REC-07 `similar`, `bought-together`, `IRecommender`, `GET /api/recommendations` (cần REC-04, REC-05)
      │         ├── REC-08 `for-you` thuật toán, nhóm, cache ────── REC-UI1 `RecommendationRail`
      │         │    └── REC-09 LLM sắp xếp lại ở nền               ├── REC-UI2 trang sản phẩm
      │         ├── REC-10 `cart` (chờ Câu hỏi mở 3)                ├── REC-UI3 trang chủ (chờ Câu hỏi mở 6)
      │         └── REC-11 API tỉ lệ chia nhóm ───────────────────── └── REC-UI2b giỏ hàng
      │              └── REC-12 báo cáo theo vị trí và nhóm ──────── REC-UI5 màn báo cáo
      └──────────────────────────────────────────────────────────── REC-E2E1
                                                                     └── REC-13 nghiệm thu
```

`REC-07` cần `REC-04` và `REC-05`, vì `bought-together` dùng cặp bổ trợ và `co_purchase`. `REC-09` cần `REC-08`, vì LLM chỉ sắp xếp lại danh sách thuật toán đã tạo. `REC-12` cần `REC-11`, vì báo cáo tách số liệu theo nhóm.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.Recommendations` và `Pickory.Modules.Recommendations.Contracts`. `RecommendationsModule.cs` có `AddRecommendationsModule` và `MapRecommendationsEndpoints`. `Pickory.ArchitectureTests` thêm luật cho module mới.

2. **Chuyển interface từ TypeScript sang .NET.**
   - `recommender.recommend(input)` thành `IRecommender.RecommendAsync(RecommendationRequest request, Actor actor, CancellationToken cancellationToken)`.
   - `RecommendationRequest` có `Placement`, `ProductId`, `Limit`. Không có `CartProductIds`, vì module tự đọc giỏ từ server (quyết định 12).
   - `RecommendationResult` có `Items`, `Variant`, `Source`. Mỗi phần tử có `ProductSummary Product` và `string? Reason`.
   - `RecommendationPlacement` là enum: `ForYou`, `Similar`, `BoughtTogether`, `Cart`. JSON dùng chuỗi `for-you`, `similar`, `bought-together`, `cart` như spec.
   - `RecommendationVariant` là enum: `Control`, `Algo`, `AlgoLlm`, `None`. JSON dùng `control`, `algo`, `algo_llm`, `none`.
   - Các kiểu trên nằm trong `Pickory.Modules.Recommendations.Contracts`. `shopping-assistant` gọi `IRecommender` làm tool.

3. **Luôn lọc lại khi đọc.** Mọi kết quả, kể cả kết quả từ cache, đi qua `ICatalogReader.GetProductsAsync` trước khi trả. Hàm này bỏ sản phẩm khách không thấy. Module bỏ thêm sản phẩm `InStock = false` hoặc `Status` khác `active`. Cache cũ vì thế không bao giờ làm lộ sản phẩm ẩn hoặc hết hàng.

4. **Thông tin danh mục của ứng viên.** `ProductSummary` hiện không có danh mục. Chấm điểm, đa dạng hóa và luật ±30% cần danh mục và giá. Module đề xuất thêm `CategoryRef Category` vào `ProductSummary` (Câu hỏi mở 4). Phương án dự phòng là gọi `GetProductAsync` cho từng ứng viên, chậm hơn nhiều.

5. **Chia nhóm theo hash.** Nhóm lấy từ SHA-256 của `anonymousId`. 4 byte đầu chia lấy dư 100 cho ra vị trí từ 0 đến 99. Vị trí so với tỉ lệ đang cấu hình để chọn nhóm. Khách chưa đồng ý luôn có `variant = none` và không vào thử nghiệm.

6. **Đổi tỉ lệ thì một phần khách đổi nhóm.** Cách chia theo vị trí cố định giữ đa số khách ở nhóm cũ. Màn cấu hình cảnh báo trước khi lưu, vì spec xếp việc đổi tỉ lệ khi đang đo vào nhóm "Ask first".

7. **Không bắt khách chờ LLM.** Lần đầu, `for-you` trả thứ tự thuật toán với lý do theo mẫu. Đồng thời module ghi một dòng vào bảng `recommendation_rerank_jobs`, unique theo `actorKey`. `RecommendationRerankWorker` chạy mỗi 5 giây theo mẫu `ImageDeletionWorker`. Test gọi thẳng `RecommendationRerankProcessor`.
   - Chọn bảng trong DB thay cho `Channel` trong process. Job còn sau khi API khởi động lại, và test đếm được job.
   - Mỗi khách chỉ có 1 job chờ. Khách tải lại trang nhiều lần không tạo thêm lời gọi AI.

8. **LLM sắp xếp lại qua `IAiGateway.ChatJsonAsync`.** `CallContext` dùng `AiFeature.RecommendationsRerank`, `actorKey` của khách và `PromptVersion` dạng `rerank@1`. Prompt chỉ chứa `IProfileReader.GetProfileSummaryAsync` và 24 ứng viên (tên, danh mục, giá, 4 thông số chính). Prompt không chứa `actorKey`.
   - Validator FluentValidation kiểm: đủ 12 id, mọi id thuộc danh sách ứng viên, không trùng, lý do tối đa 80 ký tự.
   - Kết quả sai, `AiInvalidOutputException` hoặc `AiUnavailableException`: giữ thứ tự thuật toán, ghi log cảnh báo.

9. **Hết ngân sách AI.** Processor bắt `AiBudgetExceededException`, rồi ghi ngày hiện tại (giờ `Asia/Ho_Chi_Minh`) vào bảng `recommendation_budget_flags`. Trong ngày bị đánh dấu, nhóm `algo_llm` nhận kết quả của `algo`. `variant` trong sự kiện vẫn là `algo_llm`, và báo cáo loại ngày bị đánh dấu khỏi phép so sánh.

10. **Cache.**
    - `for-you`: bảng `recommendation_cache`, unique theo `(actorKey, placement)`, hạn 6 giờ. Cột `profileFingerprint` là hash của top danh mục trong hồ sơ. Fingerprint đổi thì cache hết hiệu lực.
    - `similar` và `bought-together`: bảng `product_recommendation_cache`, khóa theo `(productId, placement)`. Spec ghi các vị trí này "cache theo sản phẩm" nhưng Data Model chỉ có bảng theo `actorKey`. Plan tách bảng riêng để khóa rõ nghĩa (Câu hỏi mở 7).
    - Nghe `ProductChangedEvent`: xóa cache của sản phẩm đó và cache có chứa sản phẩm đó. Job đêm làm mới toàn bộ.

11. **Đồng ý và xóa dữ liệu.** Module đọc `Actor.Consent.Personalization` ở mỗi request. Cache của khách chưa đồng ý không bao giờ được đọc. Nghe `UserDeletedEvent` để xóa cache và job của `user:<id>`. Xóa cache khi rút đồng ý cần `ConsentChangedEvent` của identity (Câu hỏi mở 5). Trước khi có sự kiện này, job dọn cache hết hạn xóa dòng sau 6 giờ.

12. **Giỏ hàng lấy từ server.** `GET /api/recommendations?placement=cart` không nhận danh sách sản phẩm từ client. Spec `commerce` chưa có hàm đọc giỏ cho module khác. Module đề xuất `ICommerceReader.GetCartProductIdsAsync(Actor actor, CancellationToken cancellationToken)` (Câu hỏi mở 3).

13. **Job đêm `product_associations`.**
    - `co_view` cần lượt xem sản phẩm theo phiên. Đề xuất cổng mới `IActivityEventReader.ListProductViewsAsync(DateTime since, string? cursor, int pageSize, CancellationToken cancellationToken)` trong `Pickory.Modules.ActivityTracking.Contracts`. Mỗi dòng có `actorKey`, `productId`, `occurredAt` (Câu hỏi mở 1).
    - `co_purchase` cần dòng hàng của đơn đã thanh toán. Đề xuất `ICommerceReader.ListPaidOrderLinesAsync(DateTime since, string? cursor, int pageSize, CancellationToken cancellationToken)`. Mỗi dòng có `orderId`, khóa người mua, `productId`, `paidAt` (Câu hỏi mở 2).
    - Điểm dùng lift: `P(A và B) / (P(A) × P(B))`. Cặp có ít hơn 2 lần cùng xuất hiện bị bỏ để giảm nhiễu.
    - Job chạy lúc 02:00 giờ `Asia/Ho_Chi_Minh`. Lệnh `recommendations:compute-associations` chạy một lần cho dev và test.

14. **Bán chạy.** Top theo danh mục trong 30 ngày lấy từ `ICommerceReports.GetProductSalesAsync`. Hàm này đã có trong spec `commerce` cho `admin-console`. Danh mục chưa có đơn thì dùng sản phẩm mới nhất của danh mục qua `ICatalogReader.ListProductsAsync` với `Sort = newest`.

15. **Danh mục mua lại được.** Spec đặt cột `repurchasable` trên `CategoryComplement`, tức trên một cặp danh mục. Tính chất "mua lại được" thuộc về một danh mục, không thuộc về cặp. Plan đề xuất bảng `repurchasable_categories` riêng (Câu hỏi mở 8).

16. **Route theo REST thuần.**
    - `GET /api/recommendations`, `GET` và `PUT /api/admin/recommendations/complements`, `GET` và `PUT /api/admin/recommendations/experiment` đã là tài nguyên. Giữ nguyên.
    - `GET /api/admin/recommendations/report` đổi thành `GET /api/admin/recommendations/metrics?from=&to=`. Lý do: tài nguyên trả về là số liệu, không phải tài liệu báo cáo (Câu hỏi mở 9).

17. **Mã lỗi.** `VALIDATION_ERROR` (400) cho `placement` lạ, thiếu `productId` với `similar` hoặc `bought-together`, `limit` ngoài 1 đến 24, tỉ lệ nhóm có tổng khác 100. `productId` không tồn tại trả danh sách rỗng với 200. Khối gợi ý ở frontend ẩn khi danh sách rỗng, nên 404 không có ích cho khách.

18. **Lỗi không lan sang khách.** Lỗi của `smart-search`, `activity-tracking` hoặc AI trong lúc tạo gợi ý thì module rơi về "Bán chạy" và ghi log. `GET /api/recommendations` chỉ trả 5xx khi DB của chính module lỗi.

19. **Audit log.** `PUT` cặp bổ trợ ghi `recommendations.complements.update`. `PUT` tỉ lệ nhóm ghi `recommendations.experiment.update`. Mỗi thao tác ghi đúng 1 dòng qua `IAuditLogWriter` trong cùng transaction.

20. **Ngày đánh dấu cho `admin-console`.** `admin-console` cần biết ngày nào bị đánh dấu hết ngân sách. Contracts có thêm `IRecommendationReports.GetExperimentAsync(DateRange, CancellationToken)`, cùng tên với `tasks/admin-console/plan.md`. Kết quả có danh sách ngày bị đánh dấu. Hàm này nằm ngoài interface của spec (Câu hỏi mở 10).

21. **Hiệu năng p95 dưới 300 ms.** Đường đọc chỉ dùng cache, bảng `product_associations` và `SimilarToAsync` (không gọi AI). Lệnh `recommendations:benchmark` đo p95 trên DB có tên chứa `bench`, theo mẫu `catalog:benchmark`.

## Task List

Chi tiết từng task ở `tasks/recommendations/todo.md`.

### Phase 1: Nền tảng
- [ ] REC-01 · [ ] REC-02 · [ ] REC-03

### Checkpoint R1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `Recommendations` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

### Phase 2: Nguồn ứng viên và vị trí trang sản phẩm
- [ ] REC-04 · [ ] REC-05 · [ ] REC-06 · [ ] REC-07 · [ ] REC-UI1 · [ ] REC-UI2

### Checkpoint R2
- [ ] Trang sản phẩm có khối `similar` và `bought-together` trên dữ liệu seed
- [ ] Job đêm tạo đúng `product_associations` từ dữ liệu dựng sẵn
- [ ] Review với người dùng

### Phase 3: Cá nhân hóa và thử nghiệm
- [ ] REC-08 · [ ] REC-09 · [ ] REC-10 · [ ] REC-UI3 · [ ] REC-UI2b · [ ] REC-11 · [ ] REC-12 · [ ] REC-UI4 · [ ] REC-UI5

### Checkpoint R3
- [ ] Trang chủ có "Dành cho bạn" kèm lý do. Lần gọi đầu không chờ LLM
- [ ] Hết ngân sách AI thì `algo_llm` rơi về `algo` và ngày đó bị đánh dấu
- [ ] Admin đổi tỉ lệ nhóm và xem báo cáo theo nhóm trên giao diện
- [ ] Review với người dùng

### Phase 4: E2E và nghiệm thu
- [ ] REC-E2E1 · [ ] REC-13

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ mục ghi "khi có API key"
- [ ] Coverage dòng của `Pickory.Modules.Recommendations` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `shopping-assistant`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| 4 vị trí gợi ý chạy với dữ liệu seed; khách chưa đồng ý không nhận gợi ý dựa trên lịch sử | REC-07, REC-08, REC-10, REC-UI2, REC-UI2b, REC-UI3, REC-E2E1 | Test tích hợp từng vị trí trên seed; test khách chưa đồng ý nhận `variant = none` và "Bán chạy"; E2E |
| `similar` của 1 laptop chỉ trả laptop cùng danh mục, giá trong ±30% | REC-07 | Test tích hợp trên seed, kiểm từng phần tử |
| Hồ sơ dựng sẵn "laptop gaming 25 đến 35 triệu" nhận ít nhất 8/12 sản phẩm phù hợp | REC-08, REC-13 | Test tích hợp với bộ catalog dựng sẵn trong test (xem Rủi ro 1) |
| Không gợi ý sản phẩm ẩn, hết hàng, trong giỏ, đã mua (trừ danh mục mua lại được) | REC-01, REC-07, REC-08, REC-10 | Unit test bộ lọc loại trừ; test tích hợp đổi trạng thái sản phẩm sau khi cache |
| p95 của `/api/recommendations` dưới 300 ms, kể cả lần đầu | REC-08, REC-13 | Lệnh `recommendations:benchmark`, output ghi vào báo cáo nghiệm thu |
| Tắt API AI: mọi vị trí vẫn có gợi ý | REC-09, REC-13, REC-E2E1 | Test tích hợp với fake provider báo lỗi; E2E chạy với khóa AI sai |
| Báo cáo admin hiện CTR và chuyển đổi theo vị trí và nhóm `control`, `algo`, `algo_llm` | REC-12, REC-UI5 | Test tích hợp với `IActivityMetrics` dựng sẵn; Vitest và E2E màn báo cáo |
| Coverage ít nhất 80% | REC-13 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| 1. Seed của catalog chỉ có 5 danh mục gốc, không có danh mục "Laptop gaming" | Tiêu chí 8/12 không kiểm được trên seed | Test dựng bộ catalog riêng có danh mục và giá phù hợp. Báo cáo ghi rõ tiêu chí kiểm trên bộ dựng sẵn |
| 2. Hợp đồng với `activity-tracking` và `commerce` chưa có hàm đọc cho job đêm và giỏ hàng | REC-05 và REC-10 bị chặn | Câu hỏi mở 1 đến 3 chốt trước Phase 2. Các task khác không chờ |
| 3. `ProductSummary` thiếu danh mục | Chấm điểm và luật ±30% chậm vì phải đọc chi tiết từng sản phẩm | Câu hỏi mở 4. Nếu không duyệt, cache thông tin danh mục trong module |
| 4. Dữ liệu hành vi ít ở giai đoạn đầu | `co_view` và `co_purchase` rỗng, gợi ý kém | Cặp danh mục bổ trợ và "Bán chạy" làm nguồn dự phòng |
| 5. LLM trả id ngoài danh sách | Gợi ý sản phẩm không có thật | Validator chỉ nhận id ứng viên; sai thì giữ thứ tự thuật toán |
| 6. Đổi tỉ lệ nhóm khi đang đo | Số liệu so sánh lệch | Cảnh báo trên màn cấu hình; audit log ghi thời điểm đổi |
| 7. Chưa có API key AI | Không kiểm được rerank với provider thật | Mọi test dùng fake provider. Contract test thật để mở tới khi có key |

## Câu hỏi mở

1. **Cổng đọc lượt xem cho `co_view`.** Thêm `IActivityEventReader.ListProductViewsAsync` vào `Pickory.Modules.ActivityTracking.Contracts` như quyết định 13 không? Đây là đổi Contracts (Ask first). `REC-05` chờ câu trả lời.
2. **Hàm đọc đơn đã thanh toán cho `co_purchase`.** Thêm `ICommerceReader.ListPaidOrderLinesAsync` như quyết định 13 không? `REC-05` chờ câu trả lời.
3. **Hàm đọc giỏ hàng.** Thêm `ICommerceReader.GetCartProductIdsAsync` như quyết định 12 không? `REC-10` chờ câu trả lời.
4. **Danh mục trong `ProductSummary`.** Thêm `CategoryRef Category` vào `ProductSummary` của catalog (quyết định 4) không? Đây là đổi Contracts của catalog.
5. **Rút đồng ý.** Identity chưa có `ConsentChangedEvent`. Module xóa cache ngay khi có sự kiện này, hay chấp nhận cache hết hạn sau 6 giờ (quyết định 11)?
6. **Trang chủ.** Route `/` hiện là `HealthPage`, chưa có trang chủ cho khách. Module nào dựng trang chủ? Đề xuất: `REC-UI3` dựng trang chủ tối thiểu chứa khối "Dành cho bạn". `personalized-content` gắn banner sau.
7. **Bảng cache theo sản phẩm.** Đồng ý tách `product_recommendation_cache` khỏi `recommendation_cache` (quyết định 10) không? Spec chỉ có một bảng.
8. **Danh mục mua lại được.** Spec đặt `repurchasable` trên cặp danh mục. Plan đề xuất bảng `repurchasable_categories` (quyết định 15). Danh sách ban đầu: chỉ "Phụ kiện" trong 5 danh mục seed?
9. **Route báo cáo.** Đổi `GET /api/admin/recommendations/report` thành `GET /api/admin/recommendations/metrics` (quyết định 16) không? Spec ghi `report`.
10. **Ngày đánh dấu hết ngân sách.** Thêm `IRecommendationReports.GetExperimentAsync` vào Contracts của module (quyết định 20) không? `admin-console` cần hàm này để loại ngày bị đánh dấu.
11. **Câu hỏi mở của spec.** Tỉ lệ nhóm đối chứng 10% giữ như spec? Cặp danh mục bổ trợ ban đầu soạn theo 5 danh mục seed? Không gửi email gợi ý trong MVP?
