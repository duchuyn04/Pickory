import { APP_NAME } from '../../shared/app-name';
import { ErrorAlert } from '../../shared/error-alert';
import { CatalogBrowser } from './catalog-browser';
import { CategoryBlocks } from './category-blocks';
import { DeliveryNotice } from './delivery-notice';
import { FeaturedBrands } from './featured-brands';
import { HomeHero } from './home-hero';
import { useCategoryTree } from './home-queries';
import { NewArrivals } from './new-arrivals';
import './home-page.css';

// Trang chủ theo thứ tự mới: khối đầu, dải giao hàng, hàng mới về, năm khối danh mục, thương hiệu và toàn bộ sản phẩm.
export function HomePage() {
  const categoriesQuery = useCategoryTree();
  const categories = categoriesQuery.data;
  const hasCategories = Boolean(categories && categories.length > 0);
  const firstCategorySlug = categories?.[0]?.slug;

  return (
    <main className="home-page">
      <h1 className="sr">{APP_NAME}: điện thoại, laptop, màn hình, tai nghe và phụ kiện</h1>

      <div className="home-container">
        <HomeHero
          categories={categories}
          isCategoriesPending={categoriesQuery.isPending}
          isCategoriesError={categoriesQuery.isError}
        />
        <DeliveryNotice />

        {categoriesQuery.isError && (
          <div className="home-error">
            <ErrorAlert message="Không tải được danh mục sản phẩm. Kiểm tra kết nối rồi thử lại." />
            <button type="button" className="btn" onClick={() => void categoriesQuery.refetch()}>
              Thử lại
            </button>
          </div>
        )}

        {hasCategories && categories && (
          <>
            <NewArrivals categories={categories} />
            <CategoryBlocks categories={categories} />
          </>
        )}

        <FeaturedBrands
          firstCategorySlug={firstCategorySlug}
          isCategoriesPending={categoriesQuery.isPending}
          isCategoriesError={categoriesQuery.isError}
        />
        <CatalogBrowser />
      </div>
    </main>
  );
}
