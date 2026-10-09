interface ForbiddenNoticeProps {
  permission: string;
  subject: string;
}

// Thông báo thiếu quyền dùng chung cho các màn quản trị. Backend vẫn tự chặn mọi request bằng kiểm tra quyền.
export function ForbiddenNotice({ permission, subject }: ForbiddenNoticeProps) {
  return (
    <div className="card" role="alert">
      <h2 className="forbidden-title">403 - Không có quyền truy cập</h2>
      <p className="muted">
        Bạn cần quyền <code>{permission}</code> để xem {subject}.
      </p>
    </div>
  );
}
