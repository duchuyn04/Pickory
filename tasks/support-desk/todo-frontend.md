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
- [x] Vitest đọc được ví dụ theo tên file và tên ví dụ, theo cách của `test/unit/shared/catalog/shared-fixtures.test.ts`
- [x] Helper E2E trả `status` và `response` của ví dụ cho route khớp `method` và đường dẫn
- [x] Trước khi gắn route giả, helper đọc `/api/openapi/v1.json` của backend E2E. Helper đọc tài liệu một lần cho mỗi lần chạy
- [x] Tài liệu đã có `method` và đường dẫn của route thì helper làm test lỗi với câu "Backend đã có `POST /api/support/callback-requests`. Gỡ dữ liệu giả của route này."
- [x] Helper so đường dẫn cụ thể với đường dẫn mẫu của OpenAPI, ví dụ `/api/support/conversations/abc/csat` khớp `/api/support/conversations/{id}/csat`
- [x] Helper sự kiện SSE không kiểm tài liệu OpenAPI, vì route `events` đã có từ SD-03 và SD-16
- [x] Phép thử đột biến: gọi helper route giả cho `POST /api/support/conversations` thì test lỗi, vì route này đã có
- [x] Helper E2E trả một response `text/event-stream` chứa các sự kiện chọn từ file mẫu sự kiện
- [x] Schema hàng chờ hiện có đọc được ví dụ "hàng chờ" và "phiên của tôi sau khi đồng nghiệp chuyển" của `admin-sessions.json`
- [x] `support-desk-workspace.spec.ts` không còn dữ liệu phiên viết tay, và vẫn xanh
- [x] Phép thử đột biến: đổi kiểu một trường trong schema `zod` thì Vitest hợp đồng lỗi
**Kiểm tra:** `pnpm lint && pnpm typecheck && pnpm test`; `pnpm test:e2e e2e/support-desk-workspace.spec.ts`
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/frontend/test/unit/features/support-desk/contract-fixtures.ts`, `apps/frontend/e2e/support-desk-contract-routes.ts`, `apps/frontend/e2e/support-desk-workspace.spec.ts`

### SD-UI7: Trang `/support` của khách
**Mô tả:** Trang chat của khách trong `apps/frontend/src/features/support-desk/customer/`. Trang gọi route của SD-15, nhận sự kiện của SD-16 bằng `EventSource`, và render tin của khách, nhân viên, hệ thống. Tin hệ thống có `action` thì trang hiện chỗ đặt form. SD-UI6 thay chỗ đặt đó bằng form thật. Fallback của `chatbot-rag` trỏ `form_url` tới trang này. Từ ngày 2026-11-03, trang theo mục "Route phía khách" của `api-contract.md`.
**Tiêu chí nghiệm thu:**
- [x] Schema `zod` đọc được mọi ví dụ hội thoại và tin của `customer-api.json`, và mọi sự kiện của `customer-events.json`
- [x] Khách vãng lai và khách đã đăng nhập đều mở được `/support`
- [x] Hội thoại mới hiện câu báo hội thoại được lưu 90 ngày khi `showRetentionNotice` bằng `true`
- [x] Gửi tin đầu tiên đổi trạng thái hiển thị sang "Đang chờ nhân viên"
  Kiểm đầu cuối ở SD-E2E1.
- [x] Tải lại trang khi `session.status` bằng `waiting` vẫn hiện "Đang chờ nhân viên". Thiếu khóa `session` thì trang coi như `null`
- [x] Tin của nhân viên hiện tên ngắn của nhân viên, khác kiểu với tin của khách
- [x] Tin `staff` có `staffDisplayName` bằng `null` hiện "Nhân viên CSKH"
- [x] Lỗi 429 `SUPPORT_MESSAGE_RATE_LIMITED` hiện thông báo tiếng Việt và giữ nội dung ô soạn
- [x] Mất kết nối `/events` thì trang nối lại và không hiện tin trùng
- [x] Chân trang có link "Liên hệ CSKH" tới `/support` (chờ Câu hỏi mở 12). App chưa có chân trang thì task này dựng chân trang, vì `knowledge-base` làm sau
- [x] Ô soạn có `label`. Dùng được bằng bàn phím
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang
- [x] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest cho hook SSE và ô soạn; ảnh chụp; Playwright `--headed`
**Phụ thuộc:** SD-C2. Route của SD-15 đã chạy thật. Trường `session` dùng dữ liệu mẫu tới khi SD-04 tick · **Quy mô:** M

### SD-UI8: Một mã khách vãng lai cho mọi request đầu trang
**Mô tả:** Backend cấp cookie `pk_anon` cho request không mang cookie này (`ActorMiddleware.ReadOrIssueAnonymousId`). Lần đầu khách mở trang, trình duyệt gửi cùng lúc nhiều request mà chưa có cookie. Ví dụ trên `/support`: `/api/me` của header, giỏ hàng và `POST /api/support/conversations`. Mỗi request nhận một mã khác nhau, và cookie về sau cùng ghi đè các cookie trước. Hội thoại hoặc giỏ hàng tạo theo mã cũ thành dữ liệu mồ côi, và request sau nhận 404.

Máy chủ không biết hai request cùng lúc có đến từ một trình duyệt hay không. Vì vậy task sửa ở frontend: request API đầu tiên của tab chạy một mình, và các request khác chờ request đó xong. Lúc đó trình duyệt đã có cookie.

Người dùng chọn cách sửa này ngày 2026-11-04. Backend không đổi.

**Tiêu chí nghiệm thu:**
- [ ] `apiRequest` trong `apps/frontend/src/shared/api.ts` giữ một promise cho request đầu tiên của tab. Request khác gửi sau khi promise đó xong, kể cả khi request đầu lỗi
- [ ] Request đầu lỗi mạng hoặc lỗi HTTP không chặn mãi các request sau
- [ ] `EventSource` của `stock-live.tsx` và `support-event-stream.ts` chỉ mở sau khi request đầu xong
- [ ] Không còn lời gọi `fetch` hay `EventSource` tới `/api/` đi vòng qua bước chờ. Kiểm bằng `rg "fetch\(|new EventSource" apps/frontend/src`
- [ ] Vitest: hai lời gọi `apiRequest` cùng lúc thì `fetch` thứ hai chỉ chạy sau khi `fetch` thứ nhất trả kết quả
- [ ] Vitest: sau request đầu, các request chạy song song như trước, không chờ nhau
- [ ] E2E: mở thẳng `/support` bằng context mới, không có cookie. Mọi response của lần tải đầu đặt tổng cộng 1 cookie `pk_anon`
- [ ] E2E: mở thẳng trang chủ bằng context mới. Mọi response của lần tải đầu đặt tổng cộng 1 cookie `pk_anon`
- [ ] Bỏ bước gọi `/api/me` trước khi mở trang trong `e2e/support-customer-page.spec.ts`. Mọi case vẫn xanh
- [ ] Bộ `pnpm test:e2e` đầy đủ vẫn xanh
**Kiểm tra:** `pnpm lint && pnpm typecheck && pnpm test`; `pnpm test:e2e`
**Phụ thuộc:** SD-UI7 · **Quy mô:** S
**File:** `apps/frontend/src/shared/api.ts`, `apps/frontend/src/features/catalog/shared/stock-live.tsx`, `apps/frontend/src/features/support-desk/shared/support-event-stream.ts`

### SD-UI6: Form số điện thoại và form CSAT phía khách
**Mô tả:** `CallbackRequestForm` và `CsatForm` trong `apps/frontend/src/features/support-desk/customer/`. Trang `/support` của SD-UI7 render hai form theo `action` của tin hệ thống (quyết định 21). Ngày 2026-10-18 task đổi nơi gắn form từ widget của `shopping-assistant` sang `/support`.
**Tiêu chí nghiệm thu:**
- [x] Schema `zod` đọc được các ví dụ gọi lại và chấm điểm của `customer-api.json`
- [x] Form số điện thoại kiểm bằng Zod cùng luật với backend. Lỗi hiện dưới ô bằng `FieldError`
- [x] Lỗi 429 `CALLBACK_LIMIT_REACHED` hiện thông báo tiếng Việt, form không gửi lại
- [x] Gửi thành công thì form đổi thành lời xác nhận
- [x] Form CSAT có 5 nút chọn điểm dùng được bằng bàn phím, mỗi nút có tên truy cập
- [x] Ảnh chụp trong khung chat ở 1280 px và 375 px không tràn ngang
- [x] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** SD-UI7, SD-C2 · **Quy mô:** M

### SD-UI2: Khung chat và mẫu trả lời nhanh
**Mô tả:** Cột giữa của màn CSKH: lịch sử tin có nhãn khách, nhân viên, hệ thống. Ô soạn có nút gửi và gõ `/` để chọn mẫu. Ngày 2026-10-18 task bỏ nhãn bot, nguồn bot và nút "Gợi ý trả lời". Ngày 2026-11-03 task thêm nút "Nhận" ở phiên đang chờ.
**Tiêu chí nghiệm thu:**
- [x] Schema `zod` đọc được mọi ví dụ nhận phiên, gửi tin và lịch sử tin của `admin-sessions.json`, và ví dụ danh sách của `admin-canned-responses.json`
- [x] Nút "Nhận" gọi `POST /api/admin/support/sessions/{id}/assignment`. Lỗi 409 `SUPPORT_SESSION_ALREADY_CLAIMED` hiện "Đã có người nhận"
- [x] Sự kiện `message.created` của hội thoại đang mở làm lịch sử đọc lại
  Kiểm đầu cuối ở SD-E2E1.
- [x] Tin `staff` có `staffDisplayName` bằng `null` hiện "Nhân viên CSKH"
- [x] Gõ `/` mở danh sách mẫu. Mũi tên lên, xuống chọn mẫu. Enter chèn mẫu đã thay biến
- [x] Ô soạn tắt và có chú thích khi nhân viên không giữ phiên
- [x] Lỗi 403 `SUPPORT_SESSION_NOT_ASSIGNED` hiện bằng `ErrorAlert`
- [x] Lịch sử hiện mọi phiên cũ của cùng hội thoại, có dòng phân cách giữa các phiên
- [x] Có `label` cho ô soạn. Dùng được bằng bàn phím
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang
- [x] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest cho bộ chọn mẫu và ô soạn; ảnh chụp
**Phụ thuộc:** SD-UI1, SD-C2 · **Quy mô:** M

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
**Phụ thuộc:** SD-UI2, SD-C2 · **Quy mô:** M

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
**Phụ thuộc:** SD-UI1, SD-C2 · **Quy mô:** S

Hết task frontend của Phase 2. Báo người dùng để chạy Checkpoint SD2 trong `tasks/support-desk/todo.md`.

---

## Phase 3: Cấu hình và số liệu

### SD-UI5: Màn cấu hình CSKH
**Mô tả:** Màn `/admin/support/settings` cho giờ làm việc, ngày nghỉ và mẫu dùng chung. Màn quản lý mẫu cá nhân nằm trong màn CSKH. Menu cấu hình chỉ hiện với người có `support-settings:manage`.
**Tiêu chí nghiệm thu:**
- [ ] Schema `zod` đọc được mọi ví dụ của `admin-settings.json` và `admin-canned-responses.json`
- [ ] Mẫu có `editable` bằng `false` không có nút sửa và nút xóa
- [ ] Lưu thành công hiện thông báo. Lỗi hiện dưới đúng ô bằng `FieldError`
- [ ] Mốc nhắc lại không nhỏ hơn mốc tự đóng: lỗi hiện dưới ô mốc nhắc lại, theo ví dụ lỗi của `admin-settings.json`
- [ ] Ngày nghỉ chọn bằng ô ngày có `label`, xóa được từng ngày
- [ ] Người thiếu quyền không thấy menu. Mở thẳng URL thấy trang không đủ quyền
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; E2E `--headed`
**Phụ thuộc:** SD-UI1, SD-C2 · **Quy mô:** M

Hết task frontend của Phase 3. Báo người dùng để chạy Checkpoint SD3 trong `tasks/support-desk/todo.md`. Phase 4 cũng nằm trong file đó.
