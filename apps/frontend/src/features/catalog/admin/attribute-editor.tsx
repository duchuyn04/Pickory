import React from 'react';
import type {
  AttributeDefinition,
  AttributeDefinitionError,
  AttributeType,
} from '../../../shared/catalog/attributes';
import { parseEnumOptions, splitCategoryAttributes } from './attribute-helpers';

// Thuộc tính đầu vào cho trình biên tập danh sách thuộc tính của danh mục.
export interface AttributeEditorProps {
  attributes: AttributeDefinition[];
  onChange: (attributes: AttributeDefinition[]) => void;
  inheritedAttributes?: AttributeDefinition[];
  errors?: AttributeDefinitionError[];
  readOnly?: boolean;
}

// Trình soạn thảo và quản lý các thuộc tính kỹ thuật của danh mục sản phẩm.
export function AttributeEditor({
  attributes,
  onChange,
  inheritedAttributes,
  errors = [],
  readOnly = false,
}: AttributeEditorProps) {
  // Tách biệt nhóm thuộc tính kế thừa của cha để luôn giữ ở chế độ chỉ đọc.
  const { inherited, own } = splitCategoryAttributes(inheritedAttributes, attributes);
  // Lưu chuỗi thô của từng ô options để người dùng gõ dấu phẩy không bị nuốt ký tự.
  const [rawOptionsMap, setRawOptionsMap] = React.useState<Record<number, string>>({});

  // Thêm một thuộc tính mới với giá trị khởi tạo mặc định.
  function handleAdd() {
    // Không cho phép thêm dòng mới khi component đang ở trạng thái chỉ đọc.
    if (readOnly) return;
    const newAttribute: AttributeDefinition = {
      key: '',
      label: '',
      group: '',
      type: 'text',
      unit: '',
      options: [],
      required: false,
      filterable: false,
    };
    onChange([...own, newAttribute]);
  }

  // Cập nhật giá trị một trường của thuộc tính tại vị trí chỉ định.
  function handleChangeField(index: number, field: keyof AttributeDefinition, value: unknown) {
    // Ngăn chặn việc sửa đổi dữ liệu khi component bị khóa thao tác.
    if (readOnly) return;
    const updated = own.map((attr, i) => {
      if (i !== index) return attr;
      return { ...attr, [field]: value };
    });
    onChange(updated);
  }

  // Xóa một thuộc tính riêng khỏi danh sách định nghĩa và dọn chuỗi thô tương ứng.
  function handleRemove(index: number) {
    // Không cho phép xóa dòng thuộc tính khi ở chế độ chỉ xem.
    if (readOnly) return;
    setRawOptionsMap((prev) => {
      const next: Record<number, string> = {};
      for (const [key, val] of Object.entries(prev)) {
        const k = Number(key);
        if (k < index) next[k] = val;
        else if (k > index) next[k - 1] = val;
      }
      return next;
    });
    onChange(own.filter((_, i) => i !== index));
  }

  // Tìm kiếm thông báo lỗi chính xác theo chỉ số dòng và tên trường.
  function getFieldError(
    index: number,
    field: 'key' | 'label' | 'group' | 'type' | 'options',
  ): string | undefined {
    return errors.find((err) => err.index === index && err.field === field)?.message;
  }

  return (
    <div className="stack attribute-editor">
      {/* Khối hiển thị thuộc tính kế thừa từ danh mục cha (chỉ đọc) */}
      {inherited.length > 0 && (
        <div className="attribute-inherited">
          <p className="attribute-inherited-title">Thuộc tính kế thừa từ danh mục cha (chỉ đọc)</p>
          <div className="stack-sm">
            {inherited.map((attr) => (
              <div key={attr.key} className="attribute-inherited-item">
                <div>
                  <strong>{attr.label}</strong> (<code>{attr.key}</code>)
                  <span className="muted attribute-inherited-meta">
                    Nhóm: {attr.group} | Kiểu: {attr.type}
                    {attr.unit ? ` (${attr.unit})` : ''}
                  </span>
                </div>
                <span className="cell-sub">
                  {attr.required ? 'Bắt buộc | ' : ''}
                  {attr.filterable ? 'Lọc được' : 'Không lọc'}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Khối soạn thảo thuộc tính riêng của danh mục */}
      <div className="attribute-own-head">
        <h3>Thuộc tính riêng của danh mục</h3>
        {!readOnly && (
          <button type="button" className="btn btn-sm" onClick={handleAdd}>
            + Thêm thuộc tính
          </button>
        )}
      </div>

      {own.length === 0 ? (
        <p className="empty-state empty-state-dashed">
          Chưa có thuộc tính riêng nào. Bấm &quot;Thêm thuộc tính&quot; để tạo.
        </p>
      ) : (
        <div className="stack-sm">
          {own.map((attr, index) => (
            <div key={`attr-${index}`} className="attribute-card">
              {/* Cột tối thiểu không vượt 100% để hộp thoại hẹp còn một cột, không tràn ngang. */}
              <div className="attribute-grid">
                <AttributeField
                  id={`attr-${index}-key`}
                  label="Khóa (key) *"
                  error={getFieldError(index, 'key')}
                >
                  {(fieldProps) => (
                    <input
                      {...fieldProps}
                      type="text"
                      disabled={readOnly}
                      value={attr.key}
                      onChange={(e) => handleChangeField(index, 'key', e.target.value)}
                      placeholder="ram_gb"
                    />
                  )}
                </AttributeField>

                <AttributeField
                  id={`attr-${index}-label`}
                  label="Nhãn *"
                  error={getFieldError(index, 'label')}
                >
                  {(fieldProps) => (
                    <input
                      {...fieldProps}
                      type="text"
                      disabled={readOnly}
                      value={attr.label}
                      onChange={(e) => handleChangeField(index, 'label', e.target.value)}
                      placeholder="Dung lượng RAM"
                    />
                  )}
                </AttributeField>

                <AttributeField
                  id={`attr-${index}-group`}
                  label="Nhóm *"
                  error={getFieldError(index, 'group')}
                >
                  {(fieldProps) => (
                    <input
                      {...fieldProps}
                      type="text"
                      disabled={readOnly}
                      value={attr.group}
                      onChange={(e) => handleChangeField(index, 'group', e.target.value)}
                      placeholder="Cấu hình"
                    />
                  )}
                </AttributeField>

                <AttributeField
                  id={`attr-${index}-type`}
                  label="Kiểu *"
                  error={getFieldError(index, 'type')}
                >
                  {(fieldProps) => (
                    <select
                      {...fieldProps}
                      disabled={readOnly}
                      value={attr.type}
                      onChange={(e) =>
                        handleChangeField(index, 'type', e.target.value as AttributeType)
                      }
                    >
                      <option value="text">text</option>
                      <option value="number">number</option>
                      <option value="enum">enum</option>
                      <option value="boolean">boolean</option>
                    </select>
                  )}
                </AttributeField>

                <AttributeField id={`attr-${index}-unit`} label="Đơn vị">
                  {(fieldProps) => (
                    <input
                      {...fieldProps}
                      type="text"
                      disabled={readOnly}
                      value={attr.unit || ''}
                      onChange={(e) => handleChangeField(index, 'unit', e.target.value)}
                      placeholder="GB, inch"
                    />
                  )}
                </AttributeField>
              </div>

              {/* Các tùy chọn dạng checkbox và danh sách options khi là kiểu enum */}
              <div className="attribute-flags">
                <label className="check">
                  <input
                    type="checkbox"
                    disabled={readOnly}
                    checked={attr.required}
                    onChange={(e) => handleChangeField(index, 'required', e.target.checked)}
                  />
                  Bắt buộc
                </label>

                <label className="check">
                  <input
                    type="checkbox"
                    disabled={readOnly}
                    checked={attr.filterable}
                    onChange={(e) => handleChangeField(index, 'filterable', e.target.checked)}
                  />
                  Lọc được
                </label>

                {attr.type === 'enum' && (
                  <div className="attribute-options">
                    <AttributeField
                      id={`attr-${index}-options`}
                      label="Danh sách options (cách nhau bằng dấu phẩy) *"
                      error={getFieldError(index, 'options')}
                    >
                      {(fieldProps) => (
                        <input
                          {...fieldProps}
                          type="text"
                          disabled={readOnly}
                          value={rawOptionsMap[index] ?? attr.options?.join(', ') ?? ''}
                          onChange={(e) => {
                            const val = e.target.value;
                            setRawOptionsMap((prev) => ({ ...prev, [index]: val }));
                            handleChangeField(index, 'options', parseEnumOptions(val));
                          }}
                          placeholder="8GB, 16GB, 32GB"
                        />
                      )}
                    </AttributeField>
                  </div>
                )}
              </div>

              {/* Nút Xóa nằm trên hàng riêng. Lưới ô nhập không phải chừa cột cố định. */}
              {!readOnly && (
                <div className="attribute-card-foot">
                  <button
                    type="button"
                    className="btn btn-sm btn-danger"
                    onClick={() => handleRemove(index)}
                    aria-label={`Xóa thuộc tính ${attr.label || attr.key || index + 1}`}
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
}

interface AttributeFieldControlProps {
  id: string;
  'aria-describedby'?: string;
  'aria-invalid'?: true;
}

interface AttributeFieldProps {
  id: string;
  label: string;
  error?: string;
  children: (controlProps: AttributeFieldControlProps) => React.ReactNode;
}

// Một ô của thuộc tính: nhãn, ô nhập và lỗi ngay dưới ô. Gom lại để mọi ô nối lỗi theo cùng một cách.
function AttributeField({ id, label, error, children }: AttributeFieldProps) {
  const errorId = `${id.replace(/^attr-/, 'attr-error-')}`;
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {/* Ô sai nhận `aria-invalid` và trỏ tới lỗi, để trình đọc màn hình đọc lỗi khi focus vào ô. */}
      {children({
        id,
        'aria-describedby': error ? errorId : undefined,
        'aria-invalid': error ? true : undefined,
      })}
      {error && (
        <span id={errorId} role="alert" className="form-error">
          {error}
        </span>
      )}
    </div>
  );
}
