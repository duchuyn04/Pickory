# Task List: recommendations

Plan và quyết định thiết kế: `tasks/recommendations/plan.md`. Spec: `spec/SPEC-recommendations.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Test tự động dùng fake provider của `ai-gateway`, không gọi AI thật.
- Task UI kiểm bằng Playwright: console 0 lỗi và 0 cảnh báo, ảnh chụp 1280 px và 375 px không tràn ngang.
- Kích thước: XS 1 file, S 1-2, M 3-5, L 6-8. Số file không tính file test.

---

## Phase 1: Nền tảng

### REC-01: Logic thuần cho chấm điểm, đa dạng hóa và loại trừ
**Mô tả:** Các hàm thuần, không gọi DB và không gọi HTTP.
- `CandidateScorer.Score`: tổng có trọng số của độ hợp danh mục và hãng, độ hợp tầm giá, điểm hành vi, độ mới.
- `CandidateDiversifier.Diversify`: chọn top N với giới hạn theo danh mục và theo hãng.
- `CandidateExclusion.Apply`: bỏ sản phẩm theo các luật loại trừ của spec.
- `TemplateReason.For`: lý do theo mẫu, ví dụ "Vì bạn quan tâm Laptop".

**Tiêu chí nghiệm thu:**
- [ ] Sản phẩm hợp danh mục, hãng và tầm giá có điểm cao hơn sản phẩm chỉ hợp danh mục
- [ ] Top 12 có tối đa 4 sản phẩm mỗi danh mục và 3 sản phẩm mỗi hãng
- [ ] Thiếu ứng viên đa dạng thì trả ít hơn 12, không lặp sản phẩm
- [ ] Loại sản phẩm không `active`, hết hàng, đã có trong giỏ, vừa xem trong 10 phút
- [ ] Loại sản phẩm đã mua, trừ sản phẩm thuộc danh mục mua lại được
- [ ] Lý do theo mẫu chỉ nhắc danh mục hoặc hãng có trong hồ sơ, tối đa 80 ký tự
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.Recommendations/Ranking/{CandidateScorer.cs, CandidateDiversifier.cs, CandidateExclusion.cs, TemplateReason.cs}`, test trong `apps/backend/tests/Pickory.UnitTests/Modules/Recommendations/`

### REC-02: Logic thuần cho chia nhóm, lift và kết quả LLM
**Mô tả:** Các hàm thuần, không gọi DB và không gọi HTTP.
- `VariantAssigner.Assign(anonymousId, settings)`: chọn nhóm theo quyết định 5 của plan.
- `AssociationLift.Compute`: điểm lift cho `co_view` và `co_purchase` theo quyết định 13.
- `RerankResultValidator`: validator FluentValidation cho kết quả LLM theo quyết định 8.

**Tiêu chí nghiệm thu:**
- [ ] Cùng `anonymousId` luôn ra cùng nhóm qua nhiều lần gọi
- [ ] 10.000 `anonymousId` ngẫu nhiên chia theo tỉ lệ 10/45/45 với sai lệch dưới 2 điểm phần trăm
- [ ] `control = 0` thì không khách nào vào nhóm `control`
- [ ] Lift của cặp chỉ cùng xuất hiện vì cả hai bán chạy thấp hơn cặp hiếm nhưng luôn đi cùng nhau
- [ ] Cặp có ít hơn 2 lần cùng xuất hiện bị bỏ
- [ ] Validator từ chối kết quả có id lạ, thiếu id, id trùng, lý do dài hơn 80 ký tự
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.Recommendations/{Experiments/VariantAssigner.cs, Associations/AssociationLift.cs, Rerank/RerankResultValidator.cs}`

### REC-03: Khung module, Contracts và schema
**Mô tả:** Tạo `Pickory.Modules.Recommendations` và `Pickory.Modules.Recommendations.Contracts`. Contracts có `IRecommender`, `RecommendationRequest`, `RecommendationResult`, `RecommendationPlacement`, `RecommendationVariant` theo quyết định 2. Thêm `RecommendationsDbContext`, migration đầu tiên và `RecommendationsModule.cs`.

**Tiêu chí nghiệm thu:**
- [ ] Bảng `product_associations`, `recommendation_cache`, `product_recommendation_cache`, `category_complements`, `repurchasable_categories`, `recommendation_settings`, `recommendation_rerank_jobs`, `recommendation_budget_flags`
- [ ] `product_associations` có khóa chính `(productId, relatedProductId, type)`. `recommendation_cache` unique theo `(actorKey, placement)`. `recommendation_rerank_jobs` unique theo `actorKey`
- [ ] `recommendation_settings` có 1 dòng mặc định `control = 10`, `algo = 45`, `algoLlm = 45`
- [ ] Migration chạy trên DB sạch. Lệnh `e2e:start` và fixture test tích hợp dựng được schema mới
- [ ] `Pickory.ArchitectureTests` chặn `Recommendations` tham chiếu module khác ngoài project Contracts
- [ ] `Program.cs` chỉ gọi `AddRecommendationsModule` và `MapRecommendationsEndpoints`
- [ ] JSON của `RecommendationPlacement` và `RecommendationVariant` dùng chuỗi `for-you`, `algo_llm` như spec
**Kiểm tra:** build, test kiến trúc, test tích hợp schema
**Phụ thuộc:** REC-01, REC-02 · **Quy mô:** M

### Checkpoint R1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `Recommendations` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

---

## Phase 2: Nguồn ứng viên và vị trí trang sản phẩm

### REC-04: Cặp danh mục bổ trợ
**Mô tả:** `GET /api/admin/recommendations/complements` (quyền `products:read`) và `PUT` cùng route (quyền `products:write`). Body của `PUT` gồm danh sách cặp có `weight` và danh sách danh mục mua lại được. Lệnh `recommendations:seed-complements` ghi cặp mặc định cho 5 danh mục seed.

**Tiêu chí nghiệm thu:**
- [ ] `sales` và `support` gọi `PUT` nhận 403. `support` gọi `GET` nhận 403. Endpoint mới có trong ma trận quyền
- [ ] Danh mục không tồn tại trong catalog trả 400 `VALIDATION_ERROR`
- [ ] Cặp trùng, cặp tự trỏ về chính nó, `weight` ngoài 0 đến 1 trả 400 `VALIDATION_ERROR`
- [ ] `PUT` thay toàn bộ danh sách trong một transaction và ghi đúng 1 dòng audit `recommendations.complements.update`
- [ ] Chạy lệnh seed hai lần không tạo dòng trùng
**Kiểm tra:** test tích hợp
**Phụ thuộc:** REC-03 · **Quy mô:** M

### REC-05: Job đêm `product_associations`
**Mô tả:** `ProductAssociationProcessor` đọc lượt xem và dòng hàng đã thanh toán qua cổng đọc chốt ở Câu hỏi mở 1 và 2. Processor tính `co_view` và `co_purchase` bằng `AssociationLift`. Worker chạy lúc 02:00 giờ `Asia/Ho_Chi_Minh`. Lệnh `recommendations:compute-associations` chạy một lần.

**Trạng thái:** chờ Câu hỏi mở 1 và 2 của plan.
**Tiêu chí nghiệm thu:**
- [ ] Hai lượt xem cùng `actorKey` cách nhau dưới 30 phút tạo cặp `co_view`. Cách nhau 30 phút trở lên thì không
- [ ] Hai sản phẩm trong cùng đơn đã thanh toán tạo cặp `co_purchase`
- [ ] Cùng người mua hai sản phẩm trong 30 ngày tạo cặp `co_purchase`
- [ ] Đơn chưa thanh toán không tạo cặp
- [ ] Chạy lại cho cùng dữ liệu ra cùng kết quả, không tạo dòng trùng
- [ ] Bộ dữ liệu dựng sẵn cho ra đúng điểm lift đã tính tay
- [ ] Job lỗi giữa chừng thì giữ bảng cũ, vì bảng mới chỉ thay trong một transaction
**Kiểm tra:** test tích hợp gọi processor trực tiếp, nguồn đọc dùng test double
**Phụ thuộc:** REC-03 · **Quy mô:** M

### REC-06: Bán chạy theo danh mục
**Mô tả:** `BestSellerSource` trả top sản phẩm theo danh mục trong 30 ngày từ `ICommerceReports.GetProductSalesAsync`. Danh mục chưa có đơn thì dùng sản phẩm mới nhất qua `ICatalogReader.ListProductsAsync` (quyết định 14). Kết quả cache 1 giờ.

**Tiêu chí nghiệm thu:**
- [ ] Thứ tự theo số lượng bán trong 30 ngày, giờ `Asia/Ho_Chi_Minh`
- [ ] Danh mục không có đơn trả sản phẩm mới nhất, không rỗng nếu danh mục có sản phẩm `active` còn hàng
- [ ] Sản phẩm hết hàng hoặc không `active` bị bỏ
- [ ] `commerce` báo lỗi thì dùng sản phẩm mới nhất và ghi log cảnh báo
**Kiểm tra:** test tích hợp, nguồn `commerce` dùng test double
**Phụ thuộc:** REC-03 · **Quy mô:** S

### REC-07: `similar`, `bought-together` và `GET /api/recommendations`
**Mô tả:** `RecommenderService` cài `IRecommender` cho hai vị trí `similar` và `bought-together`. Route công khai `GET /api/recommendations?placement=&productId=&limit=` gọi service này. Kết quả cache trong `product_recommendation_cache`. Module nghe `ProductChangedEvent` để xóa cache liên quan.

**Tiêu chí nghiệm thu:**
- [ ] `similar` dùng `IProductSearch.SimilarToAsync`, chỉ trả sản phẩm cùng danh mục, `MinPrice` trong ±30% giá của sản phẩm gốc
- [ ] `bought-together` gộp `co_purchase` và cặp danh mục bổ trợ. Chưa có `co_purchase` thì vẫn có gợi ý từ cặp bổ trợ
- [ ] Không trả chính sản phẩm gốc
- [ ] Ẩn một sản phẩm đã nằm trong cache: lần gọi sau không trả sản phẩm đó (quyết định 3)
- [ ] `placement` lạ, thiếu `productId`, `limit` ngoài 1 đến 24 trả 400 `VALIDATION_ERROR`. Không truyền `limit` thì trả tối đa 12
- [ ] `productId` không tồn tại trả 200 với danh sách rỗng
- [ ] `smart-search` báo lỗi thì trả "Bán chạy" của danh mục, không trả 5xx
- [ ] Response có `variant = none` và `source` dạng `recommendations.similar`
**Kiểm tra:** test tích hợp với catalog seed và test double cho `IProductSearch`
**Phụ thuộc:** REC-04, REC-05, REC-06 · **Quy mô:** M

### REC-UI1: Component `RecommendationRail`
**Mô tả:** Component trong `apps/frontend/src/features/recommendations/components/`. Rail có tiêu đề, dải thẻ sản phẩm cuộn ngang, dòng lý do dưới thẻ. Hook `useRecommendations` gọi API. Rail ghi `rec_impression` qua `useImpression` và `rec_click` qua `track` của `activity-tracking`.

**Tiêu chí nghiệm thu:**
- [ ] Hiện khung xương khi đang tải
- [ ] API lỗi hoặc danh sách rỗng thì ẩn cả khối, không hiện thông báo lỗi
- [ ] Sự kiện `rec_impression` và `rec_click` có `source`, `variant` và vị trí của thẻ
- [ ] Thẻ dùng lại `product-card.tsx` của catalog, không tạo thẻ sản phẩm thứ hai
- [ ] Cuộn ngang dùng được bằng bàn phím. Nút cuộn có `aria-label`
- [ ] Theo `docs/design/design-system.md`. Không tràn ngang ở 375 px
**Kiểm tra:** Vitest; ảnh chụp 1280 px và 375 px
**Phụ thuộc:** REC-07 · **Quy mô:** M

### REC-UI2: Khối gợi ý trên trang sản phẩm
**Mô tả:** Gắn hai `RecommendationRail` vào `product-page.tsx` của catalog: "Sản phẩm tương tự" và "Thường mua kèm". Nút thêm giỏ trên thẻ truyền `source` của vị trí cho `commerce`.

**Tiêu chí nghiệm thu:**
- [ ] Trang sản phẩm seed có hai khối với tiêu đề đúng
- [ ] Thêm giỏ từ khối gửi `source = recommendations.similar` hoặc `recommendations.bought-together`
- [ ] Khối lỗi không làm hỏng phần còn lại của trang sản phẩm
- [ ] Playwright: console 0 lỗi và 0 cảnh báo; ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** REC-UI1 · **Quy mô:** S

### Checkpoint R2
- [ ] Trang sản phẩm có khối `similar` và `bought-together` trên dữ liệu seed
- [ ] Job đêm tạo đúng `product_associations` từ dữ liệu dựng sẵn
- [ ] Review với người dùng

---

## Phase 3: Cá nhân hóa và thử nghiệm

### REC-08: "Dành cho bạn" bằng thuật toán
**Mô tả:** `RecommenderService` thêm vị trí `for-you` theo luồng của spec, bước 1 đến 5. Module đọc hồ sơ bằng `IProfileReader.GetProfileAsync`, gán nhóm bằng `VariantAssigner`, lưu kết quả trong `recommendation_cache` theo quyết định 10.

**Tiêu chí nghiệm thu:**
- [ ] Khách chưa đồng ý nhận "Bán chạy" và `variant = none`. Module không đọc hồ sơ của khách này
- [ ] Khách nhóm `control` nhận "Bán chạy" không cá nhân hóa, `variant = control`
- [ ] Khách đã đồng ý nhưng chưa có hồ sơ nhận "Bán chạy"
- [ ] Nhóm `algo` và `algo_llm` nhận thứ tự thuật toán kèm lý do theo mẫu ở lần gọi đầu
- [ ] Ứng viên lấy từ sản phẩm tương tự đã xem, `co_view` và bán chạy trong tầm giá của khách, khoảng 60 sản phẩm
- [ ] Gọi lại trong 6 giờ trả cache. Top danh mục trong hồ sơ đổi thì tính lại
- [ ] Hồ sơ dựng sẵn "laptop gaming 25 đến 35 triệu" trên bộ catalog dựng sẵn nhận ít nhất 8/12 sản phẩm phù hợp
- [ ] Nghe `UserDeletedEvent`: xóa cache và job của `user:<id>`
**Kiểm tra:** test tích hợp với test double cho `IProfileReader` và `IProductSearch`
**Phụ thuộc:** REC-07 · **Quy mô:** M

### REC-09: LLM sắp xếp lại ở nền
**Mô tả:** Lần gọi `for-you` của nhóm `algo_llm` ghi job vào `recommendation_rerank_jobs`. `RecommendationRerankWorker` chạy mỗi 5 giây. `RecommendationRerankProcessor` gọi `IAiGateway.ChatJsonAsync` theo quyết định 8, rồi ghi kết quả vào cache.

**Tiêu chí nghiệm thu:**
- [ ] Lần gọi đầu trả ngay thứ tự thuật toán. Test đo response không chờ processor
- [ ] Gọi 5 lần liên tiếp chỉ tạo 1 job
- [ ] Sau khi processor chạy, lần gọi sau trả thứ tự của LLM kèm lý do của LLM
- [ ] Prompt không chứa `actorKey`, tên, SĐT, email. Test đọc request mà fake provider ghi lại
- [ ] LLM trả id lạ, thiếu hoặc trùng: giữ thứ tự thuật toán, ghi log cảnh báo
- [ ] `AiUnavailableException`: giữ thứ tự thuật toán, job được thử lại ở vòng sau, tối đa 3 lần
- [ ] `AiBudgetExceededException`: ghi ngày vào `recommendation_budget_flags`. Trong ngày đó nhóm `algo_llm` nhận kết quả của `algo`
- [ ] Khi có API key, `AI_LIVE_TESTS=1`: contract test rerank với provider thật trả 12 id hợp lệ
- [ ] Không có API key: ghi rõ contract test thật còn mở và lý do
**Kiểm tra:** test tích hợp gọi processor trực tiếp với fake provider; `[LiveAiFact]` cho contract test
**Phụ thuộc:** REC-08 · **Quy mô:** M

### REC-10: Gợi ý trong giỏ hàng
**Mô tả:** Vị trí `cart` gộp "mua kèm" của các sản phẩm trong giỏ và bỏ sản phẩm đã có. Module đọc giỏ bằng hàm chốt ở Câu hỏi mở 3. Khách đã đồng ý được ưu tiên nhẹ theo danh mục quan tâm.

**Trạng thái:** chờ Câu hỏi mở 3 của plan.
**Tiêu chí nghiệm thu:**
- [ ] Route không nhận danh sách sản phẩm từ client. Tham số lạ bị bỏ qua
- [ ] Không trả sản phẩm đang có trong giỏ
- [ ] Giỏ rỗng trả danh sách rỗng
- [ ] Khách chưa đồng ý nhận gợi ý không dùng hồ sơ, `variant = none`
- [ ] Khách đã đồng ý: sản phẩm thuộc danh mục quan tâm đứng trước sản phẩm cùng điểm khác
**Kiểm tra:** test tích hợp với test double cho `ICommerceReader`
**Phụ thuộc:** REC-07 · **Quy mô:** S

### REC-UI3: Khối "Dành cho bạn" trên trang chủ
**Mô tả:** Gắn `RecommendationRail` với vị trí `for-you` vào trang chủ. Nếu Câu hỏi mở 6 giao việc dựng trang chủ cho module này, task tạo trang chủ tối thiểu thay `HealthPage` ở route `/`.

**Trạng thái:** chờ Câu hỏi mở 6 của plan.
**Tiêu chí nghiệm thu:**
- [ ] Khách đã đồng ý thấy tiêu đề "Dành cho bạn" và dòng lý do dưới thẻ
- [ ] Khách chưa đồng ý thấy tiêu đề "Bán chạy", không có dòng lý do cá nhân hóa
- [ ] Sự kiện hiển thị và click có `source = recommendations.for-you` và đúng `variant`
- [ ] Playwright: console 0 lỗi và 0 cảnh báo; ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** REC-UI1, REC-08 · **Quy mô:** S

### REC-UI2b: Khối gợi ý trong giỏ hàng
**Mô tả:** Gắn `RecommendationRail` với vị trí `cart` vào trang giỏ hàng của `commerce`. Thêm giỏ từ khối làm mới cả giỏ và khối gợi ý.

**Tiêu chí nghiệm thu:**
- [ ] Thêm giỏ từ khối gửi `source = recommendations.cart`
- [ ] Sau khi thêm, sản phẩm vừa thêm biến mất khỏi khối gợi ý
- [ ] Playwright: console 0 lỗi và 0 cảnh báo; ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** REC-UI1, REC-10 · **Quy mô:** S

### REC-11: Tỉ lệ chia nhóm
**Mô tả:** `GET /api/admin/recommendations/experiment` (quyền `reports:read`) và `PUT` cùng route (quyền `ai-settings:manage`). Tỉ lệ mới có hiệu lực ở request kế tiếp.

**Tiêu chí nghiệm thu:**
- [ ] `sales` gọi `PUT` nhận 403. `support` gọi `GET` nhận 403. Endpoint mới có trong ma trận quyền
- [ ] Ba tỉ lệ là số nguyên từ 0 đến 100, tổng bằng 100. Sai thì trả 400 `VALIDATION_ERROR`
- [ ] Mỗi lần sửa ghi đúng 1 dòng audit `recommendations.experiment.update` kèm tỉ lệ cũ và mới
- [ ] Đặt `control = 0` thì request kế tiếp không gán khách nào vào `control`
**Kiểm tra:** test tích hợp
**Phụ thuộc:** REC-08 · **Quy mô:** S

### REC-12: Số liệu theo vị trí và nhóm
**Mô tả:** `GET /api/admin/recommendations/metrics?from=&to=` (quyền `reports:read`, route theo Câu hỏi mở 9). Số liệu lấy từ `IActivityMetrics.GetFeatureMetricsAsync`. Contracts thêm `IRecommendationReports.GetExperimentAsync` nếu Câu hỏi mở 10 được duyệt.

**Tiêu chí nghiệm thu:**
- [ ] Trả CTR và tỉ lệ chuyển đổi cho mỗi vị trí, tách theo `control`, `algo`, `algo_llm`
- [ ] Số liệu khớp số tính tay trên bộ `IActivityMetrics` dựng sẵn
- [ ] Ngày bị đánh dấu hết ngân sách có cờ trong response và bị loại khỏi phép so sánh giữa nhóm
- [ ] `from` sau `to`, hoặc khoảng dài hơn 366 ngày, trả 400 `VALIDATION_ERROR`
- [ ] `support` nhận 403. Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp với test double cho `IActivityMetrics`
**Phụ thuộc:** REC-11 · **Quy mô:** M

### REC-UI4: Màn cặp bổ trợ và màn thử nghiệm
**Mô tả:** Hai màn back-office trong `apps/frontend/src/features/recommendations/admin/`: `/admin/recommendations/complements` và `/admin/recommendations/experiment`. Thêm mục menu theo cách khai báo hiện có trong `admin-navigation.ts`.

**Tiêu chí nghiệm thu:**
- [ ] Người chỉ có `products:read` xem được cặp bổ trợ nhưng không thấy nút lưu
- [ ] Người thiếu quyền không thấy menu. Mở thẳng URL thấy trang không đủ quyền
- [ ] Màn thử nghiệm hiện cảnh báo khi đổi tỉ lệ, theo quyết định 6 của plan. Người dùng phải xác nhận trước khi lưu
- [ ] Tổng tỉ lệ khác 100 thì báo lỗi dưới ô bằng `FieldError`, không gửi request
- [ ] Lưu thành công hiện thông báo. Lỗi từ API hiện dưới đúng ô
- [ ] Playwright: console 0 lỗi và 0 cảnh báo; ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** REC-04, REC-11 · **Quy mô:** M

### REC-UI5: Màn báo cáo gợi ý
**Mô tả:** Màn `/admin/recommendations/metrics` hiện bảng CTR và tỉ lệ chuyển đổi theo vị trí và nhóm. Bộ chọn khoảng thời gian nằm trên URL. Ngày bị đánh dấu hết ngân sách có ghi chú.

**Tiêu chí nghiệm thu:**
- [ ] Bảng có đủ 4 vị trí và 3 nhóm
- [ ] Đổi khoảng thời gian cập nhật URL. Mở lại URL ra cùng khoảng
- [ ] Không có dữ liệu thì hiện trạng thái trống, không hiện bảng toàn số 0
- [ ] Bảng có tiêu đề cột và đọc được bằng trình đọc màn hình
- [ ] Playwright: console 0 lỗi và 0 cảnh báo; ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; Playwright `--headed`
**Phụ thuộc:** REC-12 · **Quy mô:** S

### Checkpoint R3
- [ ] Trang chủ có "Dành cho bạn" kèm lý do. Lần gọi đầu không chờ LLM
- [ ] Hết ngân sách AI thì `algo_llm` rơi về `algo` và ngày đó bị đánh dấu
- [ ] Admin đổi tỉ lệ nhóm và xem báo cáo theo nhóm trên giao diện
- [ ] Review với người dùng

---

## Phase 4: E2E và nghiệm thu

### REC-E2E1: Luồng E2E của recommendations
**Mô tả:** File `apps/frontend/e2e/recommendations-for-you.spec.ts`. Kịch bản theo mục Testing của spec.
1. Khách chọn đồng ý cá nhân hóa.
2. Khách xem 3 laptop.
3. Test gọi lệnh tính lại hồ sơ của `activity-tracking`, không chờ job 5 phút.
4. Khách về trang chủ.
5. Khách click một thẻ trong "Dành cho bạn".

**Tiêu chí nghiệm thu:**
- [ ] Khối "Dành cho bạn" có ít nhất 1 laptop và dòng lý do
- [ ] Request `POST /api/events` có `rec_click` với `source = recommendations.for-you` và `variant` khớp response của `GET /api/recommendations`
- [ ] Khách chưa đồng ý thấy "Bán chạy" ở trang chủ
- [ ] Chạy lại với khóa AI sai: trang chủ, trang sản phẩm và giỏ hàng vẫn có khối gợi ý
- [ ] Console 0 lỗi và 0 cảnh báo trong cả luồng
**Kiểm tra:** `pnpm test:e2e`, chế độ có giao diện và chế độ ẩn
**Phụ thuộc:** REC-UI2, REC-UI2b, REC-UI3 · **Quy mô:** M

### REC-13: Nghiệm thu recommendations
**Mô tả:** Kiểm toàn module trước khi sang `shopping-assistant`. Lệnh `recommendations:benchmark` đo p95 của `GET /api/recommendations` cho 4 vị trí, theo mẫu `catalog:benchmark`.

**Tiêu chí nghiệm thu:**
- [ ] Ma trận quyền có mọi endpoint back-office của `recommendations`
- [ ] Coverage dòng của `Pickory.Modules.Recommendations` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] p95 của `GET /api/recommendations` dưới 300 ms cho mỗi vị trí, đo cả lần gọi đầu chưa có cache
- [ ] Lệnh benchmark từ chối chạy khi tên DB không chứa `bench`
- [ ] Test tích hợp với fake provider luôn lỗi: cả 4 vị trí vẫn trả gợi ý
- [ ] Khi có API key: contract test rerank thật đạt
- [ ] Không có API key: ghi rõ contract test thật còn mở và lý do
**Kiểm tra:** `dotnet test`, script gộp coverage, output lệnh benchmark
**Phụ thuộc:** REC-E2E1 · **Quy mô:** S

## Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ mục ghi "khi có API key"
- [ ] Coverage dòng của `Pickory.Modules.Recommendations` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `shopping-assistant`
