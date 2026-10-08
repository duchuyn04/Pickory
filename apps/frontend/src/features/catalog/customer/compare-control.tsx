import { COMPARE_LIST_LIMIT, describeAddState } from './compare-list';
import { useCompareList } from './use-compare-list';

interface CompareControlProps {
  categoryId: string;
  productId: string;
}

export function CompareControl({ categoryId, productId }: CompareControlProps) {
  const { list, add, remove, startNew } = useCompareList();
  const addState = describeAddState(list, categoryId, productId);
  const isInList = addState === 'in-list';
  // Nút chỉ tắt khi bấm vào cũng không đổi được danh sách. Sản phẩm đã có trong danh sách luôn bỏ được.
  const isBlocked = addState === 'full' || addState === 'category-mismatch';

  function handleToggle() {
    if (isInList) remove(productId);
    else add(categoryId, productId);
  }

  return (
    <div className="compare-control">
      <button
        type="button"
        className="btn compare-button"
        aria-pressed={isInList}
        aria-describedby={isBlocked ? 'compare-note' : undefined}
        disabled={isBlocked}
        onClick={handleToggle}
      >
        {isInList ? 'Bỏ khỏi so sánh' : 'Thêm vào so sánh'}
      </button>
      <p className="muted small compare-count">
        Danh sách so sánh: {list.productIds.length}/{COMPARE_LIST_LIMIT}
      </p>
      {addState === 'full' && (
        <p id="compare-note" className="muted small">
          Đã đủ {COMPARE_LIST_LIMIT} sản phẩm
        </p>
      )}
      {addState === 'category-mismatch' && (
        <div id="compare-note" className="compare-mismatch">
          <p className="muted small">
            Danh sách so sánh đang có sản phẩm của danh mục khác. Chỉ so sánh được các sản phẩm cùng
            danh mục.
          </p>
          <button
            type="button"
            className="btn btn-sm"
            onClick={() => startNew(categoryId, productId)}
          >
            Bắt đầu danh sách mới
          </button>
        </div>
      )}
    </div>
  );
}
