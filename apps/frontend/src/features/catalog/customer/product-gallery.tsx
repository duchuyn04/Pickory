import { useState } from 'react';
import { buildCloudinarySrcSet, withCloudinaryTransform } from '../shared/image-url';
import { ProductImage } from '../shared/product-image';
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
        <ProductImage
          src={
            activeImage ? withCloudinaryTransform(activeImage.url, MAIN_IMAGE_FALLBACK_WIDTH) : null
          }
          srcSet={activeImage ? buildCloudinarySrcSet(activeImage.url) : undefined}
          sizes={MAIN_IMAGE_SIZES}
          alt={activeImage ? activeImage.alt || productName : productName}
          width={MAIN_IMAGE_FALLBACK_WIDTH}
          height={MAIN_IMAGE_FALLBACK_WIDTH}
          fetchPriority="high"
        />
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
                <ProductImage
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
