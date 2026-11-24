import { Link } from 'react-router-dom';
import { Icon, type IconName } from '../../shared/icons';
import type { CategoryNode } from '../catalog/customer/catalog-api';
import { BannerCarousel } from './banner-carousel';
import { HOME_BANNERS } from './home-banners';
import { getCategoryVisual, HERO_CATEGORY_LIMIT } from './home-content';
import { HomeSidebar } from './home-sidebar';
import { SmallBanners } from './small-banners';
import './home-hero.css';

interface UtilityMenuItem {
  label: string;
  to: string;
  icon: IconName;
}

// Ba lối tắt hỗ trợ khách luôn giữ cố định dưới nhóm danh mục chính.
const MENU_UTILITY_LINKS: UtilityMenuItem[] = [
  { label: 'Tra cứu đơn hàng', to: '/order-lookup', icon: 'package' },
  { label: 'Phí giao hàng', to: '/policy/giao-hang', icon: 'truck' },
  { label: 'So sánh sản phẩm', to: '/compare', icon: 'arrowLeftRight' },
];

interface HomeHeroProps {
  categories?: CategoryNode[];
  isCategoriesPending: boolean;
  isCategoriesError: boolean;
}

// Khối đầu trang chủ gồm menu danh mục bên trái, banner giữa và cột phải khám phá nhanh.
export function HomeHero({ categories, isCategoriesPending, isCategoriesError }: HomeHeroProps) {
  const heroCategories = (categories ?? []).slice(0, HERO_CATEGORY_LIMIT);
  const firstCategorySlug = heroCategories[0]?.slug;

  return (
    <div className="home-hero">
      {!isCategoriesError && (
        <HeroCategoryMenu categories={heroCategories} isPending={isCategoriesPending} />
      )}
      <div className="home-hero-main">
        <BannerCarousel banners={HOME_BANNERS} />
        <SmallBanners
          categories={categories}
          isPending={isCategoriesPending && !isCategoriesError}
        />
      </div>
      <HomeSidebar
        firstCategorySlug={firstCategorySlug}
        isCategoriesPending={isCategoriesPending && !isCategoriesError}
      />
    </div>
  );
}

interface HeroCategoryMenuProps {
  categories: CategoryNode[];
  isPending: boolean;
}

function HeroCategoryMenu({ categories, isPending }: HeroCategoryMenuProps) {
  if (isPending) {
    return <aside className="hero-sidebar hero-sidebar-skeleton" aria-hidden="true" />;
  }

  const firstCategory = categories[0];

  return (
    <aside className="hero-sidebar">
      <nav className="hero-category-nav" aria-label="Danh mục sản phẩm">
        {categories.length > 0 && (
          <ul className="hero-menu-list">
            {categories.map((category) => {
              const visual = getCategoryVisual(category.slug);
              return (
                <li key={category.id}>
                  <Link to={`/c/${category.slug}`} className="hero-menu-link">
                    <span className="hero-menu-icon">
                      <Icon name={visual.icon} />
                    </span>
                    <span className="hero-menu-label">{category.name}</span>
                    <Icon name="chevronRight" className="icon hero-menu-chevron" />
                  </Link>
                </li>
              );
            })}
          </ul>
        )}

        {categories.length > 0 && <hr className="hero-menu-divider" />}

        <ul className="hero-menu-list">
          {MENU_UTILITY_LINKS.map((item) => (
            <li key={item.to}>
              <Link to={item.to} className="hero-menu-link">
                <span className="hero-menu-icon">
                  <Icon name={item.icon} />
                </span>
                <span className="hero-menu-label">{item.label}</span>
                <Icon name="chevronRight" className="icon hero-menu-chevron" />
              </Link>
            </li>
          ))}
        </ul>

        {/* Chỉ hiện liên kết xem tất cả khi có danh mục đầu tiên hợp lệ để tránh trỏ tới đường dẫn rỗng. */}
        {firstCategory && (
          <Link to={`/c/${firstCategory.slug}`} className="hero-menu-all">
            <span>Xem tất cả sản phẩm</span>
            <Icon name="chevronRight" className="icon hero-menu-all-icon" />
          </Link>
        )}
      </nav>
    </aside>
  );
}
