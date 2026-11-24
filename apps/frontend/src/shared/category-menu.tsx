import { useEffect, useRef, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useCategoryTree } from '../features/home/home-queries';
import { ErrorAlert } from './error-alert';
import { Icon } from './icons';

const PANEL_ID = 'site-category-menu-panel';

// Nút "Danh mục" trên thanh đầu trang (màn rộng). Bấm mở bảng liệt kê danh mục cha và danh mục con.
export function CategoryMenu() {
  const { pathname } = useLocation();
  const { data: categories, isPending, isError } = useCategoryTree();
  const [isOpen, setIsOpen] = useState(false);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);

  // Đóng bảng khi khách chuyển trang, để bảng không che nội dung trang mới.
  useEffect(() => {
    setIsOpen(false);
  }, [pathname]);

  useEffect(() => {
    // Bảng đóng thì dừng lắng nghe sự kiện, tránh chạy kiểm tra mỗi lần bấm trên trang.
    if (!isOpen) return;

    function handlePointerDown(event: MouseEvent) {
      // Bấm ra ngoài vùng nút và bảng thì đóng, giống menu thả xuống thông thường.
      if (!wrapperRef.current?.contains(event.target as Node)) setIsOpen(false);
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape') return;
      setIsOpen(false);
      // Trả focus về nút để người dùng bàn phím không mất vị trí.
      toggleRef.current?.focus();
    }

    document.addEventListener('mousedown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('mousedown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen]);

  return (
    <div className="category-menu" ref={wrapperRef}>
      <button
        ref={toggleRef}
        type="button"
        className="site-action category-menu-toggle"
        aria-expanded={isOpen}
        aria-controls={PANEL_ID}
        onClick={() => setIsOpen((current) => !current)}
      >
        <Icon name="dashboard" />
        <span>Danh mục</span>
        <Icon name="chevronDown" />
      </button>

      {isOpen && (
        <div id={PANEL_ID} className="category-menu-panel">
          {isPending && <p role="status">Đang tải danh mục...</p>}
          {/* Lỗi tải chỉ hiện trong bảng, nên phần còn lại của thanh đầu trang vẫn dùng được. */}
          {isError && <ErrorAlert message="Không tải được danh mục. Thử lại sau ít phút." />}
          {categories && categories.length === 0 && <p>Chưa có danh mục nào.</p>}
          {categories && categories.length > 0 && (
            <ul className="category-menu-groups">
              {categories.map((category) => (
                <li key={category.id} className="category-menu-group">
                  <Link
                    to={`/c/${category.slug}`}
                    className="category-menu-parent"
                    onClick={() => setIsOpen(false)}
                  >
                    {category.name}
                  </Link>
                  {category.children.length > 0 && (
                    <ul>
                      {category.children.map((child) => (
                        <li key={child.id}>
                          <Link to={`/c/${child.slug}`} onClick={() => setIsOpen(false)}>
                            {child.name}
                          </Link>
                        </li>
                      ))}
                    </ul>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
