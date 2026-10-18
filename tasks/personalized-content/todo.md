# Task List: personalized-content

> **Trạng thái: đã bỏ ngày 2026-10-14.** Người dùng thu hẹp phần AI cá nhân hóa về một tính năng: chatbot tư vấn `shopping-assistant`.
>
> Chatbot đọc review thật của `commerce` khi khách hỏi, rồi tự tóm tắt trong câu trả lời. Hệ thống không còn nội dung viết sẵn theo từng người.
>
> File này chỉ giữ để tra cứu lịch sử. Không làm task nào trong file này. Không tick mục nào trong file này.

Plan và quyết định thiết kế: `tasks/personalized-content/plan.md`. Spec: `spec/SPEC-personalized-content.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Test tự động chỉ dùng fake provider của `ai-gateway`. Test không gọi AI thật.
- Kích thước: XS 1 file, S 1-2, M 3-5, L 6-8. Số file không tính file test.

---

## Phase 1: Logic thuần và nền tảng

### PCON-01: Nhóm nhu cầu và ánh xạ hồ sơ
**Mô tả:** `PersonaCatalog` khai báo nhóm nhu cầu theo danh mục, kèm luật ánh xạ từ `InterestProfile`. `PersonaResolver.Resolve(categoryId, profile)` trả mã nhóm nhu cầu. Bảng ánh xạ khóa thuộc tính sang khía cạnh đánh giá cũng nằm ở đây (quyết định 10 của plan). Luật viết theo thuộc tính có thật trong seed (Câu hỏi mở 3).
**Tiêu chí nghiệm thu:**
- [ ] Laptop có `gaming`, `office`, `creator`, `student`. Điện thoại có `camera`, `battery`, `gaming`, `budget`. Tai nghe có `commute`, `gaming`, `audiophile`, `sport`
- [ ] Hồ sơ `null` trả `general`
- [ ] Hồ sơ không khớp luật nào trả `general`
- [ ] Danh mục không có nhóm nhu cầu trả `general`
- [ ] Hồ sơ khớp hai nhóm thì trả nhóm có điểm cao hơn. Hai nhóm bằng điểm thì trả nhóm khai báo trước
- [ ] Mỗi luật chỉ dùng khóa thuộc tính có trong bộ thuộc tính seed. Test đối chiếu với `CatalogSeedData`
- [ ] Khóa `battery_mah` và `battery_hours` ánh xạ sang khía cạnh `pin`
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.PersonalizedContent/Personas/{PersonaCatalog.cs, PersonaResolver.cs}`, test trong `apps/backend/tests/Pickory.UnitTests/Modules/PersonalizedContent/`

### PCON-02: Bộ kiểm tra chống bịa
**Mô tả:** Ba hàm thuần trả danh sách vi phạm, không gọi DB và không gọi AI.
- `HighlightsGuard.Check(highlights, specs)`: kiểm điểm nổi bật.
- `ReviewSummaryGuard.Check(summary, reviews)`: kiểm tóm tắt đánh giá.
- `BannerCopyGuard.Check(copy)`: kiểm tiêu đề và câu phụ của banner.
**Tiêu chí nghiệm thu:**
- [ ] Khóa trong `basedOn` không có trong thông số của sản phẩm tạo vi phạm `unknown_spec_key`
- [ ] Con số trong câu không có trong giá trị của thông số được nhắc tạo vi phạm `unsupported_number`
- [ ] "16GB", "16 GB" và "16" khớp với giá trị `16` đơn vị `GB`. "165Hz" khớp với `165`
- [ ] Câu chứa từ trong danh sách chặn tạo vi phạm `absolute_claim`. Danh sách có "tốt nhất", "số 1", "rẻ nhất", "bảo hành trọn đời"
- [ ] So từ chặn không phân biệt hoa thường và dấu
- [ ] `mentions` lớn hơn số đánh giá tạo vi phạm `mentions_exceed_reviews`
- [ ] Khía cạnh không xuất hiện trong đánh giá nào tạo vi phạm `aspect_not_found`. So khớp bỏ dấu
- [ ] Mỗi điểm nổi bật dài hơn 140 ký tự, hoặc số ý nằm ngoài 3 đến 4, tạo vi phạm
- [ ] Tiêu đề banner quá 60 ký tự, câu phụ quá 120 ký tự, hoặc có số tiền, tạo vi phạm
- [ ] Nội dung hợp lệ trả danh sách rỗng
**Kiểm tra:** unit test, mỗi loại vi phạm có ít nhất một ca đạt và một ca không đạt
**Phụ thuộc:** không · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.PersonalizedContent/Guards/{HighlightsGuard.cs, ReviewSummaryGuard.cs, BannerCopyGuard.cs, AbsoluteClaimList.cs, NumberTokens.cs}`

### PCON-03: Hash đầu vào, điều kiện tạo lại và sắp ý theo sở thích
**Mô tả:** Ba hàm thuần.
- `ContentInputHash.ForHighlights(product, persona)`: băm đầu vào của điểm nổi bật, không có giá.
- `ReviewSummaryPolicy.NeedsRegeneration(previous, current)`: quyết định có tạo lại tóm tắt đánh giá hay không.
- `ReviewSummaryOrdering.Order(summary, profile)`: đưa ý khớp sở thích lên đầu và đánh dấu in đậm.
**Tiêu chí nghiệm thu:**
- [ ] Đổi giá SKU không đổi hash. Đổi thông số, tên hoặc mô tả ngắn thì đổi hash
- [ ] Hash không phụ thuộc thứ tự thông số trong mảng
- [ ] Dưới 3 đánh giá đang hiện thì không tạo tóm tắt
- [ ] Policy trả `true` khi có thêm 5 đánh giá, khi số đánh giá tăng 20%, hoặc khi có đánh giá bị ẩn
- [ ] Thêm 2 đánh giá trên tổng 40 thì không tạo lại
- [ ] Ý có khía cạnh khớp sở thích được đưa lên đầu và có cờ `emphasized`. Thứ tự các ý còn lại giữ nguyên
- [ ] Hồ sơ `null` giữ nguyên thứ tự, không ý nào có cờ `emphasized`
**Kiểm tra:** unit test
**Phụ thuộc:** PCON-01 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.PersonalizedContent/Generation/{ContentInputHash.cs, ReviewSummaryPolicy.cs}`, `Reading/ReviewSummaryOrdering.cs`

### PCON-04: Khung module, Contracts, schema và migration
**Mô tả:** Tạo `Pickory.Modules.PersonalizedContent` và `Pickory.Modules.PersonalizedContent.Contracts`. Contracts có `IContentReader`, `ReviewSummaryView`, `HighlightsView`, `BannerView`. Thêm `PersonalizedContentDbContext` và migration đầu tiên. `Program.cs` gọi `AddPersonalizedContentModule` và `MapPersonalizedContentEndpoints`.
**Tiêu chí nghiệm thu:**
- [ ] Bảng `generated_contents`, `content_jobs`, `static_banners` theo Data Model của spec, cộng cột `categoryId` cho banner
- [ ] Khóa `(kind, productId, categoryId, persona)` của `generated_contents` là unique với `NULLS NOT DISTINCT` (quyết định 9). Test ghi hai banner cùng khóa nhận lỗi ràng buộc
- [ ] `content_jobs` có khóa unique tương tự, để một nội dung chỉ có một job đang chờ
- [ ] `status` chỉ nhận `active`, `pending_approval`, `hidden`, `failed`, `expired`
- [ ] Migration chạy trên DB sạch. Lệnh `e2e:start` và fixture test tích hợp dựng được schema mới
- [ ] `Pickory.ArchitectureTests` chặn module tham chiếu module khác ngoài project Contracts
- [ ] `Program.cs` chỉ gọi hai hàm công khai của `PersonalizedContentModule.cs`
**Kiểm tra:** build, test kiến trúc, test tích hợp schema
**Phụ thuộc:** PCON-01, PCON-02, PCON-03 · **Quy mô:** M

### Checkpoint P1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `PersonalizedContent` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng: danh sách nhóm nhu cầu và luật ánh xạ (Câu hỏi mở 3 của plan)

---

## Phase 2: Tạo nội dung

### PCON-05: Hàng đợi job, processor và worker
**Mô tả:** `ContentJobQueue.EnqueueAsync` thêm job, bỏ qua khi job cùng khóa đang chờ. `ContentJobProcessor.ProcessDueJobsAsync` chạy job đến hạn và gọi bộ tạo của từng loại nội dung. `ContentJobWorker` chạy processor mỗi 30 giây, theo mẫu `ImageDeletionWorker` (quyết định 6 và 7 của plan).
**Tiêu chí nghiệm thu:**
- [ ] Gọi `EnqueueAsync` hai lần cùng khóa chỉ tạo 1 dòng `content_jobs`
- [ ] Job xong thì dòng job bị xóa
- [ ] `AiUnavailableException` tăng `attempts`, ghi `lastError` và hoãn job theo backoff. Sau 5 lần, job bị xóa và nội dung có `status = failed`
- [ ] `AiBudgetExceededException` dừng vòng chạy, giữ job, không tăng `attempts`
- [ ] Nội dung `active` cũ vẫn giữ nguyên khi job tạo lại bị lỗi
- [ ] Lỗi DB trong một vòng chỉ ghi log, worker vẫn chạy vòng sau
- [ ] Chu kỳ worker là 30 giây. Test đọc cấu hình chu kỳ, không chờ timer
**Kiểm tra:** test tích hợp gọi processor trực tiếp với fake provider
**Phụ thuộc:** PCON-04 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.PersonalizedContent/Jobs/{ContentJobQueue.cs, ContentJobProcessor.cs, ContentJobWorker.cs, ContentJobBackoff.cs}`

### PCON-06: Tạo tóm tắt đánh giá
**Mô tả:** `ReviewSummaryGenerator` đọc tối đa 100 đánh giá đang hiện mới nhất qua `ICommerceReader`, bỏ tên người viết, rồi gọi `IAiGateway.ChatJsonAsync` với feature `PersonalizedContentReviewSummary`. Kết quả qua `ReviewSummaryGuard`. Tín hiệu tạo lại theo Câu hỏi mở 1 của plan.
**Tiêu chí nghiệm thu:**
- [ ] Prompt không chứa tên người viết đánh giá. Fake provider ghi request để test kiểm
- [ ] Đầu ra sai schema thì gateway ném `AiInvalidOutputException`, job ghi `failed` với `failReason`
- [ ] Nội dung không đạt bộ kiểm tra thì tạo lại đúng 1 lần. Lần hai không đạt thì `status = failed`, tổng 2 lời gọi AI
- [ ] Nội dung đạt có `status = active`, `promptVersion`, `inputHash` và `generatedAt`
- [ ] Sản phẩm dưới 3 đánh giá đang hiện không tạo job
- [ ] Tín hiệu đánh giá đổi tạo job khi `ReviewSummaryPolicy` trả `true`, và không tạo job khi trả `false`
- [ ] Đánh giá bị ẩn làm tóm tắt cũ được tạo lại, và nội dung mới không dựa trên đánh giá đã ẩn
**Kiểm tra:** test tích hợp với `commerce` thật và fake provider có kịch bản
**Phụ thuộc:** PCON-05, Câu hỏi mở 1 của plan, `ICommerceReader` của `commerce` · **Quy mô:** M

### PCON-07: Tạo điểm nổi bật
**Mô tả:** `HighlightsGenerator` tạo 3 hoặc 4 ý cho mỗi cặp sản phẩm `active` và nhóm nhu cầu của danh mục, kể cả `general`. Đầu vào lấy từ `ICatalogReader.GetProductAsync`, không có giá. Handler của `ProductChangedEvent` so hash đầu vào rồi mới tạo job.
**Tiêu chí nghiệm thu:**
- [ ] Sản phẩm mới `active` tạo job cho mọi nhóm nhu cầu của danh mục và `general`
- [ ] `ProductChangedEvent` với hash không đổi không tạo job. Test đổi giá SKU và đếm 0 lời gọi AI
- [ ] `ProductChangedEvent` với hash đổi tạo job cho mọi nhóm nhu cầu của sản phẩm
- [ ] Sự kiện loại `Status` hoặc `Deleted` với sản phẩm không còn `active` xóa điểm nổi bật và tóm tắt đánh giá của sản phẩm
- [ ] Prompt không chứa giá. Fake provider ghi request để test kiểm
- [ ] Nội dung không đạt `HighlightsGuard` thì tạo lại đúng 1 lần, rồi `failed`
- [ ] Mỗi ý lưu kèm `basedOn`
**Kiểm tra:** test tích hợp với `catalog` thật và fake provider
**Phụ thuộc:** PCON-05 · **Quy mô:** M

### PCON-08: Tạo banner
**Mô tả:** `BannerGenerator` chạy mỗi tuần cho mỗi cặp danh mục gốc và nhóm nhu cầu. Hệ thống chọn tối đa 8 sản phẩm còn hàng bằng `ICatalogReader.ListProductsAsync`. LLM chỉ viết tiêu đề và câu phụ. Banner mới có `status = pending_approval`.
**Tiêu chí nghiệm thu:**
- [ ] Bộ sưu tập chỉ có sản phẩm còn hàng, đúng danh mục, tối đa 8 sản phẩm
- [ ] Bộ sưu tập rỗng thì không gọi AI và không tạo banner
- [ ] Prompt chỉ có đặc điểm chung của bộ sưu tập, không có giá
- [ ] Câu có số tiền bị `BannerCopyGuard` từ chối, rồi tạo lại đúng 1 lần
- [ ] Banner chờ duyệt quá 7 ngày chuyển `expired`
- [ ] Banner `active` cũ vẫn hiện trong lúc banner mới chờ duyệt
- [ ] Job tuần không tạo banner thứ hai cho cùng cặp danh mục và nhóm nhu cầu đang chờ duyệt
**Kiểm tra:** test tích hợp với `catalog` thật, fake provider và `TimeProvider` giả
**Phụ thuộc:** PCON-05 · **Quy mô:** M

### Checkpoint P2
- [ ] Job với fake provider tạo đủ ba loại nội dung `active` hoặc `pending_approval`
- [ ] Nội dung bịa thành `failed` sau đúng 2 lần tạo
- [ ] Review với người dùng

---

## Phase 3: Đọc, quản trị và giao diện

### PCON-09: API đọc công khai và `IContentReader`
**Mô tả:** Cài `IContentReader`. Thêm `GET /api/products/{id}/review-summary`, `GET /api/products/{id}/highlights` và `GET /api/home/banners`, đều công khai. Nhóm nhu cầu lấy từ `IProfileReader`, nên khách chưa đồng ý dùng `general`.
**Tiêu chí nghiệm thu:**
- [ ] Sản phẩm không có hoặc khách không thấy trả 404 `PRODUCT_NOT_FOUND`
- [ ] Chưa có nội dung thì trả 200 với `{ data: null }` và tạo đúng 1 job. Gọi 5 lần vẫn chỉ có 1 job
- [ ] API đọc không gọi `IAiGateway`. Fake provider ghi 0 request trong test
- [ ] Khách `gaming` nhận bản `gaming`. Chưa có bản đó thì nhận `general`. Chưa có `general` thì nhận `null`
- [ ] Tóm tắt đánh giá trả ý khớp sở thích lên đầu, có cờ `emphasized`
- [ ] Nội dung `hidden`, `failed`, `expired` hoặc `pending_approval` không bao giờ được trả
- [ ] Banner trả phần giá "từ X triệu" theo giá thấp nhất hiện tại của bộ sưu tập (quyết định 11)
- [ ] Không có banner `active` của nhóm phù hợp thì trả banner `general`, rồi tới banner tĩnh đang hiệu lực
- [ ] Response có trường nhãn `aiGenerated: true` cho nội dung AI
- [ ] Hết ngân sách AI thì API vẫn trả nội dung `active` cũ, HTTP 200
- [ ] `PendingApprovalCountAsync` trả đúng số banner `pending_approval`
**Kiểm tra:** test tích hợp với `catalog` thật và hồ sơ dựng sẵn
**Phụ thuộc:** PCON-06, PCON-07, PCON-08, `IProfileReader` của `activity-tracking` · **Quy mô:** M

### PCON-10: API quản trị nội dung AI
**Mô tả:** `GET /api/admin/generated-contents` và `GET /api/admin/generated-contents/{id}` với quyền `products:read`. Ẩn, duyệt và yêu cầu tạo lại với quyền `products:write`, theo route ở quyết định 13 của plan sau khi người dùng chốt.
**Tiêu chí nghiệm thu:**
- [ ] Danh sách lọc được theo loại, trạng thái và sản phẩm, phân trang theo `PagedResponse`
- [ ] Chi tiết trả nội dung, `inputHash`, `promptVersion`, `failReason` và dữ liệu đầu vào dựng lại từ dữ liệu hiện tại
- [ ] Ẩn chuyển nội dung sang `hidden`. API đọc công khai không trả nội dung đó nữa
- [ ] Duyệt chỉ nhận banner `pending_approval`, rồi lưu `approvedBy`. Loại khác trả 409 `INVALID_CONTENT_TRANSITION`
- [ ] Yêu cầu tạo lại trả 202 và tạo đúng 1 job
- [ ] Id lạ trả 404 `GENERATED_CONTENT_NOT_FOUND`
- [ ] Mỗi thao tác ghi đúng 1 dòng audit trong cùng transaction, với mã ở quyết định 15
- [ ] `sales` gọi được API xem, gọi API ẩn nhận 403. `support` gọi API xem nhận 403. Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp
**Phụ thuộc:** PCON-09, Câu hỏi mở 2 của plan · **Quy mô:** M

### PCON-11: Banner tĩnh
**Mô tả:** `GET` và `PUT /api/admin/home/static-banners` với quyền `products:write`. Mỗi banner tĩnh có `imagePublicId`, tiêu đề, link, vị trí, thời gian hiệu lực. Dựng URL ảnh theo Câu hỏi mở 4 của plan.
**Tiêu chí nghiệm thu:**
- [ ] `PUT` thay toàn bộ danh sách trong một transaction và ghi 1 dòng audit `personalized-content.static-banners.update`
- [ ] Tiêu đề trống, link không bắt đầu bằng `/`, hoặc `activeTo` trước `activeFrom` trả 400 `VALIDATION_ERROR`
- [ ] Banner ngoài khoảng hiệu lực không được trả ở `GET /api/home/banners`
- [ ] Thiếu khóa Cloudinary ở dev và test thì URL ảnh đi qua kho ảnh giả
- [ ] `sales` và `support` nhận 403. Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp
**Phụ thuộc:** PCON-09, Câu hỏi mở 4 của plan · **Quy mô:** S

### PCON-UI1: Khối nội dung AI trên trang sản phẩm
**Mô tả:** Khối "Khách hàng nói gì" đặt trên danh sách đánh giá. Khối "Điểm nổi bật cho nhu cầu của bạn" đặt cạnh bảng thông số. Code trong `apps/frontend/src/features/personalized-content/`.
**Tiêu chí nghiệm thu:**
- [ ] Mỗi khối có nhãn "Tóm tắt bởi AI" hoặc "Gợi ý bởi AI", kèm link giải thích (Câu hỏi mở 7 của plan)
- [ ] API trả `null` hoặc lỗi thì ẩn khối, không hiện lỗi cho khách
- [ ] Ý có cờ `emphasized` hiện in đậm và đứng đầu
- [ ] Trang sản phẩm không chờ hai khối này: thông tin sản phẩm hiện trước khi API nội dung trả về
- [ ] Theo `docs/design/design-system.md`. Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Dùng được bằng bàn phím. Link giải thích có tên truy cập rõ ràng
- [ ] Playwright ghi 0 lỗi và 0 cảnh báo trên console
**Kiểm tra:** Vitest; ảnh chụp 1280 px và 375 px; Playwright đọc console
**Phụ thuộc:** PCON-09 · **Quy mô:** M

### PCON-UI2: Banner trang chủ
**Mô tả:** Carousel banner trên trang chủ của khách. Banner tĩnh làm dự phòng. Task chờ quyết định về trang chủ (Câu hỏi mở 5 của plan).
**Tiêu chí nghiệm thu:**
- [ ] Carousel có nút trước, nút sau và chỉ báo vị trí, đều dùng được bằng bàn phím
- [ ] Carousel không tự chuyển khi người dùng bật `prefers-reduced-motion`
- [ ] Banner AI có nhãn "Gợi ý bởi AI". Banner tĩnh không có nhãn này
- [ ] API lỗi hoặc trả rỗng thì ẩn carousel
- [ ] Ảnh có `alt`. Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright ghi 0 lỗi và 0 cảnh báo trên console
**Kiểm tra:** Vitest; ảnh chụp 1280 px và 375 px; Playwright đọc console
**Phụ thuộc:** PCON-09, PCON-11, Câu hỏi mở 5 của plan · **Quy mô:** M

### PCON-UI3: Màn nội dung AI trong back-office
**Mô tả:** Màn `/admin/generated-contents` có danh sách, bộ lọc, chi tiết kèm dữ liệu đầu vào, nút ẩn, tạo lại và duyệt banner. Thêm mục menu vào `ADMIN_NAV_GROUPS`, cho tới khi `admin-console` thay cách đăng ký menu.
**Tiêu chí nghiệm thu:**
- [ ] Menu chỉ hiện với người có `products:read`. Nút ẩn, tạo lại, duyệt chỉ hiện với người có `products:write`
- [ ] Mở thẳng URL khi thiếu quyền thấy trang không đủ quyền
- [ ] Ẩn và duyệt hỏi xác nhận trước khi gửi. Thành công hiện thông báo, lỗi hiện thông điệp tiếng Việt của API
- [ ] Nội dung `failed` hiện `failReason`
- [ ] Bảng và bộ lọc theo design system. Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Dùng được bằng bàn phím, mọi ô lọc có `label`
- [ ] Playwright ghi 0 lỗi và 0 cảnh báo trên console
**Kiểm tra:** Vitest; E2E `--headed`; ảnh chụp
**Phụ thuộc:** PCON-10 · **Quy mô:** M

### PCON-UI4: Màn banner tĩnh trong back-office
**Mô tả:** Màn `/admin/home/static-banners` cho admin thêm, sửa, sắp xếp và xóa banner tĩnh. Ảnh tải lên qua chữ ký Cloudinary của catalog, giống `UI-C6` của `tasks/catalog/todo.md`.
**Tiêu chí nghiệm thu:**
- [ ] Lỗi validate hiện dưới đúng ô bằng `FieldError`
- [ ] Đổi thứ tự dùng được bằng bàn phím
- [ ] Lưu thành công hiện thông báo. Danh sách tải lại khớp dữ liệu đã lưu
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright ghi 0 lỗi và 0 cảnh báo trên console
**Kiểm tra:** Vitest; E2E với Cloudinary giả lập bằng `page.route`
**Phụ thuộc:** PCON-11 · **Quy mô:** M

### Checkpoint P3
- [ ] Trang sản phẩm hiện hai khối nội dung AI có nhãn, và ẩn khối khi chưa có nội dung
- [ ] Admin duyệt banner trên giao diện, banner hiện ở trang chủ cho đúng nhóm nhu cầu
- [ ] Review với người dùng

---

## Phase 4: E2E và nghiệm thu

### PCON-12: Lệnh kiểm lại và lệnh đánh giá với AI thật
**Mô tả:** Hai lệnh dòng lệnh.
- `dotnet run --project apps/backend/src/Pickory.Api -- personalized-content:verify`: chạy lại bộ kiểm tra chống bịa trên mọi nội dung `active`.
- `dotnet run --project apps/backend/src/Pickory.Api -- personalized-content:eval`: tạo điểm nổi bật cho mọi sản phẩm seed và mọi nhóm nhu cầu bằng AI thật.
**Tiêu chí nghiệm thu:**
- [ ] `verify` in số nội dung đã kiểm và số vi phạm. Có vi phạm thì lệnh trả mã thoát khác 0
- [ ] `eval` từ chối chạy khi `AI_LIVE_TESTS` khác `1`
- [ ] `eval` in tỉ lệ nội dung đạt kiểm tra ngay lần đầu
- [ ] `eval` ghi 20 mẫu ngẫu nhiên ra file Markdown ngoài repo, có cột chấm "đúng, hữu ích, tự nhiên" cho người duyệt
- [ ] `eval` từ chối chạy khi `ASPNETCORE_ENVIRONMENT` là `Production`
**Kiểm tra:** test tích hợp cho `verify`; chạy `eval` khi có API key
**Phụ thuộc:** PCON-07 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.PersonalizedContent/Evaluation/{PersonalizedContentCommandLine.cs, ContentVerifier.cs, HighlightsEvaluation.cs}`

### PCON-E2E1: Luồng E2E của personalized-content
**Mô tả:** Playwright trong `apps/frontend/e2e/personalized-content-*.spec.ts`. Startup E2E dùng fake provider và dựng sẵn nội dung qua job.
**Tiêu chí nghiệm thu:**
- [ ] Khách nhóm `gaming` và khách nhóm `general` xem cùng 1 laptop thấy 2 bộ điểm nổi bật khác nhau
- [ ] Banner chưa duyệt không hiện ở trang chủ. Admin duyệt trên màn back-office, rồi khách đúng nhóm thấy banner
- [ ] Startup với AI tắt (sai key): trang sản phẩm vẫn hiện nội dung đã có, không có lỗi trên console
- [ ] Suite chạy được ở chế độ có giao diện và chế độ ẩn
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** PCON-UI1, PCON-UI2, PCON-UI3, cách kích hoạt job hồ sơ của `activity-tracking` cho E2E · **Quy mô:** M

### PCON-13: Nghiệm thu personalized-content
**Mô tả:** Kiểm toàn module trước khi sang `shopping-assistant`.
**Tiêu chí nghiệm thu:**
- [ ] Ma trận quyền có mọi endpoint back-office của module
- [ ] Coverage dòng của `Pickory.Modules.PersonalizedContent` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Lệnh đo p95 cho `GET /api/products/{id}/review-summary` và `GET /api/products/{id}/highlights` in kết quả dưới 150 ms trên máy dev
- [ ] `personalized-content:verify` trên dữ liệu dev in 0 vi phạm
- [ ] Mọi sản phẩm seed có ít nhất 3 đánh giá đều có tóm tắt `active`. Tiêu chí này cần đánh giá seed của `commerce` (Câu hỏi mở 6 của plan)
- [ ] Khi có API key: `personalized-content:eval` đạt ít nhất 90% ngay lần đầu
- [ ] Khi có API key: người duyệt chấm 20 mẫu, ít nhất 18 mẫu đạt
- [ ] Không có API key: ghi rõ hai tiêu chí AI thật còn mở và lý do
**Kiểm tra:** `dotnet test`, script gộp coverage, output đo p95, output hai lệnh của `PCON-12`
**Phụ thuộc:** PCON-12, PCON-E2E1 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Api/Benchmark/PersonalizedContentBenchmark.cs`

## Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ mục ghi "khi có API key"
- [ ] Coverage dòng của `Pickory.Modules.PersonalizedContent` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `shopping-assistant`
