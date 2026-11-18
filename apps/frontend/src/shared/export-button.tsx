import { useRef, useState } from 'react';
import { checkPermission, useActor } from '../features/identity/hooks/use-actor';
import { ApiError } from './api';
import { DOWNLOAD_FAILURE_MESSAGE, downloadFile } from './download-file';
import { ErrorAlert } from './error-alert';

interface ExportButtonProps {
  // Đường dẫn API trả file, ví dụ `/api/admin/exports/orders?status=paid`.
  path: string;
  // Người dùng phải có đủ mọi quyền này thì nút mới hiện. Danh sách rỗng nghĩa là không cần quyền.
  requiredPermissions: readonly string[];
  // Nhãn của nút khi rảnh.
  label?: string;
}

// Nút tải file Excel từ một API. Nút ẩn khi thiếu quyền và hiện lỗi ngay dưới nút khi tải thất bại.
export function ExportButton({
  path,
  requiredPermissions,
  label = 'Xuất Excel',
}: ExportButtonProps) {
  // Gọi hook một lần. Không gọi `useCan` trong vòng lặp vì số lần gọi hook phải cố định giữa các lần vẽ.
  const { data: actor } = useActor();
  const [isDownloading, setIsDownloading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Cờ đọc được ngay trong lúc `setState` chưa kịp cập nhật. Hai lần bấm cùng một khung hình chỉ gửi một request.
  const isDownloadingRef = useRef(false);

  // Nút chỉ hiện khi actor có mọi quyền, nên actor chưa nạp thì nút ẩn. Danh sách quyền rỗng không cần quyền nào nên nút luôn hiện.
  const hasEveryPermission = requiredPermissions.every((permission) =>
    checkPermission(actor, permission),
  );
  if (!hasEveryPermission) return null;

  async function handleClick() {
    // Lần tải trước chưa xong thì bỏ qua lần bấm này, để người dùng không tạo hai file cùng lúc.
    if (isDownloadingRef.current) return;
    isDownloadingRef.current = true;

    // Xóa lỗi của lần trước. Người dùng không nhầm lỗi cũ với kết quả của lần tải mới.
    setErrorMessage(null);
    setIsDownloading(true);

    try {
      // Gọi API tải file. Mọi lỗi của lần gọi này phải rơi vào `catch` bên dưới.
      await downloadFile(path);
    } catch (error) {
      // Chỉ hiện câu của `ApiError`. Lỗi lạ có thể chứa chi tiết kỹ thuật nên thay bằng câu chung.
      setErrorMessage(error instanceof ApiError ? error.message : DOWNLOAD_FAILURE_MESSAGE);
    } finally {
      // Mở lại nút cả khi thành công và khi lỗi. Nếu không, một lỗi sẽ khóa nút vĩnh viễn.
      isDownloadingRef.current = false;
      setIsDownloading(false);
    }
  }

  return (
    <div>
      <button
        type="button"
        className="btn btn-outline"
        disabled={isDownloading}
        aria-busy={isDownloading ? 'true' : undefined}
        onClick={handleClick}
      >
        {isDownloading ? 'Đang tạo file...' : label}
      </button>
      {errorMessage && <ErrorAlert message={errorMessage} />}
    </div>
  );
}
