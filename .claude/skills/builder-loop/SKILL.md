---
name: builder-loop
description: Giao một task cho agent builder chạy trong Herdr, review kết quả và yêu cầu sửa đến khi đạt. Dùng khi người dùng gõ /builder-loop <mã task> hoặc yêu cầu giao task cho builder omp.
argument-hint: <mã task> [chỉ báo cáo | commit | commit và push]
---

# Builder loop

Đối số: `$ARGUMENTS`. Từ đầu tiên là mã task, ví dụ `CAT-05`. Phần còn lại là việc làm sau khi đạt. Không có phần này thì mặc định là "chỉ báo cáo".

## Vai trò

Bạn là reviewer và người điều phối. Builder là agent `omp` chạy trong Herdr. Builder viết code của task. Bạn giao việc, review và yêu cầu sửa. Bạn không tự viết code của task.

Đặt mọi file trao đổi trong thư mục scratchpad của phiên. Builder đọc được thư mục này vì hai bên chạy trên cùng máy.

## Quy trình

1. Chạy `test "${HERDR_ENV:-}" = 1`. Nếu lệnh lỗi, báo người dùng rằng phiên không chạy trong Herdr, rồi dừng.
2. Chạy `herdr agent list` để tìm builder.
   - Có agent `omp` đang rảnh: đặt tên bằng `herdr agent rename <pane> builder`.
   - Không có: tách pane bằng `herdr pane split --current --direction right --cwd "$PWD" --no-focus`, rồi chạy `herdr agent start builder --kind omp --pane <pane mới>`.
3. Tìm tiêu chí của task trong `tasks/todo-*.md` và `tasks/plan-*.md`. Đọc spec và code liên quan.
   - Ghi các điểm mơ hồ.
   - Ghi phần hạ tầng còn thiếu, ví dụ test chưa port.
   - Ghi chỗ tiêu chí còn viết theo bản Node. Backend hiện là .NET tại `apps/backend`.
4. Viết file giao việc `<task>-handoff.md`, gồm:
   - luật bắt buộc: `CLAUDE.md` mục 1 đến 6, không em dash hay en dash, không `/// <summary>`, không commit, không push, không `git add -A`, `git add .` hay `git add -u`;
   - đường dẫn cấm stage hoặc xóa: `.agents/`, `skills-lock.json`, `docs/research/`, file `.docx` và `.pdf`, `.claude/settings*.json` (cấm cả đọc);
   - các bước theo thứ tự, mỗi bước có kết quả kiểm tra được;
   - yêu cầu TDD, và phép thử đột biến cho test xanh ngay lần đầu;
   - lệnh kiểm tra và yêu cầu ghi kết quả thật;
   - đường dẫn file báo cáo, và câu trả lời cố định `XONG, báo cáo ở <đường dẫn>`.
5. Gửi bằng `herdr agent prompt builder "<câu ngắn trỏ tới file giao việc>"`.
6. Chờ bằng một vòng lặp chạy nền. Vòng lặp chỉ dừng khi file báo cáo tồn tại và builder ở trạng thái `idle`, `done` hoặc `blocked`.
   - Không dùng `herdr agent wait` ngay sau prompt. Lệnh có thể trả về trước khi builder bắt đầu làm.
   - Gặp `blocked`: đọc màn hình bằng `herdr agent read builder`. Hỏi người dùng trước khi trả lời thay builder.
7. Review. Không tin báo cáo của builder, tự kiểm tra:
   - `git status --porcelain` và `git diff --cached --name-only`: không có đường dẫn cấm bị stage. Nếu có, bỏ stage bằng `git reset -q -- <đường dẫn>`, rồi ghi vi phạm vào review.
   - Đối chiếu mọi khẳng định trong code, comment và tài liệu với file thật.
   - Tiêu chí đã tick không bị viết lại thành nghĩa khác hoặc yếu hơn.
   - Comment theo `CLAUDE.md` mục 4 và 5. Chạy script kiểm tra comment nếu phiên có.
   - Không có em dash hay en dash trong các dòng thêm mới.
   - Tự chạy lại build và test sau lần sửa cuối có đổi code.
8. Nếu chưa đạt:
   1. Viết `<task>-review-<N>.md`. Mỗi lỗi ghi vị trí, lý do và cách sửa cụ thể.
   2. Đổi tên báo cáo cũ thành `<task>-builder-report-r<N>.md`.
   3. Gửi review cho builder.
   4. Quay lại bước 6.
9. Dừng vòng lặp và hỏi người dùng khi gặp một trong các trường hợp sau:
   - đã qua 5 lượt review mà vẫn chưa đạt;
   - builder cần đổi schema DB, thêm package hoặc đổi phạm vi;
   - tiêu chí mâu thuẫn với spec.
10. Khi đạt, làm đúng việc ghi trong đối số.
    - Commit và push qua builder bằng một file giao việc riêng.
    - Commit message không có `Co-Authored-By` hay `Generated with`.
    - Không dùng `--force`.
    - Sau đó tự kiểm tra hash commit, message và trạng thái remote.

## Báo cáo cho người dùng

Viết bằng tiếng Việt, ngắn gọn:

- kết quả và số lượt review;
- lỗi chính đã bắt được ở mỗi lượt;
- số liệu kiểm tra do bạn tự chạy lại;
- câu hỏi còn chờ người dùng quyết.

Trong lúc builder chạy, báo trạng thái ngắn sau mỗi lượt review.
