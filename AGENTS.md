# Quy tắc cho agent trong repo Pickory

## 1. Không dùng em dash

Không dùng ký tự em dash (—) ở bất kỳ đâu: code, comment, commit message, tài liệu, nội dung UI, câu trả lời cho người dùng. Khi cần ngắt ý thì dùng dấu phẩy, dấu hai chấm, hoặc tách thành câu riêng. Khi cần nối hai cụm liên quan thì dùng gạch ngang thường `-`, không dùng `—` hay `–`.

## 2. Thiết kế test case thủ công bằng Playwright

Khi được yêu cầu thiết kế kịch bản test thủ công (manual test case) cho một tính năng hay module:

- Tối đa 100 kịch bản cho một lần thiết kế. Nếu phạm vi lớn hơn 100 case thì chọn các luồng quan trọng nhất, không cố nhồi đủ mọi trường hợp.
- Kịch bản chạy bằng Playwright, không phải mô tả tay bấm chuột của người dùng.
- Mỗi kịch bản có: ID, nhóm, các bước, kết quả mong đợi, Priority, trạng thái, ghi chú.
- Trạng thái chỉ nhận 1 trong 4 giá trị: `Chưa chạy`, `PASS`, `FAILED`, `UNKNOWN`.
  - `PASS`: kết quả thực tế khớp kết quả mong đợi.
  - `FAILED`: không khớp, phải ghi kết quả thực tế và bằng chứng (ảnh chụp hoặc log).
  - `UNKNOWN`: không chạy được trong môi trường hiện tại, phải ghi rõ lý do.
  - Không tự đổi trạng thái thành PASS khi chưa thực sự chạy.
- Sắp xếp kịch bản theo Priority, nhóm cao xuống thấp: P0 (bảo mật, dữ liệu, luồng không thể hỏng), P1 (luồng nghiệp vụ chính), P2 (kiểm tra dữ liệu nhập và lỗi), P3 (trải nghiệm, khả năng truy cập, giao diện).
- Không sửa bước hay kết quả mong đợi của kịch bản chỉ để case thành PASS. Thấy kịch bản viết sai thì báo lại người giao việc, không tự âm thầm đổi cho khớp kết quả.

## 3. Code dễ hiểu, tường minh

Ưu tiên code rõ ràng, đọc một lần hiểu luôn hơn code ngắn nhưng phải suy luận. Đặt tên biến, hàm, file theo đúng nghĩa việc nó làm. Tránh viết tắt khó đoán, tránh lồng nhiều tầng điều kiện hay callback khi có thể tách hàm nhỏ.

## 4. Chú thích từng dòng, không chú thích tổng quát

Chú thích giải thích lý do của từng dòng hoặc từng bước xử lý cụ thể, không viết một câu chú thích chung cho cả khối code. Chú thích trả lời "vì sao làm vậy" hơn là lặp lại "đang làm gì" (code đã tự nói lên việc đang làm). Comment bằng tiếng Việt.

Sai (chú thích tổng quát):

```ts
// Xử lý đăng nhập
function login(email, password) {
  const user = findUser(email);
  if (!user) throw new Error('not found');
  const ok = verify(user.hash, password);
  if (!ok) throw new Error('wrong password');
  return createSession(user);
}
```

Đúng (chú thích từng dòng, giải thích lý do):

```ts
function login(email, password) {
  const user = findUser(email);
  // Không tìm thấy user vẫn phải báo lỗi giống sai mật khẩu, tránh lộ email nào đã đăng ký.
  if (!user) throw new Error('Email hoặc mật khẩu không đúng.');
  const ok = verify(user.hash, password);
  if (!ok) throw new Error('Email hoặc mật khẩu không đúng.');
  // Tạo phiên mới, không tái dùng phiên cũ để tránh cố định session (session fixation).
  return createSession(user);
}
```
