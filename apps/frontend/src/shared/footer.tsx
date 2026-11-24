import { Link } from 'react-router-dom';
import { useCategoryTree } from '../features/home/home-queries';
import { APP_NAME } from './app-name';
import './footer.css';

interface FooterLinkItem {
  label: string;
  to: string;
}

// Thiết kế chỉ hiển thị năm danh mục gốc ở chân trang để giữ chiều cao các cột cân đối.
const FOOTER_CATEGORY_LIMIT = 5;

const SUPPORT_LINKS: FooterLinkItem[] = [
  { label: 'Tra cứu đơn hàng', to: '/order-lookup' },
  { label: 'Phí giao hàng', to: '/policy/giao-hang' },
  { label: 'Liên hệ CSKH', to: '/support' },
];

// Trang chính sách bảo hành và điều khoản sử dụng chưa có route riêng, nên trỏ tạm về `/privacy`.
const POLICY_LINKS: FooterLinkItem[] = [
  { label: 'Quyền riêng tư', to: '/privacy' },
  { label: 'Chính sách bảo hành', to: '/privacy' },
  { label: 'Điều khoản sử dụng', to: '/privacy' },
];

function FooterLinkList({ ariaLabel, links }: { ariaLabel: string; links: FooterLinkItem[] }) {
  return (
    <nav aria-label={ariaLabel}>
      <ul className="site-footer-list">
        {links.map((link) => (
          <li key={link.label}>
            <Link to={link.to}>{link.label}</Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}

// Chân trang dùng chung của khách gồm bốn cột thông tin và thanh bản quyền ở đáy.
export function Footer() {
  // Dùng chung hook danh mục với thanh đầu trang để tận dụng cache và tránh gọi API trùng lặp.
  const { data: categories, isPending, isError } = useCategoryTree();
  // Kiểm tra dạng mảng trước khi cắt để chân trang không sập khi phản hồi API sai cấu trúc.
  const rootCategories = Array.isArray(categories)
    ? categories.slice(0, FOOTER_CATEGORY_LIMIT)
    : [];

  return (
    <footer className="site-footer">
      <div className="site-footer-inner">
        <div className="site-footer-columns">
          <section className="site-footer-column">
            <h2 className="site-footer-heading">{APP_NAME}</h2>
            <p className="site-footer-description">
              Điện thoại, laptop, màn hình, phụ kiện và tai nghe chính hãng. Bảo hành và phí giao
              hàng ghi rõ trước khi đặt.
            </p>
          </section>

          <section className="site-footer-column">
            <h2 className="site-footer-heading">Hỗ trợ khách hàng</h2>
            <FooterLinkList ariaLabel="Hỗ trợ khách hàng" links={SUPPORT_LINKS} />
          </section>

          <section className="site-footer-column">
            <h2 className="site-footer-heading">Chính sách</h2>
            <FooterLinkList ariaLabel="Chính sách" links={POLICY_LINKS} />
          </section>

          <section className="site-footer-column">
            <h2 className="site-footer-heading">Danh mục</h2>
            {/* Giữ nguyên cột với thông báo ngắn khi đang tải hoặc lỗi để không chặn phần còn lại của chân trang. */}
            {isPending && (
              <p className="site-footer-status" role="status">
                Đang tải danh mục...
              </p>
            )}
            {isError && <p className="site-footer-status">Chưa tải được danh mục.</p>}
            {!isPending && !isError && rootCategories.length === 0 && (
              <p className="site-footer-status">Chưa có danh mục.</p>
            )}
            {!isPending && !isError && rootCategories.length > 0 && (
              <FooterLinkList
                ariaLabel="Danh mục"
                links={rootCategories.map((category) => ({
                  label: category.name,
                  to: `/c/${category.slug}`,
                }))}
              />
            )}
          </section>
        </div>

        <div className="site-footer-bottom">
          <span className="site-footer-copy">© 2026 {APP_NAME}</span>
          <span className="site-footer-warranty">Bảo hành ghi rõ trên từng sản phẩm</span>
        </div>
      </div>
    </footer>
  );
}
