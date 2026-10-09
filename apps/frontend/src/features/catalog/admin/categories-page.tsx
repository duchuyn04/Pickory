import { useQuery, useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import {
  validateAttributeDefinitions,
  type AttributeDefinition,
  type AttributeDefinitionError,
} from '../../../shared/catalog/attributes';
import { useCan } from '../../identity/hooks/use-actor';
import { AdminFormDialog } from './admin-form-dialog';
import { AttributeEditor } from './attribute-editor';
import { toCatalogErrorMessage } from './catalog-errors';
import { ErrorAlert } from '../../../shared/error-alert';
import './catalog-admin.css';

// Bản ghi danh mục trả về từ API quản trị bao gồm đầy đủ thuộc tính riêng.
export interface CategoryItem {
  id: string;
  parentId: string | null;
  name: string;
  slug: string;
  position: number;
  attributes: AttributeDefinition[];
  createdAt: string;
  updatedAt: string;
}

// Màn hình quản lý cây danh mục sản phẩm và định nghĩa bộ thuộc tính kỹ thuật.
export function CategoriesPage() {
  const canWrite = useCan('products:write');
  const queryClient = useQueryClient();

  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingCategory, setEditingCategory] = useState<CategoryItem | null>(null);
  const [name, setName] = useState('');
  const [parentId, setParentId] = useState<string>('');
  const [position, setPosition] = useState<number>(0);
  const [attributes, setAttributes] = useState<AttributeDefinition[]>([]);

  const [formError, setFormError] = useState<string | null>(null);
  const [pageError, setPageError] = useState<string | null>(null);
  const [attributeErrors, setAttributeErrors] = useState<AttributeDefinitionError[]>([]);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Tải danh sách phẳng toàn bộ danh mục kèm đầy đủ thuộc tính riêng.
  const {
    data: categories = [],
    isLoading,
    isError,
    error,
  } = useQuery<CategoryItem[]>({
    queryKey: ['admin-categories'],
    queryFn: () => apiRequest<CategoryItem[]>('/api/admin/categories'),
  });

  // Danh mục gốc không có parentId, sắp xếp theo vị trí và tên hiển thị.
  const roots = categories
    .filter((category) => !category.parentId)
    .sort((a, b) => a.position - b.position || a.name.localeCompare(b.name));

  // Gom các danh mục con theo parentId tương ứng để dựng cây phân cấp 2 cấp.
  const childrenByParent: Record<string, CategoryItem[]> = {};
  for (const category of categories) {
    if (category.parentId) {
      const parentKey = category.parentId;
      const list = childrenByParent[parentKey] ?? [];
      list.push(category);
      childrenByParent[parentKey] = list;
    }
  }

  // Sắp xếp các danh mục con trong từng nhánh theo vị trí và tên.
  for (const list of Object.values(childrenByParent)) {
    list.sort((a, b) => a.position - b.position || a.name.localeCompare(b.name));
  }

  // Mở hộp thoại để tạo danh mục gốc mới.
  function handleOpenCreateRoot() {
    setEditingCategory(null);
    setName('');
    setParentId('');
    setPosition(0);
    setAttributes([]);
    setFormError(null);
    setAttributeErrors([]);
    setIsFormOpen(true);
  }

  // Mở hộp thoại để tạo danh mục con thuộc một danh mục gốc xác định.
  function handleOpenCreateChild(parent: CategoryItem) {
    setEditingCategory(null);
    setName('');
    setParentId(parent.id);
    setPosition(0);
    setAttributes([]);
    setFormError(null);
    setAttributeErrors([]);
    setIsFormOpen(true);
  }

  // Mở hộp thoại chỉnh sửa thông tin và bộ thuộc tính của danh mục đã chọn.
  function handleOpenEdit(category: CategoryItem) {
    setEditingCategory(category);
    setName(category.name);
    setParentId(category.parentId || '');
    setPosition(category.position);
    setAttributes(category.attributes || []);
    setFormError(null);
    setAttributeErrors([]);
    setIsFormOpen(true);
  }

  // Đóng hộp thoại chỉnh sửa.
  function handleCloseForm() {
    setIsFormOpen(false);
    setEditingCategory(null);
  }

  // Gửi dữ liệu tạo mới hoặc cập nhật danh mục lên API.
  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);
    setAttributeErrors([]);

    const trimmedName = name.trim();
    // Bắt buộc nhập tên danh mục để tránh lưu bản ghi rỗng vào hệ thống.
    if (!trimmedName) {
      setFormError('Tên danh mục không được để trống.');
      return;
    }

    // Thẩm định danh sách thuộc tính trước khi gửi để tránh request không hợp lệ.
    const validation = validateAttributeDefinitions(attributes);
    if (!validation.valid) {
      setAttributeErrors(validation.errors);
      setFormError('Bộ thuộc tính của danh mục có lỗi, vui lòng kiểm tra lại.');
      return;
    }

    // Lọc sạch dữ liệu thừa: bỏ options khi không phải enum, bỏ unit khi rỗng.
    const sanitizedAttributes: AttributeDefinition[] = attributes.map((attr) => {
      const item: AttributeDefinition = {
        key: attr.key.trim(),
        label: attr.label.trim(),
        group: attr.group.trim(),
        type: attr.type,
        required: attr.required,
        filterable: attr.filterable,
      };
      if (attr.unit && attr.unit.trim().length > 0) {
        item.unit = attr.unit.trim();
      }
      if (attr.type === 'enum' && attr.options && attr.options.length > 0) {
        item.options = attr.options;
      }
      return item;
    });

    setIsSubmitting(true);
    try {
      const payload = {
        name: trimmedName,
        parentId: parentId ? parentId : null,
        position: Number(position) || 0,
        attributes: sanitizedAttributes,
      };

      if (editingCategory) {
        await apiRequest(`/api/admin/categories/${editingCategory.id}`, {
          method: 'PATCH',
          body: JSON.stringify(payload),
        });
      } else {
        await apiRequest('/api/admin/categories', {
          method: 'POST',
          body: JSON.stringify(payload),
        });
      }

      // Làm mới dữ liệu trong bộ nhớ đệm sau khi lưu thành công.
      await queryClient.invalidateQueries({ queryKey: ['admin-categories'] });
      setIsFormOpen(false);
      setEditingCategory(null);
    } catch (err: unknown) {
      // Giữ nguyên dữ liệu đang soạn thảo khi API trả về lỗi nghiệp vụ.
      setFormError(toCatalogErrorMessage(err, 'Lưu danh mục thất bại.'));
    } finally {
      setIsSubmitting(false);
    }
  }

  // Xóa danh mục và yêu cầu xác nhận để tránh mất dữ liệu do bấm nhầm.
  async function handleDelete(category: CategoryItem) {
    if (!window.confirm(`Bạn có chắc chắn muốn xóa danh mục "${category.name}"?`)) {
      return;
    }
    setPageError(null);
    try {
      await apiRequest(`/api/admin/categories/${category.id}`, {
        method: 'DELETE',
      });
      // Làm mới danh sách danh mục sau khi xóa bản ghi thành công.
      await queryClient.invalidateQueries({ queryKey: ['admin-categories'] });
    } catch (err: unknown) {
      // Hiển thị thông báo lỗi bằng tiếng Việt khi danh mục còn ràng buộc.
      setPageError(toCatalogErrorMessage(err, 'Xóa danh mục thất bại.'));
    }
  }

  // Tìm danh mục cha để lấy danh sách thuộc tính kế thừa hiển thị chỉ đọc.
  const parentCategory = parentId
    ? categories.find((candidate) => candidate.id === parentId)
    : null;
  const inheritedAttributes = parentCategory?.attributes || [];

  // Kiểm tra danh mục gốc hiện tại có danh mục con hay không để khóa chọn cha.
  const hasChildren = editingCategory
    ? (childrenByParent[editingCategory.id] || []).length > 0
    : false;

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Quản lý danh mục</h1>
          <p>Cấu hình cây danh mục hai cấp và bộ thuộc tính kỹ thuật phục vụ thông số sản phẩm.</p>
        </div>

        {/* Chỉ quyền products:write được tạo danh mục. */}
        {canWrite && (
          <button type="button" className="btn btn-primary" onClick={handleOpenCreateRoot}>
            + Thêm danh mục gốc
          </button>
        )}
      </div>

      {/* Lỗi xóa hiện ở mức trang vì hộp thoại đã đóng khi người dùng bấm Xóa. */}
      {pageError && <ErrorAlert message={pageError} />}

      {isLoading && (
        <p role="status" className="empty-state">
          Đang nạp dữ liệu danh mục...
        </p>
      )}

      {isError && (
        <ErrorAlert
          message={error instanceof Error ? error.message : 'Tải danh sách danh mục thất bại.'}
        />
      )}

      {/* Cây danh mục hai cấp */}
      {!isLoading && !isError && (
        <div className="catalog-admin-list category-tree">
          {roots.length === 0 ? (
            <p className="empty-state">Chưa có danh mục nào. Hãy tạo danh mục gốc đầu tiên.</p>
          ) : (
            roots.map((root) => {
              const children = childrenByParent[root.id] || [];

              return (
                <div key={root.id} className="category-node">
                  <div className="category-node-head">
                    <div className="category-node-text">
                      <strong>{root.name}</strong>
                      <span className="cell-sub wrap-anywhere">
                        Vị trí {root.position} · {root.attributes?.length || 0} thuộc tính riêng ·
                        slug: <code>{root.slug}</code>
                      </span>
                    </div>

                    {canWrite && (
                      <div className="row-actions">
                        <button
                          type="button"
                          className="btn btn-sm"
                          aria-label={`Thêm con cho ${root.name}`}
                          onClick={() => handleOpenCreateChild(root)}
                        >
                          + Thêm con
                        </button>
                        <button
                          type="button"
                          className="btn btn-sm"
                          aria-label={`Sửa ${root.name}`}
                          onClick={() => handleOpenEdit(root)}
                        >
                          Sửa
                        </button>
                        <button
                          type="button"
                          className="btn btn-sm btn-danger"
                          aria-label={`Xóa ${root.name}`}
                          onClick={() => handleDelete(root)}
                        >
                          Xóa
                        </button>
                      </div>
                    )}
                  </div>

                  {children.length > 0 && (
                    <div className="category-children">
                      {children.map((child) => (
                        <div
                          key={child.id}
                          data-testid="category-child-card"
                          className="category-child"
                        >
                          <div className="category-node-text">
                            <strong>{child.name}</strong>
                            <span className="cell-sub wrap-anywhere">
                              Vị trí {child.position} · {child.attributes?.length || 0} thuộc tính
                              riêng · slug: <code>{child.slug}</code>
                            </span>
                          </div>

                          {canWrite && (
                            <div className="row-actions">
                              <button
                                type="button"
                                className="btn btn-sm"
                                aria-label={`Sửa ${child.name}`}
                                onClick={() => handleOpenEdit(child)}
                              >
                                Sửa
                              </button>
                              <button
                                type="button"
                                className="btn btn-sm btn-danger"
                                aria-label={`Xóa ${child.name}`}
                                onClick={() => handleDelete(child)}
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
              );
            })
          )}
        </div>
      )}

      {/* Hộp thoại thêm hoặc sửa danh mục dùng khung chung */}
      {canWrite && (
        <AdminFormDialog
          isOpen={isFormOpen}
          onClose={handleCloseForm}
          title={editingCategory ? `Sửa danh mục: ${editingCategory.name}` : 'Thêm danh mục mới'}
          formId="category-form"
          onSubmit={handleSubmit}
          isSubmitting={isSubmitting}
          submitText={editingCategory ? 'Lưu thay đổi' : 'Tạo danh mục'}
          error={formError}
          size="lg"
        >
          <div className="form-grid form-grid-2">
            <div className="field">
              <label htmlFor="category-name">Tên danh mục *</label>
              <input
                id="category-name"
                type="text"
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Điện thoại, Laptop..."
              />
            </div>

            {/* Chỉ danh mục gốc được làm cha, vì cây danh mục giới hạn hai cấp. */}
            <div className="field">
              <label htmlFor="category-parent">Danh mục cha</label>
              <select
                id="category-parent"
                disabled={hasChildren}
                value={parentId}
                onChange={(e) => setParentId(e.target.value)}
              >
                <option value="">[Không có - Danh mục gốc]</option>
                {roots
                  .filter((root) => !editingCategory || root.id !== editingCategory.id)
                  .map((root) => (
                    <option key={root.id} value={root.id}>
                      {root.name}
                    </option>
                  ))}
              </select>
              {hasChildren && <small>Danh mục có con không thể chuyển thành danh mục con.</small>}
            </div>
          </div>

          <div className="field">
            <label htmlFor="category-position">Thứ tự hiển thị (position)</label>
            <input
              id="category-position"
              type="number"
              className="category-position-input"
              value={position}
              onChange={(e) => setPosition(Number(e.target.value) || 0)}
            />
          </div>

          {/* Đường kẻ tách trình soạn thuộc tính khỏi thông tin chung, vì đây là phần dài nhất của form. */}
          <div className="category-attributes">
            <AttributeEditor
              attributes={attributes}
              onChange={setAttributes}
              inheritedAttributes={inheritedAttributes}
              errors={attributeErrors}
            />
          </div>
        </AdminFormDialog>
      )}
    </div>
  );
}
