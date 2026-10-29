import type { ChangeEvent } from 'react';
import { ErrorAlert } from '../../../shared/error-alert';
import { Icon } from '../../../shared/icons';
import { withCloudinaryTransform } from '../shared/image-url';
import type { UploadedImage } from './image-upload';

interface BrandLogoFieldProps {
  logo: UploadedImage | null;
  brandName: string;
  isUploading: boolean;
  error: string | null;
  onSelect: (file: File) => void;
}

export function BrandLogoField({
  logo,
  brandName,
  isUploading,
  error,
  onSelect,
}: BrandLogoFieldProps) {
  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    // Xóa lựa chọn trong input để lần chọn lại cùng file vẫn phát sự kiện `change`.
    event.target.value = '';
    if (file) onSelect(file);
  }

  const trimmedName = brandName.trim();
  const imageAlt = trimmedName ? `Logo của ${trimmedName}` : 'Xem trước logo thương hiệu';

  return (
    <div className="field">
      <span>Logo thương hiệu</span>
      <div className="brand-logo-editor">
        {logo ? (
          <img
            className="brand-logo-preview"
            src={withCloudinaryTransform(logo.url, 200)}
            alt={imageAlt}
            width={80}
            height={80}
          />
        ) : (
          <span className="brand-logo-empty">Chưa có logo</span>
        )}
        <div className="brand-logo-actions">
          {/* Nhãn là vùng bấm của input file, nên chọn ảnh được bằng chuột hoặc bàn phím. */}
          <label className="btn brand-logo-upload">
            <Icon name="upload" />
            {logo ? 'Đổi ảnh' : 'Chọn ảnh từ máy'}
            <input
              type="file"
              className="sr"
              accept=".jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp"
              disabled={isUploading}
              onChange={handleFileChange}
            />
          </label>
          <small>JPG, PNG hoặc WebP, tối đa 5 MB.</small>
        </div>
      </div>
      {isUploading && (
        <p role="status" className="muted small">
          Đang tải logo lên...
        </p>
      )}
      {error && <ErrorAlert message={error} />}
    </div>
  );
}
