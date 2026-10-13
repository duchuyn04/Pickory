interface PagerProps {
  page: number;
  total: number;
  limit: number;
  onPageChange: (page: number) => void;
  // Tên truy cập của `nav`, ví dụ "Phân trang nhân viên". Mỗi màn có một tên riêng.
  label: string;
}

export function Pager({ page, total, limit, onPageChange, label }: PagerProps) {
  const pageCount = Math.ceil(total / limit);
  // Một trang trở xuống thì không cần điều hướng. Ẩn cả thanh để giao diện gọn.
  if (pageCount <= 1) return null;

  return (
    <nav className="pager" aria-label={label}>
      <button
        type="button"
        className="btn btn-sm"
        // Trang 1 không có trang trước. Nút tắt để không gọi trang 0.
        disabled={page <= 1}
        onClick={() => onPageChange(page - 1)}
      >
        Trang trước
      </button>
      <span>
        Trang {page} / {pageCount}
      </span>
      <button
        type="button"
        className="btn btn-sm"
        // Trang cuối không có trang sau. Nút tắt để không gọi trang vượt quá tổng.
        disabled={page >= pageCount}
        onClick={() => onPageChange(page + 1)}
      >
        Trang sau
      </button>
    </nav>
  );
}
