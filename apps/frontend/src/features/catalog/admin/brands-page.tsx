import { useQuery, useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useCan } from '../../identity/hooks/use-actor';
import { AdminFormDialog } from './admin-form-dialog';
import { toCatalogErrorMessage } from './catalog-errors';
import './catalog-admin.css';

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
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Quản lý thương hiệu</h1>
          <p>Danh sách các thương hiệu sản phẩm kinh doanh trên hệ thống.</p>
        </div>

        {/* Chỉ quyền products:write được tạo thương hiệu. */}
        {canWrite && (
          <button type="button" className="btn btn-primary" onClick={handleOpenCreate}>
            + Thêm thương hiệu
          </button>
        )}
      </div>

      {/* Lỗi xóa hiện ở mức trang vì hộp thoại đã đóng khi người dùng bấm Xóa. */}
      {pageError && (
        <p role="alert" className="alert">
          {pageError}
        </p>
      )}

      {isLoading && (
        <p role="status" className="empty-state">
          Đang nạp dữ liệu thương hiệu...
        </p>
      )}

      {isError && (
        <p role="alert" className="alert">
          {error instanceof Error ? error.message : 'Tải danh sách thương hiệu thất bại.'}
        </p>
      )}

      {!isLoading && !isError && (
        <div className="card card-flush">
          {brands.length === 0 ? (
            <p className="empty-state">Chưa có thương hiệu nào. Hãy tạo thương hiệu đầu tiên.</p>
          ) : (
            <div className="table-wrap">
              <table className="tbl">
                <thead>
                  <tr>
                    <th scope="col">Thương hiệu</th>
                    {canWrite && (
                      <th scope="col" className="num">
                        Thao tác
                      </th>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {brands.map((brand) => (
                    <tr key={brand.id}>
                      <td>
                        <strong>{brand.name}</strong>
                        {/* Slug và mã logo là chuỗi liền, phải ngắt được để trang không tràn ngang ở 375px. */}
                        <span className="cell-sub wrap-anywhere">
                          slug: <code>{brand.slug}</code>
                          {brand.logoPublicId ? ` · logo: ${brand.logoPublicId}` : ''}
                        </span>
                      </td>
                      {canWrite && (
                        <td className="num">
                          <div className="row-actions catalog-row-actions">
                            <button
                              type="button"
                              className="btn btn-sm"
                              aria-label={`Sửa ${brand.name}`}
                              onClick={() => handleOpenEdit(brand)}
                            >
                              Sửa
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-danger"
                              aria-label={`Xóa ${brand.name}`}
                              onClick={() => handleDelete(brand)}
                            >
                              Xóa
                            </button>
                          </div>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
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
        >
          <div className="field">
            <label htmlFor="brand-name">Tên thương hiệu *</label>
            <input
              id="brand-name"
              type="text"
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Apple, Asus, Samsung..."
            />
          </div>

          <div className="field">
            <label htmlFor="brand-logo">Mã logo Cloudinary (tùy chọn)</label>
            <input
              id="brand-logo"
              type="text"
              value={logoPublicId}
              onChange={(e) => setLogoPublicId(e.target.value)}
              placeholder="pickory/brands/apple_logo"
            />
          </div>
        </AdminFormDialog>
      )}
    </div>
  );
}
