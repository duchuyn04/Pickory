import { useQuery, useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { ActiveFilterBar, type ActiveFilter } from '../../../shared/active-filter-bar';
import { apiRequest } from '../../../shared/api';
import { FilteredEmptyState } from '../../../shared/filtered-empty-state';
import { Pager } from '../../../shared/pager';
import { rowNumber } from '../../../shared/row-number';
import { SearchInput } from '../../../shared/search-input';
import { useListFilters } from '../../../shared/use-list-filters';
import { useCan } from '../../identity/hooks/use-actor';
import { AdminFormDialog } from '../../../shared/admin-form-dialog';
import { BrandLogoField } from './brand-logo-field';
import { toCatalogErrorMessage } from './catalog-errors';
import { filterAndSortBrands } from './catalog-filters';
import {
  uploadToCloudinary,
  validateImageFile,
  type UploadedImage,
  type UploadSignature,
} from './image-upload';
import { ErrorAlert } from '../../../shared/error-alert';
import './catalog-admin.css';

// Dữ liệu phản hồi thương hiệu sản phẩm từ API.
export interface BrandItem {
  id: string;
  name: string;
  slug: string;
  logoPublicId: string | null;
  logoUrl: string | null;
  createdAt: string;
  updatedAt: string;
}

// Màn lọc trên dữ liệu đã tải. Các tham số này chỉ thuộc URL của trình duyệt.
const FILTER_NAMES = ['search', 'logo', 'sort'] as const;
const BRAND_PAGE_SIZE = 10;
const LOGO_FILTER_LABELS: Record<string, string> = {
  'with-logo': 'Có logo',
  'without-logo': 'Chưa có logo',
};
const SORT_LABELS: Record<string, string> = {
  'name-asc': 'Tên A-Z',
  'name-desc': 'Tên Z-A',
  newest: 'Mới nhất',
  oldest: 'Cũ nhất',
};

type BrandFilterName = (typeof FILTER_NAMES)[number];

// Màn hình quản lý danh sách và thông tin thương hiệu sản phẩm.
export function BrandsPage() {
  const canWrite = useCan('products:write');
  const queryClient = useQueryClient();
  // Bộ lọc và số trang nằm trên URL. Mở lại link cho đúng phần danh sách đang xem.
  const { values, page, setFilter, clearFilters, setPage } = useListFilters(FILTER_NAMES);
  const keyword = values.search;

  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingBrand, setEditingBrand] = useState<BrandItem | null>(null);
  const [name, setName] = useState('');
  const [logo, setLogo] = useState<UploadedImage | null>(null);
  const [logoUploadError, setLogoUploadError] = useState<string | null>(null);
  const [isLogoUploading, setIsLogoUploading] = useState(false);

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

  // Lọc và sắp xếp sau mỗi lần tải lại để thao tác thêm, sửa hoặc xóa giữ điều kiện hiện tại.
  const matchingBrands = filterAndSortBrands(brands, {
    keyword,
    logo: values.logo,
    sort: values.sort,
  });
  // Chỉ cắt danh sách sau khi lọc. Tổng số kết quả quyết định số trang của `Pager`.
  const pageBrands = matchingBrands.slice((page - 1) * BRAND_PAGE_SIZE, page * BRAND_PAGE_SIZE);
  const hasFilters = keyword.trim() !== '' || values.logo !== '';
  const activeFilters: ActiveFilter<BrandFilterName>[] = [];
  if (keyword.trim()) activeFilters.push({ name: 'search', label: `Từ khóa: ${keyword.trim()}` });
  if (values.logo) {
    // Mã lạ trên URL vẫn cần một nhãn để có thể bỏ giá trị đó.
    activeFilters.push({
      name: 'logo',
      label: `Logo: ${LOGO_FILTER_LABELS[values.logo] ?? values.logo}`,
    });
  }
  if (values.sort) {
    activeFilters.push({
      name: 'sort',
      label: `Sắp xếp: ${SORT_LABELS[values.sort] ?? values.sort}`,
    });
  }

  // Mở hộp thoại để tạo mới thương hiệu.
  function handleOpenCreate() {
    setEditingBrand(null);
    setName('');
    setLogo(null);
    setLogoUploadError(null);
    setIsLogoUploading(false);
    setFormError(null);
    setIsFormOpen(true);
  }

  // Mở hộp thoại chỉnh sửa thương hiệu đã chọn.
  function handleOpenEdit(brand: BrandItem) {
    setEditingBrand(brand);
    setName(brand.name);
    // Đưa logo đã lưu vào form để thao tác chỉ sửa tên không làm mất logo.
    setLogo(
      brand.logoPublicId && brand.logoUrl
        ? { publicId: brand.logoPublicId, url: brand.logoUrl }
        : null,
    );
    setLogoUploadError(null);
    setIsLogoUploading(false);
    setFormError(null);
    setIsFormOpen(true);
  }

  // Đóng hộp thoại chỉnh sửa.
  function handleCloseForm() {
    // Không đóng form trong lúc tải để kết quả chậm không ghi vào một lần mở form khác.
    if (isLogoUploading) return;
    setIsFormOpen(false);
    setEditingBrand(null);
  }

  async function handleSelectLogo(file: File) {
    setLogoUploadError(null);
    // Chặn file sai trước khi xin chữ ký để không phát sinh request không cần thiết.
    const validationError = validateImageFile(file);
    if (validationError) {
      setLogoUploadError(validationError);
      return;
    }

    // Khóa nút lưu trong lúc tải để form không ghi logo cũ trước khi ảnh mới sẵn sàng.
    setIsLogoUploading(true);
    try {
      // Route riêng khóa thư mục `pickory/brands` trong chữ ký.
      const signature = await apiRequest<UploadSignature>(
        '/api/admin/uploads/brand-logo/signature',
        {
          method: 'POST',
        },
      );
      // File đi thẳng tới Cloudinary và không đi qua API Pickory.
      const uploaded = await uploadToCloudinary(file, signature);
      // Chỉ thay logo đang hiện sau khi Cloudinary xác nhận tải thành công.
      setLogo(uploaded);
    } catch (error: unknown) {
      // Giữ logo cũ khi tải thất bại để thao tác sửa tên vẫn không làm mất ảnh.
      setLogoUploadError(toCatalogErrorMessage(error, 'Tải logo thất bại. Hãy thử lại.'));
    } finally {
      // Mở khóa nút lưu sau cả thành công lẫn thất bại để có thể tiếp tục hoặc thử lại.
      setIsLogoUploading(false);
    }
  }

  // Gửi thông tin tạo mới hoặc cập nhật thương hiệu lên máy chủ.
  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    // Enter có thể gửi form trong lúc tải. Không lưu cho tới khi có kết quả upload.
    if (isLogoUploading) return;
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
        logoPublicId: logo?.publicId ?? null,
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
      {pageError && <ErrorAlert message={pageError} />}

      {isLoading && (
        <p role="status" className="empty-state">
          Đang nạp dữ liệu thương hiệu...
        </p>
      )}

      {isError && (
        <ErrorAlert
          message={error instanceof Error ? error.message : 'Tải danh sách thương hiệu thất bại.'}
        />
      )}

      {!isLoading && !isError && (
        <div className="card card-flush">
          <div className="filters-row">
            <SearchInput
              label="Tìm theo tên hoặc slug thương hiệu"
              value={keyword}
              onSearch={(text) => setFilter('search', text)}
            />
            <div className="field">
              <label htmlFor="brand-logo-filter">Logo</label>
              <select
                id="brand-logo-filter"
                value={values.logo}
                onChange={(event) => setFilter('logo', event.target.value)}
              >
                <option value="">Tất cả</option>
                <option value="with-logo">Có logo</option>
                <option value="without-logo">Chưa có logo</option>
              </select>
            </div>
            <div className="field">
              <label htmlFor="brand-sort">Sắp xếp</label>
              <select
                id="brand-sort"
                value={values.sort}
                onChange={(event) => setFilter('sort', event.target.value)}
              >
                <option value="">Tên A-Z</option>
                <option value="name-desc">Tên Z-A</option>
                <option value="newest">Mới nhất</option>
                <option value="oldest">Cũ nhất</option>
              </select>
            </div>
          </div>
          <ActiveFilterBar
            filters={activeFilters}
            summary={`${matchingBrands.length} thương hiệu`}
            onRemove={(filterName) => setFilter(filterName, '')}
            onClearAll={clearFilters}
          />
          {brands.length === 0 && !hasFilters ? (
            <p className="empty-state">Chưa có thương hiệu nào. Hãy tạo thương hiệu đầu tiên.</p>
          ) : pageBrands.length === 0 ? (
            <FilteredEmptyState page={page} onFirstPage={() => setPage(1)} />
          ) : (
            <div className="table-wrap">
              <table className="tbl">
                <thead>
                  <tr>
                    <th scope="col" className="num col-row-number">
                      STT
                    </th>
                    <th scope="col">Thương hiệu</th>
                    {canWrite && (
                      <th scope="col" className="num">
                        Thao tác
                      </th>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {pageBrands.map((brand, index) => (
                    <tr key={brand.id}>
                      {/* STT theo vị trí sau khi lọc và sắp xếp. Trang sau tiếp nối số của trang trước. */}
                      <td className="num col-row-number">
                        {rowNumber({ page, limit: BRAND_PAGE_SIZE }, index)}
                      </td>
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
          {/* Trang vượt giới hạn chỉ cần nút Về trang 1. Không hiện thanh phân trang có số trang sai. */}
          {pageBrands.length > 0 && (
            <Pager
              page={page}
              total={matchingBrands.length}
              limit={BRAND_PAGE_SIZE}
              onPageChange={setPage}
              label="Phân trang thương hiệu"
            />
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
          submitDisabled={isLogoUploading}
          closeDisabled={isLogoUploading}
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

          <BrandLogoField
            logo={logo}
            brandName={name}
            isUploading={isLogoUploading}
            error={logoUploadError}
            onSelect={handleSelectLogo}
          />
        </AdminFormDialog>
      )}
    </div>
  );
}
