# Implementation Plan: chatbot-widget

## Tóm tắt dễ hiểu

Chatbot của Pickory nằm ở một dự án riêng tên `chatbot-rag`. Dự án đó chạy như một dịch vụ độc lập, giống một cửa hàng khác bên cạnh. Pickory chỉ treo khung chat của dịch vụ đó lên trang của mình.

Module này làm 3 việc:

1. Cho người triển khai khai báo địa chỉ dịch vụ chatbot bằng biến môi trường.
2. Hiện khung chat ở các trang khách, không hiện ở trang quản trị.
3. Giữ trang mua hàng chạy bình thường khi dịch vụ chatbot bị sập.

Không khai báo địa chỉ thì Pickory không hiện khung chat, và mọi trang vẫn chạy như cũ.

## Overview

Ngày 2026-10-18 người dùng chuyển chatbot sang dự án riêng `chatbot-rag` (`C:\Users\juven\Desktop\chatbot-rag`). Module `shopping-assistant` bị bỏ. Module này thay phần chatbot phía khách bằng widget của `chatbot-rag`.

`chatbot-rag` nhúng vào website bằng một thẻ `<script src=".../widget.js" data-api="...">`. Widget render trong Shadow DOM, nên CSS hai bên không ảnh hưởng nhau. Widget gọi `POST /api/chat` của `chatbot-rag`, không gọi API của Pickory.

Module chỉ phụ thuộc `identity` (layout trang khách đã có). Module không có bảng DB.

## Đồ thị phụ thuộc

```
CBW-01 cấu hình dịch vụ chatbot và route công khai
 └── CBW-UI1 tải widget trên trang khách
      └── CBW-E2E1 trang mua hàng chạy khi dịch vụ chatbot lỗi (thay COM-E2E5)
CBW-02 tài liệu triển khai (song song với CBW-UI1)
CBW-E2E1, CBW-02 └── CBW-03 nghiệm thu
```

## Architecture Decisions

1. Địa chỉ dịch vụ đọc từ biến môi trường của API: `CHATBOT_WIDGET_SCRIPT_URL` và `CHATBOT_API_URL`. Frontend không build cứng địa chỉ vào bundle. Một bản build chạy được trên nhiều môi trường, đúng hướng deploy Docker lên VPS.
2. Frontend đọc cấu hình qua route công khai `GET /api/chatbot-widget`. Route trả `enabled: false` khi thiếu biến. Route không có DB nên đặt trong `Pickory.Api`, cạnh `/api/health`.
3. API kiểm hai địa chỉ khi khởi động. Địa chỉ phải là URL tuyệt đối. Ngoài `Development`, địa chỉ phải dùng `https`. Chỉ đặt một trong hai biến thì API dừng khi khởi động, giống luật Cloudinary trong `AppConfig.cs`.
4. Widget chỉ tải trong `CustomerLayout` của `apps/frontend/src/app.tsx`. Trang `/admin` dùng `AdminLayout`, nên không có khung chat.
5. Widget tải sau khi trang khách hiện xong, bằng thẻ `script` có `async`. Lỗi tải script không làm hỏng trang. Script chỉ tải một lần, kể cả khi khách chuyển trang.
6. Trang Pickory hiện chưa có header `Content-Security-Policy`. Khi thêm header này, phải cho phép domain của `chatbot-rag` ở `script-src` và `connect-src`. CBW-02 ghi điều này vào tài liệu triển khai.
7. Mỗi website dùng một bản `chatbot-rag` riêng, với bộ tài liệu và index riêng. `chatbot-rag` MVP chưa hỗ trợ nhiều website trên một bản.

## Yêu cầu với `chatbot-rag`

Các mục sau thuộc dự án `chatbot-rag`. Plan ghi lại để theo dõi khi tích hợp.

1. Nạp tài liệu từ `GET /api/help-documents` của `knowledge-base` (KB-14 của `tasks/knowledge-base/todo.md`). Route trả Markdown và `updatedAt` của tài liệu công khai. MVP của `chatbot-rag` chỉ nạp PDF và Word, nên cần thêm loader đọc route này.
2. Nạp lại tài liệu khi `updatedAt` đổi, để chatbot không trả lời theo chính sách cũ.
3. Cấu hình `form_url` của fallback trỏ tới `/support` của Pickory, nơi khách chat với CSKH.
4. Thêm link vào fallback theo loại câu hỏi:
   - câu về giá hoặc sản phẩm cụ thể: link trang tìm kiếm của Pickory. Trang này chờ CAT-14 của `tasks/catalog/todo.md`. Trước đó, link trỏ tới trang chủ;
   - câu về trạng thái đơn: link `/order-lookup`, trang tra đơn cho khách vãng lai.
5. Thêm origin của Pickory vào danh sách CORS.
6. Đọc IP thật từ `X-Forwarded-For` của proxy tin cậy. Pickory chạy trên VPS sau reverse proxy, nên thiếu bước này thì rate limit gom mọi khách về một IP.
7. Che số điện thoại, email và địa chỉ khách gõ vào chat, trước khi gửi sang LLM và trước khi ghi log. Luật 9 của `spec/CAPABILITY-MAP.md` cấm gửi thông tin định danh sang API AI.
8. Hiện một dòng thông báo quyền riêng tư trong widget trước câu hỏi đầu tiên. SPEC của `chatbot-rag` đã ghi rủi ro thiếu thông báo này.
9. Cho website chỉnh màu chính, tiêu đề, lời chào và vị trí khung chat qua thuộc tính `data-*`. Khung chat không được che banner đồng ý cookie hay nút giỏ hàng trên điện thoại.
10. Soạn bộ câu hỏi eval theo nghiệp vụ Pickory: đổi trả, bảo hành, phí vận chuyển, COD, hạn mức COD, thời gian giao.
11. Giai đoạn sau: thêm tool tìm sản phẩm gọi `GET /api/products?q=` của Pickory. Bot chỉ nêu giá và tồn kho lấy từ kết quả của lượt đó.

## Task List

### Phase 1: Cấu hình và widget
- [ ] CBW-01 · [ ] CBW-UI1 · [ ] CBW-02

### Checkpoint W1
- [ ] Đặt hai biến môi trường trỏ tới `chatbot-rag` chạy ở máy dev. Khung chat hiện ở trang chủ, không hiện ở `/admin`
- [ ] Review với người dùng

### Phase 2: Nghiệm thu
- [ ] CBW-E2E1 · [ ] CBW-03

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Review với người dùng

## Traceability: tiêu chí → task

| Tiêu chí | Task | Bằng chứng |
|---|---|---|
| Thiếu biến môi trường thì không có khung chat, mọi trang chạy như cũ | CBW-01, CBW-UI1 | Test tích hợp route; Vitest |
| Khung chat chỉ hiện ở trang khách | CBW-UI1, CBW-E2E1 | Vitest; E2E |
| Dịch vụ chatbot lỗi thì khách vẫn mua hàng | CBW-E2E1 | E2E với địa chỉ script không truy cập được |
| Người triển khai làm theo tài liệu là gắn được chatbot | CBW-02 | Người dùng tự làm theo ở Checkpoint W1 |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
|---|---|---|
| Script lỗi tải sinh lỗi console, làm E2E "console 0 lỗi" đỏ | Trung bình | CBW-E2E1 chỉ cho phép lỗi tải của đúng URL script chatbot, theo cách `isAllowedLoadFailure` trong `catalog-admin-flow.ts` |
| Widget che nút trên điện thoại | Trung bình | Kiểm ảnh chụp 375 px ở Checkpoint W1; yêu cầu 9 với `chatbot-rag` |
| `chatbot-rag` đổi cách nhúng script | Thấp | Pickory chỉ phụ thuộc thẻ `script` và `data-api`. CBW-02 ghi phiên bản `chatbot-rag` đã kiểm |
| Lưu câu hỏi của khách chưa có thông báo quyền riêng tư | Cao | Yêu cầu 8 với `chatbot-rag`; Câu hỏi mở 1 |

## Câu hỏi mở

1. Đã chốt ngày 2026-10-18: widget không chờ khách đồng ý cookie, vì khách tự mở khung chat. Widget hiện thông báo quyền riêng tư trước câu đầu (yêu cầu 8). Nên hỏi bộ phận pháp lý trước khi chạy thật.
2. Domain chạy `chatbot-rag` khi deploy là gì? CBW-02 cần domain này để ghi ví dụ cấu hình CORS.
