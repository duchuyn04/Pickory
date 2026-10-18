# Kế hoạch và danh sách task

Mỗi thư mục con là một module hoặc một đợt việc. Mỗi thư mục có 2 file:

- `plan.md`: quyết định thiết kế, đồ thị phụ thuộc, các Phase, dòng tiến độ và bảng truy vết tiêu chí của spec.
- `todo.md`: từng task với tiêu chí nghiệm thu dạng checkbox.

Spec của module nằm ở `spec/SPEC-<module>.md`. Thứ tự build theo `spec/CAPABILITY-MAP.md`.

## Mục lục

| Thư mục | Nội dung | Trạng thái |
|---|---|---|
| `identity-ai-gateway/` | `identity` và `ai-gateway` (bản Node, mã `ID`, `AI`, `UI`) | Xong phần code. Ngày 2026-10-18 người dùng quyết định gỡ `ai-gateway`, xem `ai-gateway-removal/` |
| `dotnet-migration/` | Chuyển backend từ Node.js sang .NET (mã `DN`) | Xong |
| `design/` | Đổi design system theo `shop-retail` (mã `DS`) | Xong |
| `catalog/` | `catalog` (mã `CAT`, `UI-C`) | Xong. Còn 3 mục chờ người dùng tự test. `CAT-14` (tìm theo từ khóa, thêm ngày 2026-10-14) chưa bắt đầu |
| `shipping/` | `shipping` (mã `SHIP`, `UI-S`) | Xong, đã review với người dùng |
| `rest-routes/` | Đổi route cũ đặt theo hành động sang REST (mã `REST`) | Xong. Còn mục review với người dùng ở Checkpoint R1 và Checkpoint cuối |
| `commerce/` | `commerce` (mã `COM`) | Đang ở Phase 7. COM-E2E0 tới COM-E2E4 xong. COM-E2E5 chuyển sang CBW-E2E1. Còn COM-24 |
| `activity-tracking/` | `activity-tracking` (mã `ACT`) | Chưa bắt đầu. Ngày 2026-10-18 chỉ giữ phần đo chuyển đổi |
| `smart-search/` | `smart-search` (mã `SRCH`) | Đã bỏ ngày 2026-10-14. Chỉ giữ để tra cứu lịch sử |
| `recommendations/` | `recommendations` (mã `REC`) | Đã bỏ ngày 2026-10-14. Chỉ giữ để tra cứu lịch sử |
| `personalized-content/` | `personalized-content` (mã `PCON`) | Đã bỏ ngày 2026-10-14. Chỉ giữ để tra cứu lịch sử |
| `knowledge-base/` | `knowledge-base` (mã `KB`) | Chưa bắt đầu. Ngày 2026-10-18 bỏ phần AI, giữ `/help` và API tài liệu công khai |
| `shopping-assistant/` | `shopping-assistant` (mã `SA`) | Đã bỏ ngày 2026-10-18. Chỉ giữ để tra cứu lịch sử |
| `chatbot-widget/` | Nhúng widget của `chatbot-rag` (mã `CBW`) | Chưa bắt đầu |
| `ai-gateway-removal/` | Gỡ `ai-gateway` khỏi Pickory (mã `AIR`) | Chưa bắt đầu |
| `support-desk/` | `support-desk` (mã `SD`) | Chưa bắt đầu. Ngày 2026-10-18 đổi thành chat CSKH độc lập, không qua bot |
| `admin-console/` | `admin-console` (mã `ADM`) | Chưa bắt đầu. Ngày 2026-10-18 bỏ báo cáo hiệu quả AI |

Ngày 2026-10-14 người dùng bỏ ba module `smart-search`, `recommendations` và `personalized-content`. Phần AI cá nhân hóa chỉ còn chatbot tư vấn `shopping-assistant`. Chi tiết ở mục "Thay đổi phạm vi ngày 2026-10-14" của `spec/CAPABILITY-MAP.md`.

Ngày 2026-10-18 người dùng chuyển chatbot sang dự án riêng `chatbot-rag`. Pickory bỏ `shopping-assistant`, gỡ `ai-gateway` và chỉ nhúng widget chatbot. Chi tiết ở mục "Thay đổi phạm vi ngày 2026-10-18" của `spec/CAPABILITY-MAP.md`.

Cập nhật cột "Trạng thái" khi một module đổi Phase hoặc xong Checkpoint.

## Thêm module mới

1. Tạo thư mục `tasks/<module>/`, với `<module>` là mã module trong `spec/CAPABILITY-MAP.md`.
2. Viết `plan.md` theo mẫu `shipping/plan.md`.
3. Viết `todo.md` theo mẫu `shipping/todo.md`.
4. Chọn tiền tố mã task chưa có trong bảng trên.
5. Thêm một dòng vào mục lục.

## Đánh dấu tiến độ

1. Tick `[x]` từng tiêu chí đã đạt trong `todo.md`.
2. Tick mã task ở dòng tiến độ của `plan.md` khi mọi tiêu chí của task đã đạt.
3. Giữ `[ ]` cho tiêu chí chưa kiểm được. Thêm một dòng thụt lề ghi lý do và task sẽ kiểm.
4. Không tick mục "Review với người dùng". Người dùng tự xác nhận mục này.
