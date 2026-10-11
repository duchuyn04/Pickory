# Image chạy API trên Render. Build từ gốc repo vì image cần cả mã backend và bản build của web.
# Lệnh build ở máy: docker build -f apps/backend/Dockerfile -t pickory-api .

# Giai đoạn 1: build web để lấy `index.html`. API chèn thẻ meta SEO vào file này cho `/p/{slug}` và `/c/{slug}`.
FROM node:22-bookworm-slim AS web-build
# Corepack cài đúng bản pnpm ghi trong `packageManager`. Bản pnpm khác có thể đổi cách giải lockfile.
RUN corepack enable
WORKDIR /src/apps/frontend
# Chép manifest trước mã nguồn. Docker giữ cache bước cài gói khi chỉ mã nguồn thay đổi.
COPY apps/frontend/package.json apps/frontend/pnpm-lock.yaml ./
# `--frozen-lockfile` dừng build khi lockfile lệch. Vercel cũng cài theo lockfile này, nên hai bản build dùng cùng phiên bản gói.
RUN pnpm install --frozen-lockfile
COPY apps/frontend/ ./
# Tên file asset chứa hash nội dung. Cùng mã nguồn và cùng lockfile thì hash khớp với asset Vercel phục vụ.
RUN pnpm build

# Giai đoạn 2: build API bằng SDK .NET 10, đúng `TargetFramework` trong `Directory.Build.props`.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src/apps/backend
# `Directory.Build.props` và `Directory.Packages.props` giữ framework và phiên bản gói. Thiếu hai file này thì build sai cấu hình.
COPY apps/backend/Directory.Build.props apps/backend/Directory.Packages.props ./
COPY apps/backend/src/ ./src/
# Chỉ publish project API. Project test không thuộc image chạy thật.
RUN dotnet publish src/Pickory.Api/Pickory.Api.csproj -c Release -o /app/publish

# Giai đoạn 3: image chạy chỉ có runtime ASP.NET. Image không chứa SDK và mã nguồn.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Npgsql thử mã hóa GSS khi mở kết nối. Thiếu thư viện này thì mỗi lần chạy in lỗi nạp thư viện ra log.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api-build /app/publish ./
# API chỉ đọc `index.html`. Vercel phục vụ JS, CSS và ảnh của web.
COPY --from=web-build /src/apps/frontend/dist/index.html ./web/index.html

# `Production` bật các kiểm tra khóa bắt buộc và cookie `Secure`.
ENV ASPNETCORE_ENVIRONMENT=Production
# Đường dẫn trỏ tới file đã chép ở trên. Đặt ở image để Render không cần biến này.
ENV WEB_INDEX_HTML_PATH=/app/web/index.html
# Container nhận cấu hình từ biến môi trường của Render. File `.env` không có trong image.
ENV PICKORY_SKIP_DOTENV=true
# Dự án chưa có dịch vụ gửi thư thật. Người dùng không ghi được vào `/app`, nên thư ghi vào `/tmp`.
ENV DEV_OUTBOX_DIR=/tmp/pickory-outbox

# Chạy bằng người dùng không có quyền root. Lỗi trong API không chiếm được quyền root của container.
USER $APP_UID
# API đọc biến `PORT` do Render đặt, nên image không cố định cổng.
# Dùng `CMD` thay `ENTRYPOINT`. Lệnh pre-deploy của Render thay toàn bộ `CMD`, nên lệnh `db:migrate` không bị ghép vào lệnh chạy API.
CMD ["dotnet", "Pickory.Api.dll"]
