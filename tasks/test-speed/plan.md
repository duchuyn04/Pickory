# Implementation Plan: test-speed

## Tóm tắt

Integration test của backend chạy rất lâu. Lần chạy đủ suite sau SD-05 (ngày 2026-11-04) cho 2521 kết quả trong 50 phút. Mỗi task backend phải chờ test nhiều lần, nên một task mất nhiều giờ.

Số đo của lần chạy đó cho thấy thời gian mất ở đâu:

- Phần thân test, tức các bước gọi API và kiểm kết quả, chỉ chiếm 39%.
- Phần dựng dữ liệu trước mỗi test chiếm 59%. Phần lớn là tạo tài khoản thử, và mỗi tài khoản phải băm mật khẩu bằng Argon2.
- Khởi động bản API của mỗi lớp test chỉ chiếm 2%.
- Không test nào chờ quá 4,6 giây.

Plan giảm thời gian chạy theo ba bước:

1. Ghi số đo. Task đầu viết số đo ở trên thành file `baseline.md`, để các task sau so sánh.
2. Dựng dữ liệu nhanh hơn. Test dùng lại mật khẩu đã băm. Lớp test tạo tài khoản một lần cho cả lớp, thay vì tạo lại trước mỗi test.
3. Chạy song song. Hiện mọi test chạy nối đuôi nhau trên một DB. Sau plan, mỗi nhóm test có DB riêng và chạy cùng lúc.

Ví dụ đời thường: hiện mỗi khách vào quầy đều phải làm thẻ thành viên mới, và chỉ có một quầy. Bước 2 cho khách dùng lại thẻ cũ. Bước 3 mở thêm quầy, mỗi quầy có két tiền riêng.

Plan không đổi code sản phẩm và không thêm package.

## Bối cảnh kỹ thuật

Số đo lấy từ file `trx` của lần chạy đủ suite sau SD-05, trên worktree `feat/support-desk-backend`:

- Suite có 2521 kết quả, chạy 50,1 phút, xanh.
- Thân test tổng cộng 19,5 phút (39%).
- Phần ngoài thân test giữa hai test cùng lớp là 29,5 phút (59%). Phần này gồm constructor, `InitializeAsync` và `DisposeAsync` của lớp test. Trung bình mỗi test mất 745 ms ở phần này.
- Test đầu của mỗi lớp khởi động một bản API. Phần này tổng cộng 1,1 phút (2%).
- `PermissionMatrixTests` có 283 kết quả và chạy 11,3 phút, nhưng thân test chỉ mất 5 giây. Mỗi test tạo 4 tài khoản bằng `IdentityTestData.CreateUserAsync`.
- `IdentityTestData.CreateUserAsync` chạy `SeedRolesAsync` rồi băm mật khẩu bằng `PasswordHasher.Hash`. Một lần băm Argon2 mất khoảng 85 đến 170 ms lúc máy rảnh. Test gọi hàm này ở khoảng 280 chỗ.
- `IntegrationDatabaseFixture` không dọn DB giữa các test. Fixture xóa schema và chạy migration một lần cho cả suite.
- Cả 135 lớp test integration dùng chung collection `IntegrationDatabaseCollection`. xUnit không chạy song song các test trong cùng collection.
- Máy có 8 GB RAM. Postgres 17 bản Windows chạy ở cổng 5433.

## Quyết định

1. Plan chạy trên nhánh `feat/support-desk-backend`, sau khi SD-06 được commit và trước SD-07. Nhánh gộp vào `main` ở Checkpoint SD2 cùng các task support-desk.
2. Task chỉ sửa test và hạ tầng test, không đổi code sản phẩm.
3. Không làm yếu test để chạy nhanh hơn. Test vẫn kiểm cùng hành vi. Mỗi lớp test sửa phải qua phép thử đột biến.
4. TS-01 lấy số đo từ file `trx` có sẵn của SD-05. Task không chạy lại suite 50 phút, trừ khi file này không đọc được.
5. Ngày 2026-11-04 người dùng chọn sửa plan theo số đo. Task bỏ chờ thời gian thật cũ được thay bằng TS-02 dựng dữ liệu nhanh hơn, vì không test nào chờ quá 4,6 giây.
6. Ngày 2026-11-04 người dùng bỏ task dùng chung bản API. Khởi động API chỉ chiếm 2%, dưới ngưỡng 20% mà plan cũ đặt ra.
7. TS-02 dùng lại hash của cùng một mật khẩu. Argon2 lưu salt trong chuỗi hash, nên hai tài khoản cùng hash vẫn đăng nhập đúng. Test kiểm `PasswordHasher` trực tiếp vẫn băm thật.
8. Lớp test chỉ tạo dữ liệu một lần cho cả lớp khi không test nào trong lớp sửa dữ liệu đó. Ví dụ: test khóa tài khoản hay đổi role phải dùng tài khoản riêng của test đó.
9. TS-03 đặt số luồng song song tối đa là 3. Máy 8 GB RAM không chạy nổi nhiều bản API và Postgres cùng lúc.
10. DB của từng nhóm lấy tên từ `TEST_DATABASE_URL` cộng hậu tố, ví dụ `pickory_test_p1`. Mọi luật an toàn hiện có áp cho từng DB: tên phải có thành phần `test`, không trùng `DATABASE_URL`.
11. TS-02 và TS-03 đụng `tests/Pickory.IntegrationTests/Infrastructure/` hoặc helper dùng chung của test. Reviewer chạy toàn bộ suite sau mỗi task.

## Task List

- [x] TS-01 ghi số đo gốc
- [x] TS-02 dựng dữ liệu test nhanh hơn
- [x] TS-03 chạy song song theo nhóm DB

Đã bỏ ngày 2026-11-04: task bỏ chờ thời gian thật cũ và task dùng chung bản API (quyết định 5 và 6).

### Checkpoint TS

- [x] Suite đầy đủ xanh 2 lần liên tiếp sau TS-03
- [x] Thời gian suite đầy đủ giảm ít nhất 40% so với số đo của TS-01
- [ ] Review với người dùng

## Rủi ro

| Rủi ro | Cách giảm |
|---|---|
| Hai test trong một lớp dùng chung tài khoản, và một test sửa tài khoản đó | Quyết định 8. Phép thử đột biến chạy lớp theo thứ tự khác và chạy riêng từng test |
| Test cần hash khác nhau giữa hai tài khoản | Hash dùng lại chỉ áp cho `IdentityTestData`. Test của `PasswordHasher` vẫn băm thật |
| Test chạy song song lộ lỗi chập chờn do dùng chung trạng thái trong bộ nhớ | Mỗi nhóm có bản API và DB riêng. Chạy suite 2 lần liên tiếp trước khi tick |
| Máy thiếu RAM khi chạy 3 nhóm cùng lúc | Giới hạn 3 luồng. Đo RAM trống trong lúc chạy và ghi vào báo cáo |
| Chia nhóm sai làm hai lớp cùng xóa dữ liệu của nhau | Một lớp thuộc đúng một nhóm. Test kiến trúc kiểm mọi lớp có collection |
