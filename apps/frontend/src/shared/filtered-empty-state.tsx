interface FilteredEmptyStateProps {
  page: number;
  // Đưa màn về trang 1. Màn chịu trách nhiệm xóa tham số `page` khỏi URL.
  onFirstPage: () => void;
}

// Danh sách rỗng khi có bộ lọc hoặc đang ở trang sau. Nói rõ nguyên nhân là bộ lọc, không phải hệ thống chưa có dữ liệu.
export function FilteredEmptyState({ page, onFirstPage }: FilteredEmptyStateProps) {
  return (
    <div className="empty-state">
      <p role="status">Không có kết quả phù hợp với bộ lọc.</p>
      {/* Link `page=99` vượt quá số trang. Nút đưa người dùng về trang có dữ liệu. */}
      {page > 1 && (
        <button type="button" className="btn btn-sm" onClick={onFirstPage}>
          Về trang 1
        </button>
      )}
    </div>
  );
}
