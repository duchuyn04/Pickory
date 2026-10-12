# ADR 0004: Xác minh tài liệu SePay cho thanh toán

- Ngày: 2026-10-12.
- Trạng thái: Đã xác minh tài liệu. Chưa đối chiếu webhook thật vì người dùng chưa cấp khóa SePay.
- Liên quan: `spec/SPEC-commerce.md`, `tasks/commerce/plan.md` (quyết định 10 đến 12), `tasks/commerce/todo.md` (COM-00, COM-11, COM-UI4).

## Phạm vi và cách xác minh

ADR chỉ chốt hợp đồng theo tài liệu công khai. ADR không xác nhận hành vi của tài khoản SePay thật.

Quyết định Pickory giữ API key theo plan. Endpoint thành công trả HTTP 200 với `{"success": true}` theo nguồn S1 và S2.

QR (Quick Response) là mã hai chiều. VA (Virtual Account) là tài khoản ảo theo nguồn S1.

Nguồn SePay chứng minh hợp đồng bên ngoài. Nguồn nội bộ chứng minh ánh xạ của Pickory, không phải cam kết của SePay.

## Nguồn

Các URL sau đã được đọc ngày 2026-10-12. Mã nguồn S1 đến S5 được dẫn tại từng tiêu chí.

| Mã | URL | Đoạn trích ngắn |
| --- | --- | --- |
| S0 | https://docs.sepay.vn/ | "Tự động hoá thanh toán với SePay." |
| S1 | https://developer.sepay.vn/vi/sepay-webhooks/tich-hop-webhook | "Giá trị này không đổi qua mọi lần retry và replay, dùng làm khóa chống trùng." |
| S2 | https://docs.sepay.vn/tich-hop-webhooks.html | "HTTP status 200 hoặc 201"; "Body JSON có `{"success": true}`" |
| S3 | https://developer.sepay.vn/vi/sepay-webhooks/xac-thuc | `Authorization: Apikey YOUR_API_KEY` |
| S4 | https://developer.sepay.vn/vi/sepay-webhooks/xu-ly-loi | "Tổng cộng 8 lần gửi (1 lần đầu cộng 7 lần retry)" |
| S5 | https://docs.sepay.vn/tao-qr-code-vietqr-dong.html | "Chỉ `acc` và `bank` là bắt buộc" |
| S6 | https://docs.sepay.vn/lap-trinh-webhooks.html | "Với API Key, bạn cần kiểm tra xem SePay có gửi API Key đúng trong header hay không." |

Nguồn nội bộ:

- N1: `spec/SPEC-commerce.md`, mục "Thanh toán SePay", bước 3 đến 5: "Idempotent theo id giao dịch SePay".
- N2: `tasks/commerce/plan.md`, quyết định 10: "xác thực trước, lưu thô trước khi khớp".
- N3: `tasks/commerce/plan.md`, quyết định 11: "Khóa SePay chỉ nằm trong biến môi trường".
- N4: `tasks/commerce/plan.md`, quyết định 12: "Số tài khoản nhận nằm trong cấu hình admin".
- N5: `apps/backend/src/Pickory.Modules.Commerce/Persistence/Migrations/20261011034624_InitialCommerce.cs`, bảng `payment_transactions` và `commerce_settings`.
- N6: `tasks/commerce/todo.md`, COM-11: "Giao dịch tiền ra hoặc sai số tài khoản được lưu và bỏ qua".

## Tiêu chí 1: payload webhook

S1 mô tả HTTP POST và bảng kiểu dữ liệu. Trích: "Định dạng `YYYY-MM-DD HH:mm:ss`, giờ Việt Nam."

S1 cũng nêu: "Số tiền giao dịch, đơn vị VNĐ, luôn dương."

VNĐ là đồng Việt Nam. Bảng dưới giữ tên trường chính xác theo S1.

| Tên | Kiểu | Ý nghĩa và giới hạn theo S1 | Ví dụ |
| --- | --- | --- | --- |
| `id` | `integer` | Mã giao dịch SePay, không null, ổn định khi gửi lại | `92704` |
| `gateway` | `string` | Tên ngân hàng, không null | `"Vietcombank"` |
| `transactionDate` | `string` | Giờ Việt Nam, định dạng `YYYY-MM-DD HH:mm:ss`, không null | `"2024-07-02 11:08:33"` |
| `accountNumber` | `string` | Số tài khoản ngân hàng, không null | `"1017588888"` |
| `subAccount` | `string` | VA khớp giao dịch, rỗng khi không khớp | `""` |
| `code` | `string` hoặc `null` | Mã thanh toán theo cấu hình tiền tố, không phải VA | `"SEVN63DC8E5C"`, `null` |
| `content` | `string` | Nội dung chuyển khoản gốc, không null | `"SEVN63DC8E5C chuyen tien"` |
| `transferType` | `string` | Chiều giao dịch, không null | `"in"`, `"out"` |
| `description` | `string` | Mô tả ngân hàng, có thể rỗng | `"NGUYEN VAN A chuyen tien"` |
| `transferAmount` | `integer` | Số tiền dương, đơn vị VNĐ, không null | `5000000` |
| `accumulated` | `integer` | Số dư sau giao dịch, bằng 0 nếu ngân hàng không hỗ trợ | `105000000` |
| `referenceCode` | `string` | Mã tham chiếu ngân hàng, có thể rỗng | `"FT24012345678"` |

S1 nêu: "`code = null` nghĩa là không có mã thanh toán". Không đồng nhất `null` với chuỗi rỗng.

Ví dụ JSON đầy đủ dưới đây lấy từ S1 và S2. Đây là dữ liệu tài liệu, không phải webhook thật của Pickory.

```json
{
  "id": 92704,
  "gateway": "Vietcombank",
  "transactionDate": "2024-07-02 11:08:33",
  "accountNumber": "1017588888",
  "subAccount": "",
  "code": "SEVN63DC8E5C",
  "content": "SEVN63DC8E5C chuyen tien",
  "transferType": "in",
  "description": "NGUYEN VAN A chuyen tien",
  "transferAmount": 5000000,
  "accumulated": 105000000,
  "referenceCode": "FT24012345678"
}
```

S1 liệt kê `application/json` là mặc định. S1 còn hỗ trợ form, nhưng Pickory chỉ nhận JSON theo N2.

Giới hạn số nguyên của SePay và hành vi khi thiếu trường: chưa xác minh. S1 không nêu hai giới hạn này.

## Tiêu chí 2: API key

Nguồn S3, mục "API Key", ghi header sau. S2 xác nhận cùng định dạng.

```http
Authorization: Apikey YOUR_API_KEY
```

| Tên | Kiểu | Ý nghĩa | Ví dụ |
| --- | --- | --- | --- |
| `Authorization` | Header chuỗi | Tiền tố `Apikey`, một dấu cách, khóa đã cấu hình | `Apikey YOUR_API_KEY` |

`YOUR_API_KEY` chỉ là ký hiệu minh họa của S3, không phải khóa thật.

Quyết định Pickory theo N3: khóa nằm trong `SEPAY_WEBHOOK_API_KEY`. Phép so thời gian cố định theo N2 bảo vệ bước xác thực.

S3 yêu cầu chọn API Key khi tạo webhook. Cấu hình thực tế của tài khoản Pickory: chưa xác minh vì chưa có khóa.

## Tiêu chí 3: phản hồi và retry

Nguồn S1, mục "Phản hồi hợp lệ", yêu cầu đủ ba điều kiện. S2 xác nhận cùng điều kiện.

| Trường hoặc điều kiện | Kiểu | Ý nghĩa | Ví dụ |
| --- | --- | --- | --- |
| HTTP status | Số nguyên | Chỉ `200` hoặc `201` được ghi là thành công | `200` |
| `success` | Boolean JSON | Giá trị phải là `true` | `{"success": true}` |
| Thời gian phản hồi | Giây | Hoàn tất trong 30 giây | Dưới 2 giây theo N1 |
| Connect timeout | Giây | Giới hạn kết nối theo S2 | `5` |
| Response timeout | Giây | Giới hạn chờ phản hồi theo S2 | `30` |

Trích S1: "Status khác `200`/`201` (kể cả `202`, redirect, `4xx`, `5xx`)" là thất bại.

Quyết định Pickory: trả HTTP 200 với body chính xác `{"success": true}`. Mục tiêu dưới 2 giây lấy từ N1, không phải SePay.

S4 áp dụng retry khi bật "Tự động gửi lại khi server trả lỗi". Trích: "Server trả HTTP status ngoài 200-299".

Lịch dưới đây lấy nguyên số liệu từ bảng S4. Khoảng chờ tăng theo dãy Fibonacci.

| Lần gửi | Chờ sau lần trước | Tổng thời gian |
| --- | --- | --- |
| 1 | Ngay | 0 phút |
| 2 | 1 phút | 1 phút |
| 3 | 1 phút | 2 phút |
| 4 | 2 phút | 4 phút |
| 5 | 3 phút | 7 phút |
| 6 | 5 phút | 12 phút |
| 7 | 8 phút | 20 phút |
| 8 | 13 phút | 33 phút |

S4 ghi 7 lần retry ngoài lần đầu. S4 ghi giao dịch cũ hơn 5 giờ không được quét retry tiếp.

S2 chỉ ghi "Tối đa 7 lần", không phân biệt lần đầu. ADR dùng diễn giải chi tiết của S4: tổng 8 lần gửi.

S1 coi `202` hoặc body sai là thất bại. S4 chỉ nêu status ngoài `200-299` trong điều kiện retry.

Retry thực tế khi trả `202` hoặc HTTP 200 với body sai: chưa xác minh. COM-11 không dựa vào điểm mâu thuẫn này.

## Tiêu chí 4: chiều tiền và chống trùng

Nguồn S1 ghi: "`in` (tiền vào) hoặc `out` (tiền ra)."

Nguồn S2 ghi: "Cần kiểm tra trùng dựa trên trường `id`".

| Tên | Kiểu | Ý nghĩa | Ví dụ |
| --- | --- | --- | --- |
| `transferType` | `string` | Chỉ `in` được xét thanh toán theo N1 | `"in"` |
| `id` | `integer` | Khóa chống trùng ổn định theo S1 | `92704` |
| `referenceCode` | `string` | Mã tham chiếu ngân hàng, không thay `id` | `"FT24012345678"` |

S1 nêu retry, gửi lại thủ công và nhiều webhook có thể gửi cùng giao dịch. Ràng buộc unique phải bảo vệ cả lần gửi đồng thời.

Theo N5, cột thật là `providerTxId`, kiểu `text`, có index unique `payment_transactions_providerTxId_key`.

N2 viết `provider_tx_id`. Đây là tên trong plan, không phải tên cột của migration N5.

## Tiêu chí 5: URL ảnh VietQR

Nguồn S5 hiện dùng `https://vietqr.app/img`. Trang `qr.sepay.vn` được S5 giới thiệu là công cụ tạo ảnh.

Trích S5: "Ngân hàng, nhận theo một trong các field" gồm `code`, `bin`, `short_name`, `alias`.

```text
https://vietqr.app/img?acc=SO_TAI_KHOAN&bank=NGAN_HANG&amount=SO_TIEN&des=NOI_DUNG
```

| Tên | Kiểu trong URL | Ý nghĩa theo S5 | Ví dụ |
| --- | --- | --- | --- |
| `acc` | Chuỗi | Bắt buộc, số tài khoản hoặc VA, chữ và số, tối đa 19 ký tự | `0000000000` |
| `bank` | Chuỗi | Bắt buộc, mã hoặc tên ngân hàng được hỗ trợ | `VCB`, `970436`, `Vietcombank` |
| `amount` | Chuỗi chữ số | Không bắt buộc với SePay, số tiền VNĐ | `100000` |
| `des` | Chuỗi | Không bắt buộc với SePay, nội dung không dấu | `thanh%20toan%20don%20hang` |

Ví dụ đầy đủ theo S5:

<https://vietqr.app/img?acc=0000000000&bank=Vietcombank&amount=100000&des=thanh%20toan%20don%20hang>

S5 xác nhận số tài khoản trong ví dụ là dữ liệu mẫu. Ví dụ không chỉ định tài khoản nhận thật.

Quyết định Pickory theo N1: luôn điền cả bốn tham số. `des` chứa mã đơn, không chứa API key.

Ví dụ Pickory minh họa, chưa đối chiếu giao dịch thật:

<https://vietqr.app/img?acc=0000000000&bank=VCB&amount=100000&des=PKABCDEFGH>

1. Lấy `acc` từ `commerce_settings.sepayAccountNumber` theo N4 và N5.
2. Lấy `bank` từ `commerce_settings.sepayBankCode` theo N4 và N5.
3. Điền số tiền phải trả vào `amount` theo N1.
4. Mã hóa giá trị `des` cho URL theo mẫu `%20` của S5.

## Ảnh hưởng tới COM-11

Bảng ánh xạ là quyết định Pickory dựa trên N1, N2 và N5. Tên cột giữ nguyên migration, không dùng tên snake_case.

| Cột `payment_transactions` | Nguồn giá trị | Quyết định và lý do |
| --- | --- | --- |
| `id` | Mã nội bộ Pickory | Không dùng `id` SePay làm khóa chính nội bộ |
| `provider` | Hằng `sepay` theo Data Model của spec | Nhận diện nguồn giao dịch |
| `providerTxId` | `id` SePay đổi thành chuỗi thập phân | Index unique chặn cùng giao dịch theo S1 |
| `amount` | `transferAmount` | Không lấy số dư `accumulated` làm số tiền |
| `content` | `content` | Giữ nội dung gốc để tìm mã đơn |
| `raw` | Toàn bộ payload JSON | Giữ dữ liệu đối soát theo N2, không lưu header khóa |
| `orderId` | Đơn khớp từ `content` | Ban đầu null, không lấy từ `code` SePay |
| `matchStatus` | Kết quả khớp của Pickory | Không tìm thấy mã thì `unmatched` theo N6 |
| `receivedAt` | Thời điểm Pickory nhận giao dịch | Không thay bằng `transactionDate` của ngân hàng |

N5 dùng `integer` cho `amount`. Giới hạn SePay chưa rõ, nên COM-11 phải kiểm biên trước khi lưu.

S1 không đồng nhất `gateway` với mã ngân hàng dùng cho QR. `accountNumber` là trường kiểm tài khoản nhận theo S1.

Quy trình dự kiến theo N1, N2, N4 và N6:

1. Xác thực `Authorization` theo S3 trước mọi lần ghi DB.
2. Kiểm JSON theo S1 trong giới hạn 16 KB của N2.
3. Lưu giao dịch thô với `providerTxId` unique trước bước khớp đơn.
4. Bỏ qua bước khớp khi `transferType` khác `in` theo N6.
5. So `accountNumber` với `commerce_settings.sepayAccountNumber` theo N4 và N5.
6. Bỏ qua bước khớp khi số tài khoản không khớp theo N6.
7. Tìm mã trong `content` bằng quy tắc `PK[A-Z2-9]{8}` của N1.
8. Trả HTTP 200 với `{"success": true}` cho giao dịch đã nhận, kể cả lần gửi trùng.

N1 yêu cầu không phân biệt hoa thường khi tìm mã đơn. N1 yêu cầu bỏ qua ký tự ngân hàng tự chèn.

S1 ghi `code` phụ thuộc cấu hình tiền tố, có thể null. Quyết định Pickory không bắt buộc `code` để tìm đơn.

S1 phân biệt `subAccount` và `accountNumber`. Ánh xạ tài khoản thật khi dùng VA: chưa xác minh, không tự thay trường.

## Chưa đối chiếu

Tiêu chí 6 giữ mở. Người dùng chưa cấp khóa SePay, nên chưa có webhook thật đã che khóa.

| Phần chưa xác minh | Lý do | Bằng chứng cần bổ sung |
| --- | --- | --- |
| Payload thật của ngân hàng đã chọn | Chưa có giao dịch thật | JSON đã che dữ liệu nhạy cảm, đối chiếu 12 trường của S1 |
| Header xác thực thật | Chưa có khóa | Tên header và tiền tố, khóa phải được che |
| Tài khoản nhận và VA | Chưa có tài khoản thử | Đối chiếu `accountNumber`, `subAccount` với cấu hình |
| Nội dung chứa mã đơn | Chưa có chuyển khoản thật | Đối chiếu `content`, `code`, ký tự ngân hàng tự chèn |
| Phản hồi và retry thật | Chưa có webhook cấu hình | Nhật ký gửi, body phản hồi và thời gian |
| Ảnh QR trên ứng dụng ngân hàng | Chưa thử chuyển khoản thật | Số tài khoản, ngân hàng, số tiền, mã đơn sau khi quét |

Nguồn S4 cảnh báo dữ liệu "Gửi thử" chỉ gần giống thật. Giao dịch mô phỏng không thay bằng chứng tiêu chí 6.

Quy trình bổ sung khi có khóa:

1. Cấu hình webhook JSON với API Key theo S2 và S3.
2. Chuyển khoản thử tới tài khoản đã cấu hình theo S2.
3. Che khóa trước khi lưu bằng chứng theo N3.
4. Đối chiếu payload với từng dòng trong tiêu chí 1.
5. Ghi nhật ký phản hồi theo S4.
6. Tick tiêu chí 6 sau khi đối chiếu đầy đủ.
