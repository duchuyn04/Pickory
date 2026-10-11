# Triển khai Pickory lên Vercel, Render và Supabase

Tài liệu này mô tả cách đưa Pickory lên môi trường production. Mỗi nền tảng chạy một phần:

| Nền tảng | Phần chạy | File cấu hình |
|---|---|---|
| Vercel | Web React Vite trong `apps/frontend` | `apps/frontend/vercel.json` |
| Render | API .NET 10, chạy bằng Docker | `render.yaml`, `apps/backend/Dockerfile` |
| Supabase | PostgreSQL có extension pgvector | Không có file, cấu hình trên Dashboard |

## Luồng request

Trình duyệt chỉ gọi domain Vercel. Vercel chuyển tiếp các đường dẫn sau tới Render:

- `/api/*`: mọi route của API, gồm callback Google và webhook.
- `/p/{slug}` và `/c/{slug}`: trang sản phẩm và trang danh mục có thẻ meta SEO (Search Engine Optimization).

Các đường dẫn khác trả `index.html` của SPA (Single Page Application). Cookie phiên đặt `SameSite=Lax`, nên cookie phải ở cùng domain với web. Nếu web gọi thẳng domain Render, trình duyệt coi cookie phiên là cookie bên thứ ba.

## Cách giữ `index.html` khớp với asset

API chèn thẻ meta vào `index.html` cho `/p/{slug}` và `/c/{slug}`. File này chứa tên file JS và CSS có hash nội dung. Dockerfile build lại web để lấy `index.html`. Vercel build web từ cùng commit và cùng lockfile, nên hash khớp nhau.

Hai nền tảng cùng triển khai nhánh `main`. Render build lại API khi `apps/frontend` thay đổi, vì `buildFilter` trong `render.yaml` có thư mục này.

## Bước 1: tạo DB trên Supabase

1. Tạo project mới trên Supabase.
2. Chọn vùng Southeast Asia (Singapore).
3. Ghi lại mật khẩu DB.
4. Mở Connect trên trang project.
5. Chọn Session pooler.
6. Sao chép chuỗi kết nối dạng URI.
7. Thêm `?sslmode=require` vào cuối chuỗi.

Chuỗi kết quả có dạng sau:

```text
postgresql://postgres.<project-ref>:<mật khẩu>@aws-0-ap-southeast-1.pooler.supabase.com:5432/postgres?sslmode=require
```

Lý do chọn Session pooler:

- Direct connection chỉ có IPv6. Render có thể không kết nối được qua IPv6.
- Transaction pooler (cổng 6543) đổi kết nối DB sau mỗi transaction. Trạng thái gắn với kết nối, ví dụ prepared statement, không còn ở lệnh sau.

Mã hóa ký tự đặc biệt trong mật khẩu theo dạng URL, ví dụ `@` thành `%40`. `PostgresConnectionString` giải mã mật khẩu trước khi kết nối.

## Bước 2: tạo API trên Render

1. Đẩy `render.yaml`, `.dockerignore` và `apps/backend/Dockerfile` lên nhánh `main`.
2. Mở Render Dashboard.
3. Chọn New.
4. Chọn Blueprint.
5. Chọn repo Pickory.
6. Nhập giá trị cho các biến mà Render hỏi. Xem bảng biến ở bước 4.
7. Ghi lại URL của service, ví dụ `https://pickory-api.onrender.com`.

Đặt tạm `WEB_ORIGIN` và `API_BASE_URL` bằng domain Vercel dự kiến. Sửa lại hai biến này sau bước 3 nếu domain khác.

`render.yaml` chọn gói `starter`. Gói free ngủ sau 15 phút không có request. Khi service ngủ, các worker nền dừng:

- `ShipmentStatusSyncWorker`
- `AddressSyncWorker`
- `ImageDeletionWorker`

Webhook GHN và SePay gọi vào service đang ngủ phải chờ service khởi động lại. Bên gửi có thể hết thời gian chờ.

### Migration khi triển khai

Render chạy `dotnet Pickory.Api.dll db:migrate` trước mỗi lần triển khai, theo `preDeployCommand` trong `render.yaml`. Lệnh áp migration của mọi module theo thứ tự trong `DatabaseMigrationCommand`.

- Lệnh thành công: Render chuyển request sang bản mới.
- Lệnh lỗi: Render hủy lần triển khai. Bản API cũ tiếp tục chạy.
- Log của lần triển khai ghi tên module lỗi, ví dụ `Migration Catalog thất bại: ...`.

Không bật extension `vector` trên Dashboard của Supabase. Supabase đặt extension đó vào schema `extensions`. Migration AiGateway tạo extension trong schema `public`.

Pre-deploy chỉ có ở gói trả phí. Khi dùng gói free, xóa `preDeployCommand` khỏi `render.yaml`. Sau đó chạy `db:migrate` từ máy dev theo phần "Chạy lệnh trên DB production từ máy dev" ở bước 6.

## Bước 3: tạo web trên Vercel

Sửa domain Render trong `apps/frontend/vercel.json` trước khi tạo project. Nếu domain sai, mọi request `/api` trả lỗi.

1. Thay `pickory-api.onrender.com` bằng domain Render ở bước 2.
2. Đẩy thay đổi lên nhánh `main`.
3. Mở Vercel Dashboard.
4. Chọn Add New.
5. Chọn Project.
6. Chọn repo Pickory.
7. Đặt Root Directory là `apps/frontend`.
8. Đặt Production Branch là `main`.
9. Chọn Deploy.

Vercel đọc lệnh cài gói, lệnh build và thư mục kết quả từ `vercel.json`. Web không có biến môi trường riêng.

## Bước 4: biến môi trường trên Render

| Biến | Giá trị |
|---|---|
| `DATABASE_URL` | Chuỗi Session pooler ở bước 1 |
| `WEB_ORIGIN` | Domain Vercel, ví dụ `https://pickory.vercel.app` |
| `API_BASE_URL` | Cùng giá trị với `WEB_ORIGIN` |
| `TRUST_PROXY` | `2`, xem phần kiểm tra số hop |
| `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` | Ứng dụng OAuth của Google |
| `CLOUDINARY_CLOUD_NAME`, `CLOUDINARY_API_KEY`, `CLOUDINARY_API_SECRET` | Tài khoản Cloudinary |
| `GHN_API_BASE` | `https://online-gateway.ghn.vn` |
| `GHN_TOKEN`, `GHN_SHOP_ID`, `GHN_WEBHOOK_SECRET` | Tài khoản GHN production |
| `SEPAY_WEBHOOK_API_KEY` | Khóa webhook trên SePay |
| `LLM_API_KEY`, `LLM_MODEL` | Khóa và ID model của provider LLM (Large Language Model) |
| `EMBEDDING_API_KEY` | Khóa provider embedding |

API dừng khi khởi động nếu thiếu một trong các nhóm sau:

- Ba khóa Cloudinary.
- `GHN_TOKEN`, `GHN_SHOP_ID` hoặc `GHN_WEBHOOK_SECRET`.
- `SEPAY_WEBHOOK_API_KEY`, khi bản triển khai có module Commerce.

`API_BASE_URL` là domain Vercel, không phải domain Render. API ghép callback Google từ biến này. Callback phải đi qua Vercel để cookie state và cookie phiên nằm trên domain web.

Dockerfile đã đặt `ASPNETCORE_ENVIRONMENT`, `WEB_INDEX_HTML_PATH`, `PICKORY_SKIP_DOTENV` và `DEV_OUTBOX_DIR`. Không đặt lại các biến này trên Render.

## Bước 5: cấu hình dịch vụ bên ngoài

1. Thêm `https://<domain Vercel>/api/auth/google/callback` vào Authorized redirect URIs trên Google Cloud Console.
2. Đặt URL webhook GHN là `https://<domain Vercel>/api/webhooks/ghn`.
3. Mở tab Connect của service trên Render.
4. Sao chép danh sách Outbound IP.
5. Thêm các IP đó vào danh sách IP được phép của token GHN, nếu tài khoản GHN giới hạn IP.

`GhnHttpClient` ghi log lỗi khi GHN trả HTTP 401. Lỗi này có thể do token sai hoặc do IP của Render chưa được phép.

API chưa có route webhook SePay. Đặt URL webhook SePay khi module Commerce có route này. Dùng domain Vercel và tiền tố `/api/webhooks`.

## Bước 6: tạo tài khoản admin đầu tiên

Tạo admin sau khi lần triển khai đầu thành công. Lúc đó migration đã tạo bảng.

### Chạy lệnh trên DB production từ máy dev

Cách này không cần Render Shell. Render Shell chỉ có ở gói trả phí.

Giữ chuỗi kết nối Supabase trong biến môi trường của terminal. Không ghi chuỗi này vào `.env`, vì `.env` dùng cho DB dev.

1. Mở terminal.
2. Đặt biến `DATABASE_URL` của terminal bằng chuỗi Session pooler ở bước 1.
3. Mở thư mục `apps/backend/src`.
4. Chạy `dotnet run --project Pickory.Api -- identity:seed`.
5. Chạy `dotnet run --project Pickory.Api -- identity:create-admin --email <email> --password <mật khẩu>`.
6. Đóng terminal.

Trong PowerShell, đặt biến bằng `$env:DATABASE_URL = '<chuỗi kết nối>'`. Trong Bash, đặt biến bằng `export DATABASE_URL='<chuỗi kết nối>'`.

API đọc `.env` với độ ưu tiên thấp nhất. Biến `DATABASE_URL` của terminal thắng giá trị trong `.env`, nên lệnh ghi vào DB Supabase.

## Bước 7: kiểm tra sau lần triển khai đầu

1. Mở `https://<domain Vercel>/api/health`.
2. Kiểm tra kết quả có `"status":"ok"`.
3. Mở một trang `/p/{slug}` có thật.
4. Mở mã nguồn của trang.
5. Kiểm tra thẻ meta của sản phẩm.
6. So sánh thẻ `<script>` của `/p/{slug}` với thẻ `<script>` của trang chủ.
7. Đăng nhập bằng email và mật khẩu.
8. Tải lại trang.
9. Kiểm tra phiên còn giữ.

Nếu thẻ `<script>` ở mục 6 khác nhau, hai bản build không cùng commit. Chạy Manual Deploy cho service Render.

### Kiểm tra số hop của `TRUST_PROXY`

Request tới API đi qua proxy của Vercel, rồi qua proxy của Render. Giá trị `2` giả định cả hai proxy thêm một mục vào `X-Forwarded-For`. Giới hạn tần suất của identity đếm request theo IP này.

1. Gửi nhiều request đăng nhập sai từ một máy.
2. Kiểm tra API chặn máy đó.
3. Gửi request đăng nhập từ máy khác.
4. Kiểm tra API không chặn máy thứ hai.

Nếu API chặn cả hai máy, API đang đọc IP của Vercel. Tăng giá trị `TRUST_PROXY` thêm 1.

Request gửi thẳng tới domain Render chỉ đi qua một proxy. Người gửi có thể tự ghi `X-Forwarded-For` và đổi IP mà API thấy. Rủi ro này chỉ ảnh hưởng tới giới hạn tần suất theo IP.

## Giới hạn hiện tại

- Dự án chưa có dịch vụ gửi thư thật. `DevOutboxEmailSender` ghi thư vào `/tmp/pickory-outbox` trong container. Người dùng không nhận được thư đăng ký và thư đặt lại mật khẩu.
- Supabase gói free tạm dừng project sau 7 ngày không hoạt động.
- Migration chỉ tự chạy ở gói trả phí của Render. Ở gói free, quên chạy `db:migrate` sau khi `main` có migration mới thì API lỗi khi đọc bảng mới.
