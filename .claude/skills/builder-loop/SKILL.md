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
   - Đặt tên pane của bạn bằng `herdr agent rename "$HERDR_PANE_ID" reviewer`. Builder dùng tên này để báo khi xong.
3. Tìm tiêu chí của task trong `tasks/todo-*.md` và `tasks/plan-*.md`. Đọc spec và code liên quan.
   - Ghi các điểm mơ hồ.
   - Ghi phần hạ tầng còn thiếu, ví dụ test chưa port.
   - Ghi chỗ tiêu chí còn viết theo bản Node. Backend hiện là .NET tại `apps/backend`.
4. Viết file giao việc `<task>-handoff.md`, gồm:
   - luật bắt buộc: `CLAUDE.md` mục 1 đến 6, không em dash hay en dash, không `/// <summary>`, không commit, không push, không `git add -A`, `git add .` hay `git add -u`;
   - đường dẫn cấm stage hoặc xóa: `.agents/`, `skills-lock.json`, `docs/research/`, file `.docx` và `.pdf`, `.claude/settings*.json` (cấm cả đọc);
   - các bước theo thứ tự, mỗi bước có kết quả kiểm tra được;
   - mục "Agent skills" chép từ phần cùng tên bên dưới;
   - yêu cầu TDD, và phép thử đột biến cho test xanh ngay lần đầu;
   - lệnh kiểm tra và yêu cầu ghi kết quả thật;
   - bước đánh dấu tiến độ, chạy sau khi lệnh kiểm tra xanh:
     - Tick `[x]` từng tiêu chí đã đạt trong `tasks/todo-*.md`.
     - Tick mã task ở dòng tiến độ của `tasks/plan-*.md` khi mọi tiêu chí của task đã đạt.
     - Giữ `[ ]` cho tiêu chí chưa kiểm được. Thêm một dòng thụt lề bên dưới ghi lý do và task sẽ kiểm.
     - Không tick mục "Review với người dùng". Người dùng tự xác nhận mục này.
     - Không sửa nội dung tiêu chí khi tick.
     - Liệt kê các tiêu chí đã tick và chưa tick trong báo cáo.
   - đường dẫn file báo cáo, và câu trả lời cố định `XONG, báo cáo ở <đường dẫn>`;
   - bước cuối: sau khi ghi báo cáo, chạy `herdr agent prompt reviewer "XONG, báo cáo ở <đường dẫn>"`.
5. Gửi bằng `herdr agent prompt builder "<câu ngắn trỏ tới file giao việc>"`.
6. Kết thúc lượt và chờ tin nhắn `XONG, báo cáo ở <đường dẫn>` từ builder. Tin nhắn đến pane của bạn như một tin nhắn mới.
   - Không chạy vòng lặp chạy nền để chờ. Claude Code dừng tiến trình nền khi máy thiếu bộ nhớ.
   - Không dùng `herdr agent wait` ngay sau prompt. Lệnh có thể trả về trước khi builder bắt đầu làm.
   - Người dùng báo builder bị kẹt: đọc màn hình bằng `herdr agent read builder`. Hỏi người dùng trước khi trả lời thay builder.
7. Review theo skill `agent-skills:code-review-and-quality`. Không tin báo cáo của builder, tự kiểm tra:
   - `git status --porcelain` và `git diff --cached --name-only`: không có đường dẫn cấm bị stage. Nếu có, bỏ stage bằng `git reset -q -- <đường dẫn>`, rồi ghi vi phạm vào review.
   - Đọc test trước code. Test phải kiểm tra hành vi và trường hợp biên của tiêu chí.
   - Tự làm ít nhất 1 phép thử đột biến trên luật quan trọng nhất. Đột biến vẫn xanh là một lỗi: ghi test còn thiếu.
   - Xét đủ 5 trục: đúng đắn, dễ đọc, kiến trúc, bảo mật, hiệu năng.
   - Xét kích thước: file vượt khoảng 1000 dòng hoặc code lặp là dấu hiệu cần tách.
   - Đối chiếu mọi khẳng định trong code, comment và tài liệu với file thật.
   - Tiêu chí đã tick không bị viết lại thành nghĩa khác hoặc yếu hơn.
   - Mỗi tiêu chí builder đã tick có test hoặc bằng chứng chạy thật. Tick không có bằng chứng là lỗi `Bắt buộc`.
   - Builder đã tick đủ các tiêu chí đạt trong `tasks/todo-*.md` và `tasks/plan-*.md`. Thiếu tick là lỗi `Bắt buộc`.
   - Comment theo `CLAUDE.md` mục 4 và 5. Chạy script kiểm tra comment nếu phiên có.
   - Không có em dash hay en dash trong các dòng thêm mới.
   - Tự chạy lại build và test sau lần sửa cuối có đổi code.
8. Nếu chưa đạt:
   1. Viết `<task>-review-<N>.md`. Mỗi lỗi ghi vị trí, lý do và cách sửa cụ thể. Gắn mức cho từng lỗi: `Critical`, `Bắt buộc`, `Optional` hoặc `Nit`. Xếp lỗi đúng đắn và bảo mật lên đầu.
   2. Đổi tên báo cáo cũ thành `<task>-builder-report-r<N>.md`.
   3. Gửi review cho builder. Ghi lại bước cuối `herdr agent prompt reviewer` trong file review.
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

## Agent skills

Builder (omp) đã cài bộ `agent-skills`. File giao việc yêu cầu builder dùng 3 skill theo bước 3, 4, 5 của vòng đời:

1. **Build:** `/build` (`incremental-implementation` và `test-driven-development`). Làm từng lát nhỏ, mỗi lát đi qua đỏ, xanh, refactor.
2. **Verify:** `/test` (`test-driven-development`).
   - Refactor sau khi xanh: gom code lặp và đặt lại tên cho rõ.
   - Logic thuần (không DB, không HTTP) có unit test trong `Pickory.UnitTests` hoặc Vitest.
   - Test phụ thuộc thời gian hoặc chạy đồng thời phải cho kết quả ổn định. Ghi cách đảm bảo vào báo cáo.
   - Với UI: Playwright kiểm tra console có 0 lỗi và 0 cảnh báo.
   - Với UI: chạy Playwright có giao diện trình duyệt. Người dùng muốn xem trình duyệt khi test chạy.
     - Chạy cả bộ bằng `pnpm test:e2e --headed` trong `apps/frontend`.
     - Chạy một spec bằng `npx playwright test <spec> --headed`.
     - Dùng `--headed` cho lần chạy đỏ, lần chạy đột biến và lần chạy cuối.
     - Không sửa `playwright.config.ts`. CI vẫn chạy ẩn.
     - Ghi cờ `--headed` cạnh từng lệnh trong báo cáo.
     - Test phải xanh ở cả hai chế độ. Trình duyệt có giao diện có thể tự tải `favicon.ico`, và màn hình scale có thể làm số đo viền lệch.
3. **Review:** `/review` (`code-review-and-quality`). Builder tự review 5 trục trước khi báo cáo. Báo cáo có mục "Tự review" liệt kê lỗi đã tự sửa.

Luật của repo thắng luật của skill khi hai bên khác nhau:

- Không commit sau mỗi lát, dù skill `/build` yêu cầu. Reviewer commit khi người dùng yêu cầu.
- Không dùng `/build auto`.
- Theo kỷ luật phạm vi của `incremental-implementation`. Ghi việc thấy ngoài phạm vi vào mục "Ngoài phạm vi" của báo cáo, không tự sửa.

Nếu omp không gọi được skill, đọc thẳng file `SKILL.md` trong `C:/Users/juven/.claude/plugins/cache/addy-agent-skills/agent-skills/<phiên bản>/skills/`.

## Báo cáo cho người dùng

Viết bằng tiếng Việt, ngắn gọn:

- kết quả và số lượt review;
- lỗi chính đã bắt được ở mỗi lượt;
- số liệu kiểm tra do bạn tự chạy lại;
- câu hỏi còn chờ người dùng quyết.

Trong lúc builder chạy, báo trạng thái ngắn sau mỗi lượt review.
