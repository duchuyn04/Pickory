#!/usr/bin/env bash
# Triển khai Pickory lên VPS: gửi mã nguồn, build image, chạy migration, khởi động lại.
# Chạy từ gốc repo: bash scripts/deploy-vps.sh
set -euo pipefail

# Địa chỉ VPS và khóa SSH có thể đổi bằng biến môi trường khi cần.
VPS_HOST="${VPS_HOST:-root@187.77.134.168}"
SSH_KEY="${SSH_KEY:-$HOME/.ssh/id_ed25519_vps}"
REMOTE_DIR="/opt/pickory"
COMPOSE="docker compose -f docker-compose.prod.yml"

# Dừng sớm nếu chạy sai thư mục. Lệnh tar bên dưới cần đúng đường dẫn tương đối.
if [ ! -f docker-compose.prod.yml ]; then
  echo "Chạy script này từ gốc repo Pickory." >&2
  exit 1
fi

# Gửi mã nguồn qua đường ống SSH, không cần file tạm. Các loại trừ giữ secret và file build ở lại máy dev.
# Lệnh `rm -rf` xóa mã cũ trước khi giải nén, nên file đã xóa trong repo cũng biến mất trên VPS.
# File `.env` và thư mục `backups` nằm ngoài `apps` và `docker`, nên không bị xóa.
# Thư mục `apps/frontend/e2e` phải đi theo. Lệnh `pnpm build` chạy `tsc` trên cả `test`, và một unit test import file trong `e2e`.
tar czf - \
  --exclude='node_modules' --exclude='bin' --exclude='obj' --exclude='dist' \
  --exclude='playwright-report' --exclude='test-results' --exclude='coverage' \
  --exclude='.dev-outbox' --exclude='.env' --exclude='.env.*' \
  --exclude='apps/backend/tests' \
  .dockerignore docker docker-compose.prod.yml apps/backend apps/frontend \
  | ssh -i "$SSH_KEY" "$VPS_HOST" "cd $REMOTE_DIR && rm -rf apps docker && tar xzf -"

# Build image trước khi đụng container đang chạy. Build lỗi thì site cũ vẫn chạy bình thường.
ssh -i "$SSH_KEY" "$VPS_HOST" "cd $REMOTE_DIR && $COMPOSE build"

# Chạy migration bằng image mới trước khi đổi API. Migration lỗi thì script dừng và API cũ vẫn chạy.
ssh -i "$SSH_KEY" "$VPS_HOST" "cd $REMOTE_DIR && $COMPOSE run --rm api dotnet Pickory.Api.dll db:migrate"

# Tạo lại container với image mới. Caddy giữ chứng chỉ HTTPS trong volume nên không xin lại.
ssh -i "$SSH_KEY" "$VPS_HOST" "cd $REMOTE_DIR && $COMPOSE up -d && docker image prune -f >/dev/null"

# Kiểm tra API trả "ok" sau khi khởi động. Chờ vài giây vì API cần thời gian nạp cấu hình.
sleep 15
curl -fsS https://pickory.tech/api/health
echo
echo "Triển khai xong."
