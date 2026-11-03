# Task List: support-desk, nhánh frontend

Quy ước chung, Phase 1, checkpoint và E2E nằm ở `tasks/support-desk/todo.md`. Dạng dữ liệu của route nằm ở `tasks/support-desk/api-contract.md`. Tiến độ chung nằm ở mục "Task List" của `tasks/support-desk/plan.md`.

Quy ước riêng của nhánh frontend:
- Task UI theo `docs/design/design-system.md`. Kiểm Playwright console có 0 lỗi và 0 cảnh báo.
- Task thêm Vitest kiểm schema `zod` đọc được mọi ví dụ của route mà task dùng.
- Route có ô "Có từ" ghi "chưa có" trong `api-contract.md` thì E2E trả dữ liệu mẫu bằng helper `page.route` của SD-C2.
- Không gọi `page.route` trực tiếp cho route `/api/support/` hay `/api/admin/support/`. Chỉ helper của SD-C2 tự báo lỗi khi backend đã có route.
- Gộp `main` vào nhánh làm việc ít nhất mỗi ngày. Route backend mới chỉ tới nhánh frontend qua bước gộp này.
- Tiêu chí UI cần backend thật ghi "Kiểm đầu cuối ở SD-E2E1" hoặc "Kiểm đầu cuối ở SD-E2E2". Task UI đạt khi tiêu chí đó chạy được với dữ liệu mẫu.
- Nhánh frontend không chờ task nào của nhánh backend.

---

## Phase 2: Hội thoại phía khách, tiếp nhận và trả lời

### SD-C2: Bộ đọc dữ liệu mẫu và route giả phía frontend
**Mô tả:** Helper đọc `spec/fixtures/support-desk/` cho Vitest và cho Playwright. Helper E2E trả ví dụ trong dữ liệu mẫu bằng `page.route`, và trả sự kiện SSE mẫu. `support-desk-workspace.spec.ts` chuyển dữ liệu viết tay sang dữ liệu mẫu.
**Tiêu chí nghiệm thu:**
- [ ] Vitest đọc được ví dụ theo tên file và tên ví dụ, theo cách của `test/unit/shared/catalog/shared-fixtures.test.ts`
- [ ] Helper E2E trả `status` và `response` của ví dụ cho route khớp `method` và đường dẫn
- [ ] Trước khi gắn route giả, helper đọc `/api/openapi/v1.json` của backend E2E. Helper đọc tài liệu một lần cho mỗi lần chạy
- [ ] Tài liệu đã có `method` và đường dẫn của route thì helper làm test lỗi với câu "Backend đã có `POST /api/support/callback-requests`. Gỡ dữ liệu giả của route này."
- [ ] Helper so đường dẫn cụ thể với đường dẫn mẫu của OpenAPI, ví dụ `/api/support/conversations/abc/csat` khớp `/api/support/conversations/{id}/csat`
- [ ] Helper sự kiện SSE không kiểm tài liệu OpenAPI, vì route `events` đã có từ SD-03 và SD-16
- [ ] Phép thử đột biến: gọi helper route giả cho `POST /api/support/conversations` thì test lỗi, vì route này đã có
- [ ] Helper E2E trả một response `text/event-stream` chứa các sự kiện chọn từ file mẫu sự kiện
- [ ] Schema hàng chờ hiện có đọc được ví dụ "hàng chờ" và "phiên của tôi sau khi đồng nghiệp chuyển" của `admin-sessions.json`
- [ ] `support-desk-workspace.spec.ts` không còn dữ liệu phiên viết tay, và vẫn xanh
- [ ] Phép thử đột biến: đổi kiểu một trường trong schema `zod` thì Vitest hợp đồng lỗi
**Kiểm tra:** `pnpm lint && pnpm typecheck && pnpm test`; `pnpm test:e2e e2e/support-desk-workspace.spec.ts`
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/frontend/test/unit/features/support-desk/contract-fixtures.ts`, `apps/frontend/e2e/support-desk-contract-routes.ts`, `apps/frontend/e2e/support-desk-workspace.spec.ts`

### SD-UI7: Trang `/support` của khách
**Mô tả:** Trang chat của khách trong `apps/frontend/src/features/support-desk/customer/`. Trang gọi route của SD-15, nhận sự kiện của SD-16 bằng `EventSource`, và render tin của khách, nhân viên, hệ thống. Tin hệ thống có `action` thì trang hiện chỗ đặt form. SD-UI6 thay chỗ đặt đó bằng form thật. Fallback của `chatbot-rag` trỏ `form_url` tới trang này. Từ ngày 2026-11-03, trang theo mục "Route phía khách" của `api-contract.md`.
**Tiêu chí nghiệm thu:**
- [ ] Schema `zod` đọc được mọi ví dụ hội thoại và tin của `customer-api.json`, và mọi sự kiện của `customer-events.json`
- [ ] Khách vãng lai và khách đã đăng nhập đều mở được `/support`
- [ ] Hội thoại mới hiện câu báo hội thoại được lưu 90 ngày khi `showRetentionNotice` bằng `true`
- [ ] Gửi tin đầu tiên đổi trạng thái hiển thị sang "Đang chờ nhân viên"
  Kiểm đầu cuối ở SD-E2E1.
- [ ] Tải lại trang khi `session.status` bằng `waiting` vẫn hiện "Đang chờ nhân viên". Thiếu khóa `session` thì trang coi như `null`
- [ ] Tin của nhân viên hiện tên ngắn của nhân viên, khác kiểu với tin của khách
- [ ] Tin `staff` có `staffDisplayName` bằng `null` hiện "Nhân viên CSKH"
- [ ] Lỗi 429 `SUPPORT_MESSAGE_RATE_LIMITED` hiện thông báo tiếng Việt và giữ nội dung ô soạn
- [ ] Mất kết nối `/events` thì trang nối lại và không hiện tin trùng
- [ ] Chân trang có link "Liên hệ CSKH" tới `/support` (chờ Câu hỏi mở 12). App chưa có chân trang thì task này dựng chân trang, vì `knowledge-base` làm sau
- [ ] Ô soạn có `label`. Dùng được bằng bàn phím
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest cho hook SSE và ô soạn; ảnh chụp; Playwright `--headed`
**Phụ thuộc:** SD-C2. Route của SD-15 đã chạy thật. Trường `session` dùng dữ liệu mẫu tới khi SD-04 tick · **Quy mô:** M

### SD-UI6: Form số điện thoại và form CSAT phía khách
**Mô tả:** `CallbackRequestForm` và `CsatForm` trong `apps/frontend/src/features/support-desk/customer/`. Trang `/support` của SD-UI7 render hai form theo `action` của tin hệ thống (quyết định 21). Ngày 2026-10-18 task đổi nơi gắn form từ widget của `shopping-assistant` sang `/support`.
**Tiêu chí nghiệm thu:**
- [ ] Schema `zod` đọc được các ví dụ gọi lại và chấm điểm của `customer-api.json`
- [ ] Form số điện thoại kiểm bằng Zod cùng luật với backend. Lỗi hiện dưới ô bằng `FieldError`
- [ ] Lỗi 429 `CALLBACK_LIMIT_REACHED` hiện thông báo tiếng Việt, form không gửi lại
- [ ] Gửi thành công thì form đổi thành lời xác nhận
- [ ] Form CSAT có 5 nút chọn điểm dùng được bằng bàn phím, mỗi nút có tên truy cập
- [ ] Ảnh chụp trong khung chat ở 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** SD-UI7, SD-C2. Trước ngày 2026-11-03 task chờ SD-05 và SD-09 · **Quy mô:** M

### SD-UI2: Khung chat và mẫu trả lời nhanh
**Mô tả:** Cột giữa của màn CSKH: lịch sử tin có nhãn khách, nhân viên, hệ thống. Ô soạn có nút gửi và gõ `/` để chọn mẫu. Ngày 2026-10-18 task bỏ nhãn bot, nguồn bot và nút "Gợi ý trả lời". Ngày 2026-11-03 task thêm nút "Nhận" ở phiên đang chờ.
**Tiêu chí nghiệm thu:**
- [ ] Schema `zod` đọc được mọi ví dụ nhận phiên, gửi tin và lịch sử tin của `admin-sessions.json`, và ví dụ danh sách của `admin-canned-responses.json`
- [ ] Nút "Nhận" gọi `POST /api/admin/support/sessions/{id}/assignment`. Lỗi 409 `SUPPORT_SESSION_ALREADY_CLAIMED` hiện "Đã có người nhận"
- [ ] Sự kiện `message.created` của hội thoại đang mở làm lịch sử đọc lại
  Kiểm đầu cuối ở SD-E2E1.
- [ ] Tin `staff` có `staffDisplayName` bằng `null` hiện "Nhân viên CSKH"
- [ ] Gõ `/` mở danh sách mẫu. Mũi tên lên, xuống chọn mẫu. Enter chèn mẫu đã thay biến
- [ ] Ô soạn tắt và có chú thích khi nhân viên không giữ phiên
- [ ] Lỗi 403 `SUPPORT_SESSION_NOT_ASSIGNED` hiện bằng `ErrorAlert`
- [ ] Lịch sử hiện mọi phiên cũ của cùng hội thoại, có dòng phân cách giữa các phiên
- [ ] Có `label` cho ô soạn. Dùng được bằng bàn phím
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest cho bộ chọn mẫu và ô soạn; ảnh chụp
**Phụ thuộc:** SD-UI1, SD-C2. Trước ngày 2026-11-03 task chờ SD-07 · **Quy mô:** M

### SD-UI3: Thông tin khách và hộp thoại chuyển phiên
**Mô tả:** Cột phải của màn CSKH hiện dữ liệu của SD-11. Thanh trên của khung chat có nút "Chuyển" và "Kết thúc".
**Tiêu chí nghiệm thu:**
- [ ] Schema `zod` đọc được mọi ví dụ của `admin-customer-panel.json`, và các ví dụ chuyển, kết thúc phiên của `admin-sessions.json`
- [ ] Mỗi đơn có link `/admin/orders/{code}`. `name` và `email` bằng `null` thì cột hiện "Chưa có thông tin"
- [ ] Hộp thoại chuyển chỉ liệt kê nhân viên đang sẵn sàng, trừ chính người dùng
- [ ] Hộp thoại có ô ghi chú nội bộ với `label`. Esc đóng hộp thoại và trả focus về nút "Chuyển"
- [ ] "Kết thúc" hỏi xác nhận trước khi gọi API
- [ ] Lỗi 409 hiện bằng `ErrorAlert` với thông báo của API
- [ ] Ở 375 px, cột thông tin khách thu thành ngăn mở bằng nút
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** SD-UI2, SD-C2. Trước ngày 2026-11-03 task chờ SD-08 và SD-11 · **Quy mô:** M

### SD-UI4: Tab Gọi lại
**Mô tả:** Tab Gọi lại liệt kê yêu cầu `pending` trước, kèm số điện thoại, ghi chú và thời điểm gửi. Nhân viên đánh dấu "Đã gọi" hoặc "Không liên lạc được". Ngày 2026-11-03 task nhận thêm hai sự kiện `callback.created` và `session.reminded` ở cột hàng chờ.
**Tiêu chí nghiệm thu:**
- [ ] Schema `zod` đọc được mọi ví dụ của `admin-callbacks.json`. `phone` bằng `null` hiện "Đã xóa số"
- [ ] Yêu cầu mới hiện trong tab mà không tải lại trang
  Kiểm đầu cuối ở SD-E2E2.
- [ ] Sự kiện `session.reminded` phát thông báo nhắc lại, theo lựa chọn âm báo của SD-UI1
- [ ] Người thiếu `conversations:reply` thấy danh sách nhưng không thấy nút đánh dấu
- [ ] Số điện thoại hiện dạng link `tel:`
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** SD-UI1, SD-C2. Trước ngày 2026-11-03 task chờ SD-05 · **Quy mô:** S

---

## Phase 3: Cấu hình và số liệu

### SD-UI5: Màn cấu hình CSKH
**Mô tả:** Màn `/admin/support/settings` cho giờ làm việc, ngày nghỉ và mẫu dùng chung. Màn quản lý mẫu cá nhân nằm trong màn CSKH. Menu cấu hình chỉ hiện với người có `support-settings:manage`.
**Tiêu chí nghiệm thu:**
- [ ] Schema `zod` đọc được mọi ví dụ của `admin-settings.json` và `admin-canned-responses.json`
- [ ] Mẫu có `editable` bằng `false` không có nút sửa và nút xóa
- [ ] Lưu thành công hiện thông báo. Lỗi hiện dưới đúng ô bằng `FieldError`
- [ ] Ngày nghỉ chọn bằng ô ngày có `label`, xóa được từng ngày
- [ ] Người thiếu quyền không thấy menu. Mở thẳng URL thấy trang không đủ quyền
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; E2E `--headed`
**Phụ thuộc:** SD-UI1, SD-C2. Trước ngày 2026-11-03 task chờ SD-10 · **Quy mô:** M
