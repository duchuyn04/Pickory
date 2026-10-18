# Task List: chatbot-widget

Plan và quyết định thiết kế: `tasks/chatbot-widget/plan.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Kích thước: XS 1 file, S 1-2, M 3-5. Số file không tính file test.

---

## Phase 1: Cấu hình và widget

### CBW-01: Cấu hình dịch vụ chatbot và route công khai
**Mô tả:** `AppConfig` đọc `CHATBOT_WIDGET_SCRIPT_URL` và `CHATBOT_API_URL`. Route công khai `GET /api/chatbot-widget` trả `{ enabled, scriptUrl, apiUrl }`. Thiếu cả hai biến thì trả `enabled: false` và hai địa chỉ `null`.
**Tiêu chí nghiệm thu:**
- [ ] Thiếu cả hai biến: route trả 200 với `enabled: false`
- [ ] Đủ hai biến: route trả đúng hai địa chỉ đã cấu hình
- [ ] Chỉ đặt một trong hai biến: API dừng khi khởi động, thông báo nêu tên biến còn thiếu
- [ ] Địa chỉ không phải URL tuyệt đối: API dừng khi khởi động
- [ ] Ngoài `Development`, địa chỉ `http` làm API dừng khi khởi động
- [ ] `.env.example` có hai biến, để trống, kèm comment tiếng Việt
**Kiểm tra:** unit test cho `AppConfig`; test tích hợp cho route
**Phụ thuộc:** không · **Quy mô:** S

### CBW-UI1: Tải widget trên trang khách
**Mô tả:** `CustomerLayout` đọc `GET /api/chatbot-widget`. Route trả `enabled: true` thì layout chèn thẻ `<script async src="<scriptUrl>" data-api="<apiUrl>">` một lần.
**Tiêu chí nghiệm thu:**
- [ ] `enabled: false` hoặc route lỗi: trang không chèn script nào
- [ ] Chuyển qua nhiều trang khách: trang chỉ có đúng một thẻ script của chatbot
- [ ] Trang `/admin` không có thẻ script của chatbot
- [ ] Script tải lỗi: trang khách vẫn hiện đủ nội dung, không có lỗi JavaScript chưa bắt
- [ ] Gọi route sau khi trang hiện xong, để widget không làm chậm lần hiện đầu tiên
**Kiểm tra:** Vitest
**Phụ thuộc:** CBW-01 · **Quy mô:** S

### CBW-02: Tài liệu triển khai
**Mô tả:** Viết hướng dẫn gắn `chatbot-rag` vào Pickory, đặt ở `docs/chatbot-widget.md`. Hướng dẫn theo từng bước, có kết quả mong đợi và lỗi thường gặp.
**Tiêu chí nghiệm thu:**
- [ ] Có bước đặt `CHATBOT_WIDGET_SCRIPT_URL` và `CHATBOT_API_URL`, kèm ví dụ cho máy dev và VPS
- [ ] Có bước thêm origin của Pickory vào CORS của `chatbot-rag`
- [ ] Có bước đặt `form_url` của `chatbot-rag` trỏ tới `/support`
- [ ] Có bước cho `chatbot-rag` nạp tài liệu từ `GET /api/help-documents` của `knowledge-base`
- [ ] Có ghi chú về `Content-Security-Policy` theo quyết định 6 của plan
- [ ] Ghi phiên bản hoặc commit của `chatbot-rag` đã kiểm
**Kiểm tra:** người dùng tự làm theo ở Checkpoint W1
**Phụ thuộc:** CBW-01; KB-14 cho bước nạp tài liệu · **Quy mô:** XS

---

## Phase 2: Nghiệm thu

### CBW-E2E1: Trang mua hàng chạy khi dịch vụ chatbot lỗi
**Mô tả:** Task này thay COM-E2E5 của `tasks/commerce/todo.md`. Spec mới `apps/frontend/e2e/chatbot-widget.spec.ts` có hai nhóm test.
- Widget giả: Playwright trả một script giả cho URL chatbot. Script giả gắn một phần tử có tên dễ tìm.
- Dịch vụ lỗi: URL script trỏ tới cổng không có dịch vụ. Khách vãng lai đặt một đơn COD qua giao diện.
**Tiêu chí nghiệm thu:**
- [ ] Widget giả hiện ở trang chủ và trang sản phẩm, không hiện ở `/admin`
- [ ] Dịch vụ chatbot lỗi: khách đặt đơn COD thành công và thấy trang thành công
- [ ] Console 0 lỗi và 0 cảnh báo, trừ lỗi tải của đúng URL script chatbot
- [ ] Spec xanh ở chế độ ẩn và `--headed`
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** CBW-UI1 · **Quy mô:** M

### CBW-03: Nghiệm thu chatbot-widget
**Mô tả:** Kiểm toàn module trước Checkpoint cuối.
**Tiêu chí nghiệm thu:**
- [ ] `dotnet test` toàn bộ và `pnpm test:e2e` toàn bộ xanh
- [ ] Mọi dòng Traceability của plan có bằng chứng
- [ ] Ảnh chụp trang chủ 1280 px và 375 px khi có widget thật: khung chat không che banner đồng ý cookie hay nút giỏ hàng
**Kiểm tra:** chạy lệnh, xem ảnh chụp
**Phụ thuộc:** CBW-E2E1, CBW-02 · **Quy mô:** XS
