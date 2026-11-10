# Image web cho VPS. Caddy phục vụ file build của React Vite và chuyển tiếp route API.
# Build từ gốc repo: docker build -f docker/web.Dockerfile -t pickory-web .

# Giai đoạn 1: build web. Dùng cùng Node, pnpm và lockfile với `apps/backend/Dockerfile`.
FROM node:22-bookworm-slim AS web-build
# Corepack cài đúng bản pnpm ghi trong `packageManager`. Bản pnpm khác có thể đổi cách giải lockfile.
RUN corepack enable
WORKDIR /src/apps/frontend
# Chép manifest trước mã nguồn. Docker giữ cache bước cài gói khi chỉ mã nguồn đổi.
COPY apps/frontend/package.json apps/frontend/pnpm-lock.yaml ./
# `--frozen-lockfile` dừng build khi lockfile lệch. Hash asset nhờ đó khớp với `index.html` trong image API.
RUN pnpm install --frozen-lockfile
COPY apps/frontend/ ./
RUN pnpm build

# Giai đoạn 2: Caddy phục vụ thư mục `dist` và tự xin chứng chỉ HTTPS.
FROM caddy:2-alpine
# Caddyfile được gắn từ compose. Thư mục `/srv` chỉ chứa file tĩnh của web.
COPY --from=web-build /src/apps/frontend/dist /srv
