# Nhật ký triển khai Pickory lần đầu

Tài liệu này ghi lại các thao tác thật trong lần triển khai đầu, theo thứ tự thực hiện. Mỗi thao tác có lý do, để người đọc học cách triển khai. Các bước chuẩn nằm ở `docs/deployment.md`.

Tài liệu không chứa mật khẩu, khóa API hoặc chuỗi kết nối đầy đủ. Các giá trị bí mật được ghi bằng `<...>`.

## Phạm vi

Lần triển khai này chỉ có code trên nhánh `main` tại commit `161c6d7`. Bản này gồm các module Identity, AiGateway, Catalog và Shipping. Module Commerce chưa có trong bản này.

## Phần 0: chuẩn bị code

Trước khi tạo dịch vụ, repo cần các file cấu hình triển khai. Commit `161c6d7` thêm các file sau:

| File | Vai trò |
|---|---|
| `apps/backend/Dockerfile` | Build image cho API |
| `.dockerignore` | Loại secret và file thừa khỏi context build |
| `render.yaml` | Mô tả service trên Render |
| `apps/frontend/vercel.json` | Cấu hình build và chuyển tiếp request trên Vercel |
| `apps/backend/src/Pickory.Api/Deployment/DatabaseMigrationCommand.cs` | Lệnh `db:migrate` |

Commit này được tạo trong một worktree riêng từ `main`. Nhánh `feat/commerce` có code Commerce chưa xong, nên commit không lấy từ nhánh đó.

Các kiểm tra trước khi push:

1. Build solution .NET. Kết quả: 0 warning, 0 error.
2. Chạy unit test và architecture test. Kết quả: 932 và 6 test thành công.
3. Build image bằng `docker build -f apps/backend/Dockerfile .` tại gốc repo.
4. Tạo DB trống trên Postgres dev.
5. Chạy `db:migrate` trong image trên DB trống. Kết quả: bốn module áp xong, mã thoát 0.
6. Chạy API trong image. Kết quả: `/api/health` trả `ok`.

Bài học: kiểm tra image ở máy trước khi đẩy lên nền tảng. Lỗi build trên Render khó đọc hơn lỗi ở máy.

## Phần 1: tạo DB trên Supabase

### Thao tác

1. Đăng nhập Supabase Dashboard.
2. Mở trang tạo project của organization có sẵn (`dictly`, gói Free).
3. Đặt Project name là `pickory`.
4. Nhập mật khẩu DB mới.
5. Chọn Region là `Southeast Asia (Singapore) ap-southeast-1`.
6. Bỏ chọn Automatically expose new tables.
7. Bỏ chọn Enable Data API.
8. Giữ Enable automatic RLS ở trạng thái tắt.
9. Chọn Create new project.
10. Chờ trạng thái project đổi từ Coming up sang Healthy.

### Lý do của từng lựa chọn

**Organization có sẵn.** Gói Free cho mỗi organization một số project miễn phí. Không cần tạo organization mới.

**Mật khẩu DB.** Mật khẩu có 32 ký tự, sinh ngẫu nhiên, chỉ gồm chữ cái và chữ số. Mật khẩu không có ký tự đặc biệt, nên không cần mã hóa khi ghép vào `DATABASE_URL`. Lưu mật khẩu vào trình quản lý mật khẩu. Supabase không hiện lại mật khẩu sau khi tạo.

**Vùng Singapore.** Service Render cũng ở Singapore. API gọi DB nhiều lần trong mỗi request, nên hai dịch vụ ở cùng vùng giảm độ trễ.

**Tắt Data API.** Data API tự tạo REST API cho schema `public`. Pickory không dùng API này, vì API .NET kết nối thẳng tới Postgres. Schema `public` chứa bảng người dùng có hash mật khẩu. Nếu Data API bật, mọi bảng có quyền cho role `anon` đều đọc được qua Internet.

**Tắt Automatically expose new tables.** Tùy chọn này cấp quyền trên bảng mới cho các role của Data API. Ô này bị khóa khi Data API tắt, nhưng vẫn giữ giá trị đã chọn. Vì vậy, bỏ chọn ô này trước, rồi mới tắt Data API. Phần "Phát hiện: role `anon` đọc được mọi bảng" cho thấy bước này chưa đủ.

**Tắt automatic RLS.** RLS (Row Level Security) giới hạn dòng mà mỗi role đọc được. Pickory kiểm tra quyền trong API .NET, không dùng RLS. Trigger tự bật RLS có thể ảnh hưởng tới migration, nên giữ tắt.

### Lấy chuỗi kết nối

1. Mở trang project.
2. Chờ nút Connect hết bị khóa. Nút này bị khóa trong khoảng 2 phút đầu.
3. Chọn Connect.
4. Chọn tab Direct.
5. Chọn Connection Method là Session pooler.
6. Sao chép chuỗi kết nối.

Chuỗi nhận được có dạng sau:

```text
postgresql://postgres.dppqxeufdnkvfmhizgyh:[YOUR-PASSWORD]@aws-0-ap-southeast-1.pooler.supabase.com:5432/postgres
```

Ghép chuỗi thành `DATABASE_URL`:

1. Thay `[YOUR-PASSWORD]` bằng mật khẩu DB.
2. Thêm `?sslmode=require` vào cuối chuỗi.

Tên người dùng là `postgres.<project-ref>`, không phải `postgres`. Pooler dùng phần sau dấu chấm để biết request thuộc project nào.

Trang Connect có ba cách kết nối:

| Cách | Địa chỉ | Kết luận |
|---|---|---|
| Direct connection | `db.<ref>.supabase.co:5432`, chỉ IPv6 | Không chọn. Render có thể không kết nối được qua IPv6. |
| Transaction pooler | Cổng 6543 | Không chọn. Pooler đổi kết nối DB sau mỗi transaction, nên trạng thái của kết nối bị mất. |
| Session pooler | `aws-0-ap-southeast-1.pooler.supabase.com:5432`, có IPv4 | Chọn. |

### Kiểm tra kết nối từ máy dev

Máy dev không cài `psql`. Image `pgvector/pgvector:pg17` của môi trường dev có sẵn `psql`, nên chạy `psql` trong container.

```bash
docker run --rm pgvector/pgvector:pg17 psql "$DATABASE_URL" -tAc "select version()"
```

Kết quả:

- Phiên bản: PostgreSQL 17.11.
- Người dùng sau khi đăng nhập: `postgres`.
- Extension `vector` 0.8.2 có sẵn nhưng chưa cài.

`pg_stat_ssl` báo `f` cho kết nối này. Giá trị đó chỉ mô tả đoạn từ pooler tới DB, là mạng nội bộ của Supabase. Đoạn từ máy dev tới pooler có TLS (Transport Layer Security), vì `sslmode=require` chặn kết nối không có TLS.

### Chạy migration từ máy dev

Lệnh `db:migrate` thường chạy trên Render. Lần này lệnh chạy từ máy dev trước, để tìm sớm lỗi riêng của Supabase. Ví dụ: role `postgres` của Supabase có thể không có quyền tạo extension trong `public`.

```bash
docker run --rm -e DATABASE_URL="$DATABASE_URL" <các biến bắt buộc khác> pickory-api:deploy dotnet Pickory.Api.dll db:migrate
```

Image chạy ở môi trường `Production`, nên API yêu cầu khóa Cloudinary và GHN khi khởi động. Lệnh migration không dùng các khóa này, nên giá trị giả là đủ.

Kết quả:

- Bốn module áp xong, mã thoát 0.
- Schema `public` có 25 bảng.
- Extension `vector` nằm trong `public`.
- Có bốn schema lịch sử: `ef_identity`, `ef_ai_gateway`, `ef_catalog`, `ef_shipping`.

Lần triển khai đầu trên Render vẫn chạy `db:migrate`. Lệnh không áp gì thêm, vì mọi migration đã có trong bảng lịch sử.

### Phát hiện: role `anon` đọc được mọi bảng

Sau migration, truy vấn sau kiểm tra quyền của hai role mà Data API dùng:

```sql
select r.rolname, count(*) filter (where has_table_privilege(r.rolname, c.oid, 'SELECT'))
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
cross join (values ('anon'), ('authenticated')) r(rolname)
where n.nspname = 'public' and c.relkind = 'r'
group by r.rolname;
```

Kết quả: cả `anon` và `authenticated` đọc được 25/25 bảng. Bỏ chọn Automatically expose new tables không thu hồi quyền này. Supabase cấp quyền qua default privileges của schema `public`.

Data API đang tắt, nên chưa có ai đọc được bảng qua Internet. Rủi ro xuất hiện nếu ai đó bật lại Data API. Bảng `users` chứa hash mật khẩu.

Lệnh sau thu hồi quyền. API .NET đăng nhập bằng `postgres`, là chủ sở hữu bảng, nên API không bị ảnh hưởng.

```sql
BEGIN;
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM anon, authenticated;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM anon, authenticated;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public REVOKE ALL ON TABLES FROM anon, authenticated;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public REVOKE ALL ON SEQUENCES FROM anon, authenticated;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public REVOKE ALL ON FUNCTIONS FROM anon, authenticated;
COMMIT;
```

Hai lệnh `REVOKE` đầu thu hồi quyền trên bảng đã có. Ba lệnh `ALTER DEFAULT PRIVILEGES` chặn quyền trên bảng mà migration tạo sau này.

Kết quả sau khi thu hồi:

- `anon` và `authenticated` đọc được 0/25 bảng.
- `anon` không có quyền dùng schema `ef_identity` và `ef_catalog`.
- `postgres` vẫn đọc được bảng `users`.

Bài học: tùy chọn trên giao diện có thể không làm đúng điều người đọc nghĩ. Sau khi tạo DB, kiểm tra quyền thật bằng SQL.

## Trạng thái hiện tại

| Phần | Trạng thái |
|---|---|
| Supabase | Đã tạo project `pickory`, đã có schema, đã thu hồi quyền của `anon` |
| Render | Chưa tạo |
| Vercel | Chưa tạo |
