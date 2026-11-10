import type { ReactNode } from 'react';
import { BrandMark } from '../../../shared/brand-mark';
import { APP_NAME } from '../../../shared/app-name';
import './auth-split-page.css';

// Nội dung bảng thương hiệu của từng màn đăng nhập: khách dùng nền đỏ, quản trị dùng nền tối.
interface BrandPanelContent {
  headline: string;
  description: string;
  benefits: string[];
}

const brandPanels = {
  customer: {
    headline: 'Mua sắm điện tử dễ dàng hơn với tài khoản Pickory',
    description:
      'Theo dõi đơn hàng, lưu thông tin giao nhận và nhận ưu đãi dành riêng cho thành viên.',
    benefits: [
      'Theo dõi trạng thái đơn hàng',
      'Lưu địa chỉ cho lần mua sau',
      'Nhận ưu đãi dành cho thành viên',
    ],
  },
  admin: {
    headline: 'Pickory Back-Office',
    description: 'Quản lý đơn hàng, sản phẩm và chăm sóc khách hàng trong một nơi.',
    benefits: [
      'Đơn hàng và thanh toán',
      'Sản phẩm, danh mục và hãng',
      'Chăm sóc khách hàng',
      'Báo cáo doanh thu',
    ],
  },
} satisfies Record<'customer' | 'admin', BrandPanelContent>;

export type AuthSplitVariant = keyof typeof brandPanels;

// Vòng tròn mờ kèm dấu tích của một mục lợi ích. Hình này chỉ để trang trí nên ẩn khỏi trình đọc màn hình.
function CheckCircle() {
  return (
    <svg
      className="auth-split-check"
      width="24"
      height="24"
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
      focusable="false"
    >
      <circle cx="12" cy="12" r="12" fill="currentColor" fillOpacity="0.2" />
      <path
        d="M7 12.5L10.2 15.7L17 9"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

interface AuthSplitPageProps {
  variant: AuthSplitVariant;
  title: string;
  subtitle: string;
  children: ReactNode;
}

// Màn xác thực chia đôi: bảng thương hiệu bên trái, biểu mẫu bên phải. Dưới 1024px bảng thu thành thanh logo.
export function AuthSplitPage({ variant, title, subtitle, children }: AuthSplitPageProps) {
  const panel = brandPanels[variant];

  return (
    <main className={`auth-split auth-split-${variant}`}>
      <aside className="auth-split-brand">
        <div className="auth-split-logo">
          <span className="auth-split-logo-box">
            <BrandMark className="auth-split-logo-mark" />
          </span>
          <span className="auth-split-logo-name">{APP_NAME}</span>
        </div>

        {/* Phần giới thiệu chỉ hợp màn rộng. Màn hẹp bỏ nó để biểu mẫu lên ngay đầu trang. */}
        <div className="auth-split-intro">
          <p className="auth-split-headline">{panel.headline}</p>
          <p className="auth-split-description">{panel.description}</p>
          <ul className="auth-split-benefits">
            {panel.benefits.map((benefit) => (
              <li key={benefit}>
                <CheckCircle />
                <span>{benefit}</span>
              </li>
            ))}
          </ul>
        </div>

        <p className="auth-split-copyright">© 2026 {APP_NAME}</p>
      </aside>

      <section className="auth-split-main">
        <div className="auth-split-content">
          <header className="auth-split-heading">
            <h1>{title}</h1>
            <p>{subtitle}</p>
          </header>
          {children}
        </div>
      </section>
    </main>
  );
}
