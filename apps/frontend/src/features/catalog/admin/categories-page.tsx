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
            Quản lý danh mục
          </h1>
          <p style={{ margin: 0, color: 'var(--color-ink-muted)', fontSize: 'var(--text-sm)' }}>
            Cấu hình cây danh mục hai cấp và bộ thuộc tính kỹ thuật phục vụ thông số sản phẩm.
          </p>
        </div>

        {canWrite && (
          <button
            type="button"
            onClick={handleOpenCreateRoot}
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
            + Thêm danh mục gốc
          </button>
        )}
      </div>

      {/* Thông báo lỗi ở mức trang khi thao tác xóa danh mục thất bại */}
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

      {/* Trạng thái tải dữ liệu và hiển thị lỗi kết nối */}
      {isLoading && (
        <div
          style={{
            padding: 'var(--space-6)',
            textAlign: 'center',
            color: 'var(--color-ink-muted)',
          }}
        >
          Đang nạp dữ liệu danh mục...
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
          {error instanceof Error ? error.message : 'Tải danh sách danh mục thất bại.'}
        </div>
      )}

      {/* Cây danh mục hai cấp */}
      {!isLoading && !isError && (
        <div
          className="catalog-admin-list"
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--space-4)',
          }}
        >
          {roots.length === 0 ? (
            <div
              style={{
                padding: 'var(--space-6)',
                textAlign: 'center',
                color: 'var(--color-ink-muted)',
                fontSize: 'var(--text-sm)',
              }}
            >
              Chưa có danh mục nào. Hãy tạo danh mục gốc đầu tiên.
            </div>
          ) : (
            roots.map((root) => {
              const children = childrenByParent[root.id] || [];

              return (
                <div
                  key={root.id}
                  style={{
                    border: '1px solid var(--color-border)',
                    borderRadius: 'var(--radius-control)',
                    overflow: 'hidden',
                  }}
                >
                  {/* Dòng danh mục gốc */}
                  <div
                    style={{
                      padding: 'var(--space-3) var(--space-4)',
                      backgroundColor: 'var(--color-surface-muted)',
                      display: 'flex',
                      flexWrap: 'wrap',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      gap: 'var(--space-2)',
                      borderBottom: children.length > 0 ? '1px solid var(--color-border)' : 'none',
                    }}
                  >
                    <div
                      style={{
                        display: 'flex',
                        flexDirection: 'column',
                        gap: 'var(--space-1)',
                      }}
                    >
                      <strong style={{ fontSize: 'var(--text-md)', color: 'var(--color-ink)' }}>
                        {root.name}
                      </strong>
                      <span style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}>
                        Vị trí {root.position} · {root.attributes?.length || 0} thuộc tính riêng ·
                        slug: <code>{root.slug}</code>
                      </span>
                    </div>

                    {canWrite && (
                      <div style={{ display: 'flex', gap: 'var(--space-2)', flexWrap: 'wrap' }}>
                        <button
                          type="button"
                          aria-label={`Thêm con cho ${root.name}`}
                          onClick={() => handleOpenCreateChild(root)}
                          style={{
                            padding: 'var(--space-1) var(--space-3)',
                            backgroundColor: 'var(--color-surface)',
                            border: '1px solid var(--color-border)',
                            borderRadius: 'var(--radius-control)',
                            fontSize: 'var(--text-xs)',
                            cursor: 'pointer',
                          }}
                        >
                          + Thêm con
                        </button>
                        <button
                          type="button"
                          aria-label={`Sửa ${root.name}`}
                          onClick={() => handleOpenEdit(root)}
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
                          aria-label={`Xóa ${root.name}`}
                          onClick={() => handleDelete(root)}
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

                  {/* Danh sách các danh mục con trực thuộc với độ thụt lề chuẩn responsive */}
                  {children.length > 0 && (
                    <div
                      style={{
                        padding: 'var(--space-2) var(--space-4) var(--space-2) var(--space-4)',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: 'var(--space-2)',
                        backgroundColor: 'var(--color-surface)',
                      }}
                    >
                      {children.map((child) => (
                        <div
                          key={child.id}
                          data-testid="category-child-card"
                          style={{
                            padding: 'var(--space-2) var(--space-3)',
                            border: '1px solid var(--color-border)',
                            borderRadius: 'var(--radius-control)',
                            display: 'flex',
                            flexWrap: 'wrap',
                            justifyContent: 'space-between',
                            alignItems: 'center',
                            gap: 'var(--space-2)',
                          }}
                        >
                          <div
                            style={{
                              display: 'flex',
                              flexDirection: 'column',
                              gap: 'var(--space-1)',
                            }}
                          >
                            <span
                              style={{
                                fontSize: 'var(--text-sm)',
                                fontWeight: 600,
                                color: 'var(--color-ink)',
                              }}
                            >
                              {child.name}
                            </span>
                            <span
                              style={{
                                fontSize: 'var(--text-xs)',
                                color: 'var(--color-ink-muted)',
                              }}
                            >
                              Vị trí {child.position} · {child.attributes?.length || 0} thuộc tính
                              riêng · slug: <code>{child.slug}</code>
                            </span>
                          </div>

                          {canWrite && (
                            <div
                              style={{ display: 'flex', gap: 'var(--space-2)', flexWrap: 'wrap' }}
                            >
                              <button
                                type="button"
                                aria-label={`Sửa ${child.name}`}
                                onClick={() => handleOpenEdit(child)}
                                style={{
                                  padding: 'var(--space-1) var(--space-2)',
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
                                aria-label={`Xóa ${child.name}`}
                                onClick={() => handleDelete(child)}
                                style={{
                                  padding: 'var(--space-1) var(--space-2)',
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
          maxWidth={860}
        >
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-4)' }}>
            {/* Ô nhập tên danh mục */}
            <div>
              <label
                htmlFor="category-name"
                style={{
                  display: 'block',
                  fontSize: 'var(--text-sm)',
                  fontWeight: 600,
                  color: 'var(--color-ink)',
                  marginBottom: 'var(--space-1)',
                }}
              >
                Tên danh mục *
              </label>
              <input
                id="category-name"
                type="text"
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Điện thoại, Laptop..."
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

            {/* Lựa chọn danh mục cha (chỉ danh mục gốc được phép làm cha) */}
            <div>
              <label
                htmlFor="category-parent"
                style={{
                  display: 'block',
                  fontSize: 'var(--text-sm)',
                  fontWeight: 600,
                  color: 'var(--color-ink)',
                  marginBottom: 'var(--space-1)',
                }}
              >
                Danh mục cha
              </label>
              <select
                id="category-parent"
                disabled={hasChildren}
                value={parentId}
                onChange={(e) => setParentId(e.target.value)}
                style={{
                  width: '100%',
                  padding: 'var(--space-2) var(--space-3)',
                  borderRadius: 'var(--radius-control)',
                  border: '1px solid var(--color-border)',
                  fontSize: 'var(--text-sm)',
                  backgroundColor: 'var(--color-surface)',
                  color: 'var(--color-ink)',
                }}
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
              {hasChildren && (
                <div
                  style={{
                    fontSize: 'var(--text-xs)',
                    color: 'var(--color-ink-muted)',
                    marginTop: 'var(--space-1)',
                  }}
                >
                  Danh mục có con không thể chuyển thành danh mục con.
                </div>
              )}
            </div>
          </div>

          {/* Thứ tự hiển thị */}
          <div>
            <label
              htmlFor="category-position"
              style={{
                display: 'block',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                color: 'var(--color-ink)',
                marginBottom: 'var(--space-1)',
              }}
            >
              Thứ tự hiển thị (position)
            </label>
            <input
              id="category-position"
              type="number"
              value={position}
              onChange={(e) => setPosition(Number(e.target.value) || 0)}
              style={{
                width: 140,
                padding: 'var(--space-2) var(--space-3)',
                borderRadius: 'var(--radius-control)',
                border: '1px solid var(--color-border)',
                fontSize: 'var(--text-sm)',
                backgroundColor: 'var(--color-surface)',
                color: 'var(--color-ink)',
              }}
            />
          </div>

          {/* Trình soạn thảo danh sách thuộc tính */}
          <div
            style={{
              borderTop: '1px solid var(--color-border)',
              paddingTop: 'var(--space-4)',
            }}
          >
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
