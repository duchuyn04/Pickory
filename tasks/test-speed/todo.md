# Task List: test-speed

Quyết định và bối cảnh nằm ở `tasks/test-speed/plan.md`. Tiến độ chung nằm ở mục "Task List" của file đó.

## Phase 1: Đo và dựng dữ liệu nhanh hơn

### TS-01: Ghi số đo gốc
**Mô tả:** Đọc file `trx` của lần chạy đủ suite sau SD-05. Ghi kết quả vào `tasks/test-speed/baseline.md`. Không sửa code. Đường dẫn file `trx` nằm trong file giao việc của task.
**Tiêu chí nghiệm thu:**
- [ ] `baseline.md` ghi lệnh đã chạy, thời điểm chạy và tổng thời gian
- [ ] `baseline.md` ghi số kết quả đạt, lỗi và bỏ qua
- [ ] `baseline.md` tách tổng thời gian thành ba phần: thân test, phần giữa hai test cùng lớp, và khởi động API ở test đầu mỗi lớp
- [ ] `baseline.md` có 20 test chậm nhất, mỗi dòng có tên test và thời gian
- [ ] `baseline.md` có 10 lớp test chậm nhất theo tổng thời gian. Mỗi dòng có tổng thời gian, thời gian thân test và số kết quả
- [ ] `baseline.md` ghi cách tính từng con số, để TS-02 và TS-03 đo lại theo cùng cách
**Kiểm tra:** reviewer tính lại tổng thời gian và một dòng của bảng lớp chậm nhất từ file `trx`
**Phụ thuộc:** SD-06 đã commit · **Quy mô:** S

### TS-02: Dựng dữ liệu test nhanh hơn
**Mô tả:** Giảm phần dựng dữ liệu trước mỗi test, theo bảng lớp chậm nhất của TS-01. Có hai cách:
- `IdentityTestData` băm mỗi mật khẩu một lần rồi dùng lại hash đó.
- Lớp test tạo tài khoản và phiên đăng nhập một lần cho cả lớp, khi không test nào sửa dữ liệu đó (quyết định 8).
**Tiêu chí nghiệm thu:**
- [ ] `IdentityTestData.CreateUserAsync` không băm lại mật khẩu đã băm trong cùng lần chạy suite
- [ ] Đăng nhập bằng tài khoản tạo từ hash dùng lại vẫn thành công. Có test chứng minh
- [ ] `PermissionMatrixTests` tạo 4 tài khoản thử một lần cho cả lớp
- [ ] Mỗi lớp trong 10 lớp chậm nhất của TS-01 có một trong hai kết quả: đã sửa, hoặc có lý do giữ nguyên trong báo cáo
- [ ] Test đã sửa vẫn kiểm cùng hành vi. Không xóa hay nới lỏng câu kiểm nào
- [ ] Phép thử đột biến: với mỗi lớp đã sửa, làm sai code sản phẩm mà lớp đó kiểm, và test phải đỏ
- [ ] Mỗi lớp đã sửa xanh khi chạy riêng từng test, không chỉ khi chạy cả lớp
- [ ] Suite đầy đủ xanh. Báo cáo ghi tổng thời gian mới và so với TS-01 theo cách tính của `baseline.md`
**Kiểm tra:** chạy từng lớp đã sửa, rồi reviewer chạy suite đầy đủ
**Phụ thuộc:** TS-01 · **Quy mô:** M

## Phase 2: Chạy song song

### TS-03: Chạy song song theo nhóm DB
**Mô tả:** Chia lớp test integration thành các collection theo module. Mỗi collection có `IntegrationDatabaseFixture` riêng và DB riêng, nên các collection chạy song song.
**Tiêu chí nghiệm thu:**
- [ ] Mỗi lớp test integration thuộc đúng một collection. Test kiến trúc lỗi khi một lớp thiếu collection
- [ ] Mỗi collection dùng DB có tên lấy từ `TEST_DATABASE_URL` cộng hậu tố. Fixture tạo DB khi DB chưa có
- [ ] Luật an toàn của fixture áp cho từng DB: tên có thành phần `test`, không trùng `DATABASE_URL`, khóa advisory riêng cho từng DB
- [ ] Hai suite chạy cùng lúc trên cùng `TEST_DATABASE_URL` vẫn báo lỗi khóa, như trước
- [ ] `xunit.runner.json` giới hạn tối đa 3 luồng song song
- [ ] Lớp tự tạo `new PickoryWebApplicationFactory(database)` vẫn chạy đúng
- [ ] Suite đầy đủ xanh 2 lần liên tiếp
- [ ] Báo cáo ghi tổng thời gian mới, so với TS-01, và RAM trống thấp nhất trong lúc chạy
**Kiểm tra:** reviewer chạy suite đầy đủ 2 lần liên tiếp
**Phụ thuộc:** TS-02 · **Quy mô:** L

Đã bỏ ngày 2026-11-04: task bỏ chờ thời gian thật cũ và task dùng chung bản API. Lý do ở quyết định 5 và 6 của `plan.md`.
