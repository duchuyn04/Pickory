import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { ApiError, apiRequest } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import type { ProductDetail } from './catalog-api';
import { ProductView } from './product-view';
import './category-page.css';
import './product-page.css';

export function ProductPage() {
  const { slug } = useParams();
  // Route luôn có `:slug`. Kiểm ở đây để kiểu dữ liệu của `slug` là chuỗi trong phần còn lại.
  if (!slug) return null;
  // Đổi sản phẩm thì dựng lại trang, để lựa chọn của sản phẩm cũ không rò sang sản phẩm mới.
  return <ProductContent key={slug} slug={slug} />;
}

function ProductNotFound() {
  return (
    <main className="catalog-page">
      <div className="card catalog-not-found">
        <h1>Không tìm thấy sản phẩm</h1>
        <p className="muted">Sản phẩm này không tồn tại hoặc đã ngừng hiển thị.</p>
        <Link to="/" className="btn btn-primary">
          Về trang chủ
        </Link>
      </div>
    </main>
  );
}

// Khung giữ chỗ cùng bố cục với trang thật, để nội dung không nhảy mạnh khi dữ liệu về.
function ProductSkeleton() {
  return (
    <main className="catalog-page product-page" aria-busy="true">
      <p role="status" className="sr">
        Đang tải sản phẩm...
      </p>
      <div className="product-skeleton-title" />
      <div className="product-grid" aria-hidden="true">
        <div className="gallery gallery-skeleton" />
        <div className="product-buy product-buy-skeleton" />
      </div>
    </main>
  );
}

function ProductContent({ slug }: { slug: string }) {
  const navigate = useNavigate();
  const location = useLocation();
  const product = useQuery<ProductDetail, ApiError>({
    queryKey: ['product', slug],
    // Slug cũ được API trả 301. `fetch` tự đi theo và trả chi tiết với slug mới.
    queryFn: () => apiRequest(`/api/products/${encodeURIComponent(slug)}`),
  });
  const currentSlug = product.data?.slug;

  useEffect(() => {
    if (!currentSlug || currentSlug === slug) return;
    // Thay URL slug cũ bằng slug mới và giữ query string, để link chia sẻ và `?sku=` không mất.
    navigate(`/p/${currentSlug}${location.search}`, { replace: true });
  }, [currentSlug, slug, location.search, navigate]);

  if (product.isError && product.error.code === 'PRODUCT_NOT_FOUND') {
    return <ProductNotFound />;
  }
  if (product.isError) {
    return (
      <main className="catalog-page">
        <ErrorAlert message={product.error.message} className="catalog-alert" />
      </main>
    );
  }
  // Đang chuyển sang slug mới thì giữ khung tải, để khách không thấy trang nhấp nháy.
  if (!product.data || product.data.slug !== slug) return <ProductSkeleton />;

  return <ProductView product={product.data} />;
}
