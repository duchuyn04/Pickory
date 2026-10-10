# Kế hoạch và danh sách task

Mỗi thư mục con là một module hoặc một đợt việc. Mỗi thư mục có 2 file:

- `plan.md`: quyết định thiết kế, đồ thị phụ thuộc, các Phase, dòng tiến độ và bảng truy vết tiêu chí của spec.
- `todo.md`: từng task với tiêu chí nghiệm thu dạng checkbox.

Spec của module nằm ở `spec/SPEC-<module>.md`. Thứ tự build theo `spec/CAPABILITY-MAP.md`.

## Mục lục

| Thư mục | Nội dung | Trạng thái |
|---|---|---|
| `identity-ai-gateway/` | `identity` và `ai-gateway` (bản Node, mã `ID`, `AI`, `UI`) | Xong phần code. Còn mở các mục cần API key thật và các mục review với người dùng |
| `dotnet-migration/` | Chuyển backend từ Node.js sang .NET (mã `DN`) | Xong |
| `design/` | Đổi design system theo `shop-retail` (mã `DS`) | Xong |
| `catalog/` | `catalog` (mã `CAT`, `UI-C`) | Xong. Còn 3 mục chờ người dùng tự test |
| `shipping/` | `shipping` (mã `SHIP`, `UI-S`) | Xong Phase 2. Còn SHIP-09 (nghiệm thu, cần token sandbox) |
| `rest-routes/` | Đổi route cũ đặt theo hành động sang REST (mã `REST`) | Chưa bắt đầu |
| `commerce/` | `commerce` (mã `COM`) | Chưa bắt đầu. Cần plan đổi route sang REST xong trước |
| `activity-tracking/` | `activity-tracking` (mã `ACT`) | Chưa bắt đầu |
| `smart-search/` | `smart-search` (mã `SRCH`) | Chưa bắt đầu |
| `recommendations/` | `recommendations` (mã `REC`) | Chưa bắt đầu |
| `personalized-content/` | `personalized-content` (mã `PCON`) | Chưa bắt đầu |
| `knowledge-base/` | `knowledge-base` (mã `KB`) | Chưa bắt đầu |
| `shopping-assistant/` | `shopping-assistant` (mã `SA`) | Chưa bắt đầu |
| `support-desk/` | `support-desk` (mã `SD`) | Chưa bắt đầu |
| `admin-console/` | `admin-console` (mã `ADM`) | Chưa bắt đầu |

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
