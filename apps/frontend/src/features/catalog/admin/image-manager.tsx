import { useEffect, useRef, useState, type ChangeEvent } from 'react';
import { ErrorAlert } from '../../../shared/error-alert';
import { FieldError, fieldErrorProps } from '../../../shared/field-error';
import { Icon } from '../../../shared/icons';
import { withCloudinaryTransform } from '../shared/image-url';
import { MAX_IMAGES_PER_PRODUCT } from './image-upload';

// Ảnh đang soạn trong form. `url` dùng để hiện ảnh thu nhỏ, backend không lưu trường này.
export interface ProductImageDraft {
  publicId: string;
  alt: string;
  skuId: string | null;
  url: string;
}

export interface ImageVariantOption {
  id: string;
  label: string;
}

interface ImageManagerProps {
  images: ProductImageDraft[];
  // Chỉ biến thể đã lưu mới có `id` để gắn ảnh.
  variants: ImageVariantOption[];
  disabled: boolean;
  isUploading: boolean;
  uploadErrors: string[];
  // Lỗi alt theo `publicId`, để lỗi đi theo ảnh khi ảnh đổi vị trí.
  altErrors: Record<string, string>;
  onChange: (images: ProductImageDraft[]) => void;
  onAddFiles: (files: File[]) => void;
}

type MoveAction = 'up' | 'down';

// Đổi chỗ một ảnh trong danh sách. Trả danh sách mới để React nhận ra thay đổi.
function moveImage(images: ProductImageDraft[], from: number, to: number): ProductImageDraft[] {
  const next = [...images];
  const [moved] = next.splice(from, 1);
  if (moved) next.splice(to, 0, moved);
  return next;
}

// Quản lý ảnh sản phẩm: tải thêm, sắp xếp bằng nút hoặc kéo thả, sửa alt, gắn biến thể và bỏ ảnh.
export function ImageManager({
  images,
  variants,
  disabled,
  isUploading,
  uploadErrors,
  altErrors,
  onChange,
  onAddFiles,
}: ImageManagerProps) {
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const pendingFocus = useRef<{ publicId: string; action: MoveAction } | null>(null);
  const listRef = useRef<HTMLOListElement>(null);

  // Sau khi đổi chỗ, đưa focus về nút của chính ảnh vừa di chuyển, để người dùng bàn phím bấm tiếp.
  useEffect(() => {
    const target = pendingFocus.current;
    if (!target || !listRef.current) return;
    pendingFocus.current = null;
    const item = listRef.current.querySelector(`[data-image-id="${CSS.escape(target.publicId)}"]`);
    const sameAction = item?.querySelector<HTMLButtonElement>(
      `button[data-action="${target.action}"]`,
    );
    // Ảnh đã tới đầu hoặc cuối thì nút cùng hướng bị khóa, nên focus sang nút hướng ngược lại.
    const otherAction = item?.querySelector<HTMLButtonElement>(
      `button[data-action="${target.action === 'up' ? 'down' : 'up'}"]`,
    );
    (sameAction && !sameAction.disabled ? sameAction : otherAction)?.focus();
  }, [images]);

  function move(index: number, action: MoveAction) {
    const targetIndex = action === 'up' ? index - 1 : index + 1;
    if (targetIndex < 0 || targetIndex >= images.length) return;
    pendingFocus.current = { publicId: images[index]!.publicId, action };
    onChange(moveImage(images, index, targetIndex));
  }

  function update(index: number, changes: Partial<ProductImageDraft>) {
    onChange(
      images.map((image, position) => (position === index ? { ...image, ...changes } : image)),
    );
  }

  function handleFiles(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? []);
    // Xóa lựa chọn để chọn lại cùng file vẫn phát sự kiện `change`.
    event.target.value = '';
    if (files.length > 0) onAddFiles(files);
  }

  const isFull = images.length >= MAX_IMAGES_PER_PRODUCT;

  return (
    <section className="card product-section">
      <h2>Ảnh sản phẩm</h2>
      <p>
        JPG, PNG hoặc WebP, tối đa 5 MB mỗi ảnh và {MAX_IMAGES_PER_PRODUCT} ảnh mỗi sản phẩm. Ảnh
        đầu tiên là ảnh đại diện.
      </p>

      {uploadErrors.length > 0 && <ErrorAlert items={uploadErrors} />}

      {images.length === 0 ? (
        <p className="empty-state empty-state-dashed">Chưa có ảnh nào.</p>
      ) : (
        <ol className="image-list" ref={listRef}>
          {images.map((image, index) => {
            const altId = `image-alt-${index}`;
            const altError = altErrors[image.publicId];
            const position = index + 1;
            return (
              <li
                key={image.publicId}
                className="image-item"
                data-image-id={image.publicId}
                data-dragging={dragIndex === index}
                // Kéo thả chỉ dành cho chuột. Người dùng bàn phím dùng nút lên và xuống.
                draggable={!disabled}
                onDragStart={() => setDragIndex(index)}
                onDragOver={(event) => {
                  if (dragIndex !== null) event.preventDefault();
                }}
                onDrop={(event) => {
                  event.preventDefault();
                  if (dragIndex !== null && dragIndex !== index) {
                    onChange(moveImage(images, dragIndex, index));
                  }
                  setDragIndex(null);
                }}
                onDragEnd={() => setDragIndex(null)}
              >
                <img
                  className="image-thumb"
                  src={withCloudinaryTransform(image.url, 200)}
                  alt={image.alt}
                  width={80}
                  height={80}
                />
                <div className="image-fields">
                  <div className="field">
                    <label htmlFor={altId}>Mô tả ảnh {position} *</label>
                    <input
                      id={altId}
                      name={altId}
                      value={image.alt}
                      disabled={disabled}
                      maxLength={200}
                      placeholder="Ví dụ: Mặt trước điện thoại màu đỏ"
                      onChange={(event) => update(index, { alt: event.target.value })}
                      {...fieldErrorProps(altId, altError)}
                    />
                    <FieldError inputId={altId} message={altError} />
                  </div>
                  <div className="field">
                    <label htmlFor={`image-variant-${index}`}>Biến thể của ảnh {position}</label>
                    <select
                      id={`image-variant-${index}`}
                      value={image.skuId ?? ''}
                      disabled={disabled}
                      onChange={(event) => update(index, { skuId: event.target.value || null })}
                    >
                      <option value="">Mọi biến thể</option>
                      {variants.map((variant) => (
                        <option key={variant.id} value={variant.id}>
                          {variant.label}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
                {!disabled && (
                  <div className="image-actions">
                    <button
                      type="button"
                      className="icon-btn"
                      data-action="up"
                      aria-label={`Đưa ảnh ${position} lên`}
                      disabled={index === 0}
                      onClick={() => move(index, 'up')}
                    >
                      <Icon name="chevronUp" />
                    </button>
                    <button
                      type="button"
                      className="icon-btn"
                      data-action="down"
                      aria-label={`Đưa ảnh ${position} xuống`}
                      disabled={index === images.length - 1}
                      onClick={() => move(index, 'down')}
                    >
                      <Icon name="chevronDown" />
                    </button>
                    <button
                      type="button"
                      className="icon-btn image-remove"
                      aria-label={`Bỏ ảnh ${position}`}
                      onClick={() => onChange(images.filter((_, itemIndex) => itemIndex !== index))}
                    >
                      <Icon name="trash" />
                    </button>
                  </div>
                )}
              </li>
            );
          })}
        </ol>
      )}

      {!disabled &&
        (isFull ? (
          <p className="muted small">
            Đã đủ {MAX_IMAGES_PER_PRODUCT} ảnh. Bỏ bớt ảnh để thêm ảnh mới.
          </p>
        ) : (
          // Ô chọn file ẩn bằng mắt nhưng vẫn nhận focus; nhãn đóng vai nút để bấm bằng chuột hoặc phím.
          <label className="btn image-upload-button">
            <Icon name="upload" />
            Thêm ảnh
            <input
              type="file"
              className="sr"
              accept=".jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp"
              multiple
              disabled={isUploading}
              onChange={handleFiles}
            />
          </label>
        ))}
      {isUploading && (
        <p role="status" className="muted small">
          Đang tải ảnh lên...
        </p>
      )}
    </section>
  );
}
