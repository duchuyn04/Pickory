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

## Phần 2: chọn gói và khóa cho Render

### Các quyết định trước khi tạo service

| Câu hỏi | Lựa chọn | Hệ quả |
|---|---|---|
| Gói Render | Free | Service ngủ sau 15 phút. Không có pre-deploy, nên `db:migrate` chạy từ máy dev. |
| Khóa GHN | Sandbox | `GHN_API_BASE` là `https://dev-online-gateway.ghn.vn`. Đơn tạo ra không phải đơn thật. |
| Khóa Cloudinary | Tài khoản có sẵn | Người triển khai tự dán khóa vào form của Render. |
| Khóa Google, LLM, embedding | Chưa có | Để trống. |

Trước khi tạo service, code được kiểm tra để biết khóa nào bắt buộc ở `Production`:

- Thiếu khóa Cloudinary: `ImageStorageRegistration.EnsureConfigured` dừng API khi khởi động.
- Thiếu khóa GHN: `GhnConfigurationCheck.EnsureConfigured` dừng API khi khởi động.
- Thiếu khóa LLM hoặc embedding: API vẫn chạy. `AiGatewayModule` không tạo provider, nên tính năng AI báo sự cố.
- Thiếu khóa Google: API dùng giá trị giả. Đăng nhập bằng Google không chạy, đăng nhập bằng email vẫn chạy.

Bài học: đọc code để biết cấu hình nào bắt buộc. Không đoán từ tên biến.

### Sửa `render.yaml` theo quyết định

Commit `b540c67` và `b25c041` sửa `render.yaml`:

1. Đổi `plan: starter` thành `plan: free`.
2. Xóa `preDeployCommand`, vì gói free không có pre-deploy.
3. Đổi `GHN_API_BASE` sang gateway sandbox.

Render đọc `render.yaml` từ nhánh `main` khi tạo Blueprint. Vì vậy, các commit này được đẩy lên `main` trước khi tạo Blueprint.

## Phần 3: tạo Blueprint trên Render

### Cấp quyền repo cho Render

Render đọc code qua GitHub App "Render". App này chỉ được cấp quyền cho repo `MemoDeck`, nên danh sách repo không có `Pickory`.

1. Mở trang New Blueprint trên Render.
2. Chọn Configure account ở mục GitHub.
3. Giữ Repository access là Only select repositories.
4. Chọn Select repositories.
5. Chọn `duchuyn04/Pickory`.
6. Chọn Save.

Không chọn All repositories. Render chỉ cần đọc repo cần triển khai. Cấp quyền cho mọi repo mở rộng phạm vi rủi ro khi tài khoản Render bị lộ.

### Tạo Blueprint

1. Chọn Connect ở dòng `duchuyn04/Pickory`.
2. Đặt Blueprint Name là `pickory`.
3. Giữ Branch là `main`.
4. Để trống Blueprint Path. Render dùng `render.yaml` ở gốc repo.
5. Chờ Render đọc `render.yaml`.
6. Nhập giá trị cho các biến có `sync: false`.

Render hiện một ô nhập cho mỗi biến có `sync: false` trong `render.yaml`. Các biến có `value` cố định không hiện trong form.

Giá trị đã nhập:

| Biến | Giá trị |
|---|---|
| `DATABASE_URL` | Chuỗi Session pooler của Supabase, có `?sslmode=require` |
| `WEB_ORIGIN` | `https://pickory.vercel.app`, giá trị tạm |
| `API_BASE_URL` | `https://pickory.vercel.app`, giá trị tạm |
| Ba khóa Cloudinary | Người triển khai tự dán |
| Ba khóa GHN | Người triển khai tự dán |

`WEB_ORIGIN` và `API_BASE_URL` là domain Vercel. Lúc này project Vercel chưa có, nên dùng domain dự kiến. Sửa hai biến này sau khi Vercel cấp domain thật.

### `GHN_WEBHOOK_SECRET` không lấy từ GHN

GHN không cấp giá trị này. Người triển khai tự sinh một chuỗi ngẫu nhiên:

```bash
python -c "import secrets; print(secrets.token_hex(32))"
```

`GhnWebhookEndpoint` so sánh chuỗi này với header `X-Pickory-Webhook-Secret` của mỗi webhook. Thiếu header hoặc sai giá trị thì API trả 401.

Rủi ro chưa kiểm chứng: trang cấu hình webhook của GHN có thể chỉ nhận URL, không nhận header. Khi đó mọi webhook thật bị trả 401. Trạng thái vận đơn chỉ cập nhật qua job đồng bộ 30 phút. Kiểm tra form webhook trên trang GHN khi cấu hình bước 5 của `docs/deployment.md`.

### Kiểm tra form trước khi deploy

Form che giá trị của biến bí mật bằng dấu chấm. Để biết ô nào đã có giá trị mà không đọc khóa, đếm độ dài giá trị của mỗi ô bằng JavaScript trong trình duyệt.

Kết quả: ba ô Cloudinary còn trống. Người triển khai chọn deploy trước, rồi thêm khóa Cloudinary sau. Mục đích là kiểm tra build trên Render sớm.

### Lần deploy đầu

1. Chọn Deploy Blueprint.
2. Mở trang service `pickory-api`.
3. Mở deploy đầu tiên trong tab Events.
4. Đọc log build và log khởi động.

Render cấp domain `https://pickory-api.onrender.com`. Domain này khớp với domain trong `apps/frontend/vercel.json`, nên không cần sửa `vercel.json`.

Kết quả build:

- Bước `pnpm install` trong Docker xong trong 12 giây.
- Bước `dotnet publish` xong trong 31 giây.
- Render đẩy image và cache lên registry trong khoảng 30 giây.

Kết quả khởi động: API dừng với mã thoát 139. Log ghi lỗi sau:

```text
Unhandled exception. System.InvalidOperationException: Production cần CLOUDINARY_CLOUD_NAME, CLOUDINARY_API_KEY và CLOUDINARY_API_SECRET.
   at Pickory.Modules.Catalog.Images.ImageStorageRegistration.EnsureConfigured(...)
```

Lỗi này đúng như dự đoán ở phần 2. Kiểm tra cấu hình lúc khởi động làm API dừng ngay, thay vì chạy rồi lỗi khi người dùng tải ảnh lên.

Bài học: một lần deploy thất bại vẫn cho thông tin. Lần này chứng minh Dockerfile build được trên Render, và chứng minh API đọc đúng biến môi trường.

### Thêm khóa Cloudinary

Render không lưu biến có giá trị rỗng khi tạo Blueprint. Vì vậy, ba biến Cloudinary không có trên trang Environment, và phải thêm từng dòng.

1. Mở tab Environment của service `pickory-api`.
2. Chọn Edit.
3. Chọn Add variable ba lần.
4. Nhập tên `CLOUDINARY_CLOUD_NAME`, `CLOUDINARY_API_KEY` và `CLOUDINARY_API_SECRET` vào ba dòng mới.
5. Dán giá trị từ trang Settings > API Keys của Cloudinary.
6. Chọn Save, rebuild, and deploy.

Ở chế độ Edit, ô giá trị của biến cũ chỉ hiện dấu chấm. Biến cũ không bị xóa khi lưu. Ảnh chụp màn hình xác nhận điều này trước khi lưu.

Trước khi lưu, JavaScript trong trình duyệt kiểm tra hai điều cho mỗi ô mới:

- Ô có giá trị.
- Giá trị không có khoảng trắng ở đầu hoặc cuối. Khoảng trắng thừa khi dán làm khóa sai mà khó phát hiện.

### Lần deploy thứ hai

Render build lại và chạy API. Log kết thúc bằng dòng `Your service is live`.

Kết quả kiểm tra:

| Request | Kết quả |
|---|---|
| `GET /api/health` | 200, `{"data":{"status":"ok"}}`, 0,6 giây |
| `GET /api/categories` | 200 |
| `GET /api/addresses/provinces` | 200, có danh sách tỉnh/thành |
| `GET /c/khong-co` | 404, route SEO đã gắn |

Log khởi động không có dòng `Error` hoặc `Warning`. `AddressSyncWorker` gọi GHN sandbox và nhận HTTP 200. Kết quả này cho biết `GHN_TOKEN` đúng, và GHN không chặn IP của Render.

## Phần 4: tạo project trên Vercel

### Cấp quyền repo cho Vercel

Vercel cũng đọc code qua GitHub App riêng. App này chỉ có quyền với repo `MemoDeck`, giống trường hợp của Render.

1. Mở trang New Project của Vercel.
2. Chọn Adjust GitHub App Permissions.
3. Chọn Select repositories.
4. Tìm `Pickory`.
5. Chọn `duchuyn04/Pickory`.
6. Chọn Save.
7. Tải lại trang New Project.

### Import và deploy

1. Chọn Import ở dòng `Pickory`.
2. Kiểm tra Project Name là `pickory`.
3. Kiểm tra Root Directory là `apps/frontend`.
4. Mở Build and Output Settings.
5. Kiểm tra lệnh build, thư mục kết quả và lệnh cài gói.
6. Chọn Deploy.

Vercel tự tìm `apps/frontend/vercel.json`, nên tự chọn Root Directory là `apps/frontend`. Các ô sau bị khóa với nhãn "Set by vercel.json":

| Ô | Giá trị |
|---|---|
| Application Preset | Vite |
| Build Command | `pnpm build` |
| Output Directory | `dist` |
| Install Command | `pnpm install --frozen-lockfile` |

Cấu hình nằm trong repo thay vì trên Dashboard. Người khác đọc repo là biết Vercel build thế nào.

Web không cần biến môi trường. Web gọi `/api` cùng origin, và Vercel chuyển tiếp request sang Render.

### Kết quả

Vercel cấp domain production `https://pickory.vercel.app`. Domain này khớp với `WEB_ORIGIN` và `API_BASE_URL` đã đặt trên Render, nên không cần sửa biến.

Kiểm tra bằng `curl`:

| Request qua `pickory.vercel.app` | Kết quả |
|---|---|
| `GET /api/health` | 200, 0,14 giây |
| `GET /api/categories` | 200, danh sách rỗng |
| `GET /gio-hang` | 200, Vercel trả `index.html` của SPA |
| `GET /c/khong-co` | 404 từ Render, route SEO đi qua rewrite |

Trang chủ và `/c/khong-co` cùng tải `/assets/index-C5rLhftp.js`. Vercel build trang chủ, còn Dockerfile trên Render build `index.html` cho route SEO. Hai bản build từ cùng commit cho cùng hash. Kết quả này xác nhận cách giữ `index.html` khớp với asset.

Kiểm tra request ghi qua Vercel, với `POST /api/auth/sessions` và mật khẩu sai:

| Header `Origin` | Kết quả |
|---|---|
| `https://pickory.vercel.app` | 401 `INVALID_CREDENTIALS` |
| `https://evil.example` | 403 `FORBIDDEN_ORIGIN` |

Với `Origin` đúng, API trả 401 vì sai mật khẩu. Kết quả này cho biết `RequestOriginGuard` cho request đi qua. Với `Origin` lạ, `RequestOriginGuard` chặn request trước khi API kiểm tra mật khẩu.

Response có cookie `pk_anon` với các thuộc tính `secure`, `samesite=lax` và `httponly`. Cookie không có thuộc tính `Domain`, nên trình duyệt gắn cookie vào `pickory.vercel.app`. Cookie là cookie bên thứ nhất, đúng như thiết kế ở `docs/deployment.md`.

Trang chủ hiện header và không có lỗi trong console. Phần nội dung trống, vì DB production chưa có danh mục và sản phẩm.

## Phần 5: mở tài liệu API chỉ xem

### Yêu cầu và quyết định cũ

Yêu cầu: người khác xem được tài liệu Swagger trên production, nhưng chỉ xem.

Trước thay đổi này, tài liệu chỉ có ở Development. `Program.cs` ghi lý do: tài liệu liệt kê cả route admin. Test `Documentation_InProduction_Returns404` giữ quyết định đó. Vì vậy, thay đổi cần một cờ bật rõ ràng, và mặc định vẫn tắt.

### Cách làm

Swagger UI có tùy chọn `SupportedSubmitMethods`. Danh sách rỗng làm trang ẩn nút "Try it out" và nút "Execute". Người xem vẫn đọc được route, tham số và schema.

| Môi trường | `API_DOCS_ENABLED` | Kết quả |
|---|---|---|
| Development | Bất kỳ | Có tài liệu, có "Try it out" |
| Production | Không đặt hoặc `0` | `/api/docs` trả 404 |
| Production | `1` | Có tài liệu, chỉ xem |
| Production | `1` kèm `API_DOCS_INTERACTIVE=1` | Có tài liệu, có "Try it out" |

Các thay đổi trong code:

- `AppConfig` đọc `API_DOCS_ENABLED` bằng `ReadZeroOrOneFlag`. Hàm này chỉ nhận `0` hoặc `1`, giống `GHN_LIVE_TESTS`.
- `ApiDocumentation.SelectMode` chọn chế độ `Interactive`, `ReadOnly` hoặc không gắn tài liệu.
- `ApiDocumentation.Map` đặt `SupportedSubmitMethods` rỗng ở chế độ `ReadOnly`.
- `API_DOCS_INTERACTIVE` cũng chỉ nhận `0` hoặc `1`. Biến này không có tác dụng khi `API_DOCS_ENABLED` tắt.
- `render.yaml` đặt `API_DOCS_ENABLED` và `API_DOCS_INTERACTIVE` bằng `"1"`.

Chế độ chỉ xem là giới hạn của giao diện, không phải lớp bảo mật. Người biết route vẫn gọi API bằng `curl`. RBAC (Role-Based Access Control) vẫn chặn route admin khi người gọi không có quyền.

### Kiểm tra trước khi deploy

| Bộ test | Kết quả |
|---|---|
| Unit test | 943 test thành công |
| Integration test | 1091 thành công, 10 bỏ qua (test gọi dịch vụ thật) |
| Architecture test | 6 thành công |

Integration test kiểm tra `index.js` của Swagger UI. Production có cờ chứa `"supportedSubmitMethods":[]`. Development không chứa chuỗi này.

Integration test chạy trên DB riêng `pickory_test_docs`. DB test của nhánh Commerce có schema khác, nên dùng chung sẽ làm fixture từ chối reset DB. Lần chạy đầu đặt `DATABASE_URL` trùng với `TEST_DATABASE_URL`. Fixture từ chối chạy với lỗi "resolves to the DATABASE_URL development database". Lớp bảo vệ này chặn test xóa nhầm DB dev.

### Kết quả trên production

Render đồng bộ `render.yaml` sau khi `main` nhận commit `95c0296`. Render thêm biến `API_DOCS_ENABLED`, rồi build lại. Tài liệu xuất hiện sau khoảng 2 phút.

| Kiểm tra | Kết quả |
|---|---|
| `GET /api/openapi/v1.json` | 200, tiêu đề `Pickory API`, 44 đường dẫn, `servers` là `/` |
| `GET /api/docs` | 301, `Location: docs/index.html` |
| `GET /api/docs/index.html` | 200 |
| `index.js` | `"supportedSubmitMethods":[]` |
| Trang Swagger trong trình duyệt | 58 thao tác, 0 nút "Try it out", 0 nút "Execute" |

`Location` là đường dẫn tương đối. Trình duyệt ở lại domain `pickory.vercel.app`, không chuyển sang domain Render.

## Trạng thái hiện tại

| Phần | Trạng thái |
|---|---|
| Supabase | Đã tạo project `pickory`, đã có schema, đã thu hồi quyền của `anon` |
| Render | Service `pickory-api` đang chạy tại `https://pickory-api.onrender.com` |
| Vercel | Web đang chạy tại `https://pickory.vercel.app` |
| Tài liệu API | Chỉ xem tại `https://pickory.vercel.app/api/docs` |
| Dữ liệu | Chưa có tài khoản admin, danh mục và sản phẩm |
