import { useState } from 'react';
import { Icon } from '../../../shared/icons';
import { buildCloudinarySrcSet, withCloudinaryTransform } from '../shared/image-url';
import type { ProductImageInfo } from './catalog-api';

// Ảnh chính chiếm tối đa nửa bề rộng bố cục 1200px ở màn rộng và cả bề rộng ở điện thoại.
const MAIN_IMAGE_SIZES = '(max-width: 767px) 100vw, 600px';
const MAIN_IMAGE_FALLBACK_WIDTH = 800;
const THUMBNAIL_WIDTH = 200;

interface ProductGalleryProps {
  images: ProductImageInfo[];
  productName: string;
}

export function ProductGallery({ images, productName }: ProductGalleryProps) {
  const [activeIndex, setActiveIndex] = useState(0);
  // Danh sách ảnh đổi khi khách đổi màu. Vị trí cũ có thể vượt danh sách mới nên kẹp lại.
  const activeImage = images[Math.min(activeIndex, images.length - 1)];

  return (
    <div className="gallery">
      <div className="gallery-main">
        {activeImage ? (
          <img
            src={withCloudinaryTransform(activeImage.url, MAIN_IMAGE_FALLBACK_WIDTH)}
            srcSet={buildCloudinarySrcSet(activeImage.url)}
            sizes={MAIN_IMAGE_SIZES}
            alt={activeImage.alt || productName}
            width={MAIN_IMAGE_FALLBACK_WIDTH}
            height={MAIN_IMAGE_FALLBACK_WIDTH}
            fetchPriority="high"
          />
        ) : (
          // Khung giữ chỗ cùng tỉ lệ với ảnh, để bố cục không nhảy khi sản phẩm chưa có ảnh.
          <div className="gallery-placeholder" aria-hidden="true">
            <Icon name="image" className="icon gallery-placeholder-icon" />
          </div>
        )}
      </div>
      {images.length > 1 && (
        <ul className="gallery-thumbs">
          {images.map((image, index) => (
            <li key={`${image.url}-${index}`}>
              <button
                type="button"
                aria-label={`Xem ảnh ${index + 1}: ${image.alt || productName}`}
                aria-pressed={image === activeImage}
                onClick={() => setActiveIndex(index)}
              >
                <img
                  src={withCloudinaryTransform(image.url, THUMBNAIL_WIDTH)}
                  alt=""
                  width={56}
                  height={56}
                  loading="lazy"
                />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
