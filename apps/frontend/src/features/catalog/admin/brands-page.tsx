import { useQuery, useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useCan } from '../../identity/hooks/use-actor';
import { AdminFormDialog } from './admin-form-dialog';
import { toCatalogErrorMessage } from './catalog-errors';

// Dữ liệu phản hồi thương hiệu sản phẩm từ API.
export interface BrandItem {
  id: string;
  name: string;
  slug: string;
  logoPublicId: string | null;
  createdAt: string;
  updatedAt: string;
}

// Màn hình quản lý danh sách và thông tin thương hiệu sản phẩm.
export function BrandsPage() {
  const canWrite = useCan('products:write');
  const queryClient = useQueryClient();

  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingBrand, setEditingBrand] = useState<BrandItem | null>(null);
  const [name, setName] = useState('');
  const [logoPublicId, setLogoPublicId] = useState('');

  const [formError, setFormError] = useState<string | null>(null);
  const [pageError, setPageError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Tải danh sách thương hiệu hiện có trong hệ thống.
  const {
    data: brands = [],
    isLoading,
    isError,
    error,
  } = useQuery<BrandItem[]>({
    queryKey: ['brands'],
    queryFn: () => apiRequest<BrandItem[]>('/api/brands'),
  });

  // Mở hộp thoại để tạo mới thương hiệu.
  function handleOpenCreate() {
    setEditingBrand(null);
    setName('');
    setLogoPublicId('');
    setFormError(null);
    setIsFormOpen(true);
  }

  // Mở hộp thoại chỉnh sửa thương hiệu đã chọn.
  function handleOpenEdit(brand: BrandItem) {
    setEditingBrand(brand);
    setName(brand.name);
    setLogoPublicId(brand.logoPublicId || '');
    setFormError(null);
    setIsFormOpen(true);
  }

  // Đóng hộp thoại chỉnh sửa.
  function handleCloseForm() {
    setIsFormOpen(false);
    setEditingBrand(null);
  }

  // Gửi thông tin tạo mới hoặc cập nhật thương hiệu lên máy chủ.
  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);

    const trimmedName = name.trim();
    // Bắt buộc nhập tên thương hiệu để tránh lưu dữ liệu không có ý nghĩa.
    if (!trimmedName) {
      setFormError('Tên thương hiệu không được để trống.');
      return;
    }

    setIsSubmitting(true);
    try {
      const payload = {
        name: trimmedName,
        logoPublicId: logoPublicId.trim() ? logoPublicId.trim() : null,
      };

      if (editingBrand) {
        await apiRequest(`/api/admin/brands/${editingBrand.id}`, {
          method: 'PATCH',
          body: JSON.stringify(payload),
        });
      } else {
        await apiRequest('/api/admin/brands', {
          method: 'POST',
          body: JSON.stringify(payload),
        });
      }

      // Làm mới dữ liệu danh sách thương hiệu sau khi lưu thành công.
      await queryClient.invalidateQueries({ queryKey: ['brands'] });
      setIsFormOpen(false);
      setEditingBrand(null);
    } catch (err: unknown) {
      // Giữ nguyên dữ liệu người dùng đang nhập khi phát sinh lỗi từ API.
      setFormError(toCatalogErrorMessage(err, 'Lưu thông tin thương hiệu thất bại.'));
    } finally {
      setIsSubmitting(false);
    }
  }

  // Xóa thương hiệu và kiểm tra lỗi ràng buộc sản phẩm.
  async function handleDelete(brand: BrandItem) {
    if (!window.confirm(`Bạn có chắc chắn muốn xóa thương hiệu "${brand.name}"?`)) {
      return;
    }
    setPageError(null);
    try {
      await apiRequest(`/api/admin/brands/${brand.id}`, {
        method: 'DELETE',
      });
      // Làm mới danh sách thương hiệu sau khi xóa thành công.
      await queryClient.invalidateQueries({ queryKey: ['brands'] });
    } catch (err: unknown) {
      // Thông báo rõ cho người dùng khi thương hiệu đang có sản phẩm liên kết.
      setPageError(toCatalogErrorMessage(err, 'Xóa thương hiệu thất bại.'));
    }
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-6)',
        fontFamily: 'var(--font-sans)',
      }}
    >
      {/* Khối đầu trang xuống dòng khi hẹp. Tiêu đề và nút tạo không bị ép ngắt từng chữ. */}
      <div
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          justifyContent: 'space-between',
          alignItems: 'center',
          gap: 'var(--space-4)',
        }}
      >
        <div style={{ flex: '1 1 240px', minWidth: 0 }}>
          <h1
            style={{
              fontSize: 'var(--text-xl)',
              fontWeight: 700,
              color: 'var(--color-ink)',
              margin: '0 0 var(--space-1) 0',
            }}
          >
            Quản lý thương hiệu
          </h1>
          <p style={{ margin: 0, color: 'var(--color-ink-muted)', fontSize: 'var(--text-sm)' }}>
            Danh sách các thương hiệu sản phẩm kinh doanh trên hệ thống.
          </p>
        </div>

        {canWrite && (
          <button
            type="button"
            onClick={handleOpenCreate}
            style={{
              padding: 'var(--space-2) var(--space-4)',
              whiteSpace: 'nowrap',
              backgroundColor: 'var(--color-brand)',
              color: 'var(--color-surface)',
              border: 'none',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-sm)',
              fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            + Thêm thương hiệu
          </button>
        )}
      </div>

      {/* Thông báo lỗi ở mức trang khi thao tác xóa thất bại */}
      {pageError && (
        <div
          role="alert"
          style={{
            padding: 'var(--space-3) var(--space-4)',
            backgroundColor: 'var(--color-surface-muted)',
            border: '1px solid var(--color-danger)',
            color: 'var(--color-danger)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
          }}
        >
          {pageError}
        </div>
      )}

      {/* Trạng thái tải dữ liệu */}
      {isLoading && (
        <div
          style={{
            padding: 'var(--space-6)',
            textAlign: 'center',
            color: 'var(--color-ink-muted)',
          }}
        >
          Đang nạp dữ liệu thương hiệu...
        </div>
      )}

      {isError && (
        <div
          role="alert"
          style={{
            padding: 'var(--space-4)',
            backgroundColor: 'var(--color-surface-muted)',
            border: '1px solid var(--color-danger)',
            color: 'var(--color-danger)',
            borderRadius: 'var(--radius-card)',
          }}
        >
          {error instanceof Error ? error.message : 'Tải danh sách thương hiệu thất bại.'}
        </div>
      )}

      {/* Bảng danh sách thương hiệu */}
      {!isLoading && !isError && (
        <div
          style={{
            backgroundColor: 'var(--color-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-card)',
            padding: 'var(--space-4)',
          }}
        >
          {brands.length === 0 ? (
            <div
              style={{
                padding: 'var(--space-6)',
                textAlign: 'center',
                color: 'var(--color-ink-muted)',
                fontSize: 'var(--text-sm)',
              }}
            >
              Chưa có thương hiệu nào. Hãy tạo thương hiệu đầu tiên.
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
              {brands.map((brand) => (
                <div
                  key={brand.id}
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    border: '1px solid var(--color-border)',
                    borderRadius: 'var(--radius-control)',
                    display: 'flex',
                    flexWrap: 'wrap',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    gap: 'var(--space-2)',
                  }}
                >
                  {/* Khối thông tin phải co lại được và ngắt dòng `logoPublicId` dài. Chuỗi liền làm trang tràn ngang ở 375px. */}
                  <div
                    style={{
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 'var(--space-1)',
                      minWidth: 0,
                    }}
                  >
                    <strong style={{ fontSize: 'var(--text-md)', color: 'var(--color-ink)' }}>
                      {brand.name}
                    </strong>
                    <span
                      style={{
                        fontSize: 'var(--text-xs)',
                        color: 'var(--color-ink-muted)',
                        overflowWrap: 'anywhere',
                      }}
                    >
                      slug: <code>{brand.slug}</code>
                      {brand.logoPublicId ? ` · logo: ${brand.logoPublicId}` : ''}
                    </span>
                  </div>

                  {canWrite && (
                    <div style={{ display: 'flex', gap: 'var(--space-2)' }}>
                      <button
                        type="button"
                        aria-label={`Sửa ${brand.name}`}
                        onClick={() => handleOpenEdit(brand)}
                        style={{
                          padding: 'var(--space-1) var(--space-3)',
                          backgroundColor: 'var(--color-surface)',
                          border: '1px solid var(--color-border)',
                          borderRadius: 'var(--radius-control)',
                          fontSize: 'var(--text-xs)',
                          cursor: 'pointer',
                        }}
                      >
                        Sửa
                      </button>
                      <button
                        type="button"
                        aria-label={`Xóa ${brand.name}`}
                        onClick={() => handleDelete(brand)}
                        style={{
                          padding: 'var(--space-1) var(--space-3)',
                          backgroundColor: 'var(--color-surface)',
                          color: 'var(--color-danger)',
                          border: '1px solid var(--color-border)',
                          borderRadius: 'var(--radius-control)',
                          fontSize: 'var(--text-xs)',
                          cursor: 'pointer',
                        }}
                      >
                        Xóa
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Hộp thoại thêm hoặc sửa thương hiệu dùng khung chung */}
      {canWrite && (
        <AdminFormDialog
          isOpen={isFormOpen}
          onClose={handleCloseForm}
          title={editingBrand ? `Sửa thương hiệu: ${editingBrand.name}` : 'Thêm thương hiệu mới'}
          formId="brand-form"
          onSubmit={handleSubmit}
          isSubmitting={isSubmitting}
          submitText={editingBrand ? 'Lưu thay đổi' : 'Tạo thương hiệu'}
          error={formError}
          maxWidth={500}
        >
          <div>
            <label
              htmlFor="brand-name"
              style={{
                display: 'block',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                color: 'var(--color-ink)',
                marginBottom: 'var(--space-1)',
              }}
            >
              Tên thương hiệu *
            </label>
            <input
              id="brand-name"
              type="text"
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Apple, Asus, Samsung..."
              style={{
                width: '100%',
                padding: 'var(--space-2) var(--space-3)',
                borderRadius: 'var(--radius-control)',
                border: '1px solid var(--color-border)',
                fontSize: 'var(--text-sm)',
                backgroundColor: 'var(--color-surface)',
                color: 'var(--color-ink)',
              }}
            />
          </div>

          <div>
            <label
              htmlFor="brand-logo"
              style={{
                display: 'block',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                color: 'var(--color-ink)',
                marginBottom: 'var(--space-1)',
              }}
            >
              Mã logo Cloudinary (tùy chọn)
            </label>
            <input
              id="brand-logo"
              type="text"
              value={logoPublicId}
              onChange={(e) => setLogoPublicId(e.target.value)}
              placeholder="pickory/brands/apple_logo"
              style={{
                width: '100%',
                padding: 'var(--space-2) var(--space-3)',
                borderRadius: 'var(--radius-control)',
                border: '1px solid var(--color-border)',
                fontSize: 'var(--text-sm)',
                backgroundColor: 'var(--color-surface)',
                color: 'var(--color-ink)',
              }}
            />
          </div>
        </AdminFormDialog>
      )}
    </div>
  );
}
