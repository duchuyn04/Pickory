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
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-4)',
        fontFamily: 'var(--font-sans)',
      }}
    >
      {/* Khối hiển thị thuộc tính kế thừa từ danh mục cha (chỉ đọc) */}
      {inherited.length > 0 && (
        <div
          style={{
            padding: 'var(--space-3)',
            backgroundColor: 'var(--color-surface-muted)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-control)',
          }}
        >
          <div
            style={{
              fontSize: 'var(--text-xs)',
              fontWeight: 700,
              color: 'var(--color-ink-muted)',
              marginBottom: 'var(--space-2)',
              textTransform: 'uppercase',
            }}
          >
            Thuộc tính kế thừa từ danh mục cha (chỉ đọc)
          </div>
          <div
            style={{
              display: 'flex',
              flexDirection: 'column',
              gap: 'var(--space-2)',
            }}
          >
            {inherited.map((attr) => (
              <div
                key={attr.key}
                style={{
                  display: 'flex',
                  flexWrap: 'wrap',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: 'var(--space-2)',
                  padding: 'var(--space-2)',
                  backgroundColor: 'var(--color-surface)',
                  border: '1px solid var(--color-border)',
                  borderRadius: 'var(--radius-control)',
                  fontSize: 'var(--text-sm)',
                }}
              >
                <div>
                  <strong>{attr.label}</strong> (<code>{attr.key}</code>)
                  <span
                    style={{
                      color: 'var(--color-ink-muted)',
                      marginLeft: 'var(--space-2)',
                    }}
                  >
                    Nhóm: {attr.group} | Kiểu: {attr.type}
                    {attr.unit ? ` (${attr.unit})` : ''}
                  </span>
                </div>
                <div style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}>
                  {attr.required ? 'Bắt buộc | ' : ''}
                  {attr.filterable ? 'Lọc được' : 'Không lọc'}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Khối soạn thảo thuộc tính riêng của danh mục */}
      <div
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          justifyContent: 'space-between',
          alignItems: 'center',
          gap: 'var(--space-2)',
        }}
      >
        <div style={{ fontSize: 'var(--text-sm)', fontWeight: 600, color: 'var(--color-ink)' }}>
          Thuộc tính riêng của danh mục
        </div>
        {!readOnly && (
          <button
            type="button"
            onClick={handleAdd}
            style={{
              padding: 'var(--space-1) var(--space-3)',
              backgroundColor: 'var(--color-brand-soft)',
              color: 'var(--color-brand)',
              border: '1px solid var(--color-brand)',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-xs)',
              fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            + Thêm thuộc tính
          </button>
        )}
      </div>

      {own.length === 0 ? (
        <div
          style={{
            padding: 'var(--space-4)',
            textAlign: 'center',
            color: 'var(--color-ink-muted)',
            fontSize: 'var(--text-sm)',
            border: '1px dashed var(--color-border)',
            borderRadius: 'var(--radius-control)',
          }}
        >
          Chưa có thuộc tính riêng nào. Bấm &quot;Thêm thuộc tính&quot; để tạo.
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-3)' }}>
          {own.map((attr, index) => {
            const keyError = getFieldError(index, 'key');
            const labelError = getFieldError(index, 'label');
            const groupError = getFieldError(index, 'group');
            const typeError = getFieldError(index, 'type');
            const optionsError = getFieldError(index, 'options');

            return (
              <div
                key={`attr-${index}`}
                style={{
                  padding: 'var(--space-3)',
                  backgroundColor: 'var(--color-surface)',
                  border: '1px solid var(--color-border)',
                  borderRadius: 'var(--radius-control)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 'var(--space-2)',
                }}
              >
                {/* Cột tối thiểu không vượt 100% để hộp thoại hẹp còn một cột, không tràn ngang. */}
                <div
                  style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 130px), 1fr))',
                    gap: 'var(--space-2)',
                    alignItems: 'start',
                  }}
                >
                  {/* Ô nhập khóa thuộc tính */}
                  <div>
                    <label
                      htmlFor={`attr-${index}-key`}
                      style={{
                        display: 'block',
                        fontSize: 'var(--text-xs)',
                        color: 'var(--color-ink-muted)',
                        marginBottom: 'var(--space-1)',
                      }}
                    >
                      Khóa (key) *
                    </label>
                    <input
                      id={`attr-${index}-key`}
                      type="text"
                      disabled={readOnly}
                      value={attr.key}
                      onChange={(e) => handleChangeField(index, 'key', e.target.value)}
                      placeholder="ram_gb"
                      aria-describedby={keyError ? `attr-error-${index}-key` : undefined}
                      aria-invalid={keyError ? true : undefined}
                      style={{
                        width: '100%',
                        padding: 'var(--space-1) var(--space-2)',
                        borderRadius: 'var(--radius-control)',
                        border: `1px solid ${keyError ? 'var(--color-danger)' : 'var(--color-border)'}`,
                        fontSize: 'var(--text-sm)',
                        backgroundColor: 'var(--color-surface)',
                        color: 'var(--color-ink)',
                      }}
                    />
                    {keyError && (
                      <div
                        id={`attr-error-${index}-key`}
                        role="alert"
                        style={{
                          color: 'var(--color-danger)',
                          fontSize: 'var(--text-xs)',
                          marginTop: 'var(--space-1)',
                        }}
                      >
                        {keyError}
                      </div>
                    )}
                  </div>

                  {/* Ô nhập nhãn hiển thị */}
                  <div>
                    <label
                      htmlFor={`attr-${index}-label`}
                      style={{
                        display: 'block',
                        fontSize: 'var(--text-xs)',
                        color: 'var(--color-ink-muted)',
                        marginBottom: 'var(--space-1)',
                      }}
                    >
                      Nhãn *
                    </label>
                    <input
                      id={`attr-${index}-label`}
                      type="text"
                      disabled={readOnly}
                      value={attr.label}
                      onChange={(e) => handleChangeField(index, 'label', e.target.value)}
                      placeholder="Dung lượng RAM"
                      aria-describedby={labelError ? `attr-error-${index}-label` : undefined}
                      aria-invalid={labelError ? true : undefined}
                      style={{
                        width: '100%',
                        padding: 'var(--space-1) var(--space-2)',
                        borderRadius: 'var(--radius-control)',
                        border: `1px solid ${labelError ? 'var(--color-danger)' : 'var(--color-border)'}`,
                        fontSize: 'var(--text-sm)',
                        backgroundColor: 'var(--color-surface)',
                        color: 'var(--color-ink)',
                      }}
                    />
                    {labelError && (
                      <div
                        id={`attr-error-${index}-label`}
                        role="alert"
                        style={{
                          color: 'var(--color-danger)',
                          fontSize: 'var(--text-xs)',
                          marginTop: 'var(--space-1)',
                        }}
                      >
                        {labelError}
                      </div>
                    )}
                  </div>

                  {/* Ô nhập nhóm thuộc tính */}
                  <div>
                    <label
                      htmlFor={`attr-${index}-group`}
                      style={{
                        display: 'block',
                        fontSize: 'var(--text-xs)',
                        color: 'var(--color-ink-muted)',
                        marginBottom: 'var(--space-1)',
                      }}
                    >
                      Nhóm *
                    </label>
                    <input
                      id={`attr-${index}-group`}
                      type="text"
                      disabled={readOnly}
                      value={attr.group}
                      onChange={(e) => handleChangeField(index, 'group', e.target.value)}
                      placeholder="Cấu hình"
                      aria-describedby={groupError ? `attr-error-${index}-group` : undefined}
                      aria-invalid={groupError ? true : undefined}
                      style={{
                        width: '100%',
                        padding: 'var(--space-1) var(--space-2)',
                        borderRadius: 'var(--radius-control)',
                        border: `1px solid ${groupError ? 'var(--color-danger)' : 'var(--color-border)'}`,
                        fontSize: 'var(--text-sm)',
                        backgroundColor: 'var(--color-surface)',
                        color: 'var(--color-ink)',
                      }}
                    />
                    {groupError && (
                      <div
                        id={`attr-error-${index}-group`}
                        role="alert"
                        style={{
                          color: 'var(--color-danger)',
                          fontSize: 'var(--text-xs)',
                          marginTop: 'var(--space-1)',
                        }}
                      >
                        {groupError}
                      </div>
                    )}
                  </div>

                  {/* Lựa chọn kiểu dữ liệu */}
                  <div>
                    <label
                      htmlFor={`attr-${index}-type`}
                      style={{
                        display: 'block',
                        fontSize: 'var(--text-xs)',
                        color: 'var(--color-ink-muted)',
                        marginBottom: 'var(--space-1)',
                      }}
                    >
                      Kiểu *
                    </label>
                    <select
                      id={`attr-${index}-type`}
                      disabled={readOnly}
                      value={attr.type}
                      onChange={(e) =>
                        handleChangeField(index, 'type', e.target.value as AttributeType)
                      }
                      aria-describedby={typeError ? `attr-error-${index}-type` : undefined}
                      aria-invalid={typeError ? true : undefined}
                      style={{
                        width: '100%',
                        padding: 'var(--space-1) var(--space-2)',
                        borderRadius: 'var(--radius-control)',
                        border: `1px solid ${typeError ? 'var(--color-danger)' : 'var(--color-border)'}`,
                        fontSize: 'var(--text-sm)',
                        backgroundColor: 'var(--color-surface)',
                        color: 'var(--color-ink)',
                      }}
                    >
                      <option value="text">text</option>
                      <option value="number">number</option>
                      <option value="enum">enum</option>
                      <option value="boolean">boolean</option>
                    </select>
                    {typeError && (
                      <div
                        id={`attr-error-${index}-type`}
                        role="alert"
                        style={{
                          color: 'var(--color-danger)',
                          fontSize: 'var(--text-xs)',
                          marginTop: 'var(--space-1)',
                        }}
                      >
                        {typeError}
                      </div>
                    )}
                  </div>

                  {/* Đơn vị tính */}
                  <div>
                    <label
                      htmlFor={`attr-${index}-unit`}
                      style={{
                        display: 'block',
                        fontSize: 'var(--text-xs)',
                        color: 'var(--color-ink-muted)',
                        marginBottom: 'var(--space-1)',
                      }}
                    >
                      Đơn vị
                    </label>
                    <input
                      id={`attr-${index}-unit`}
                      type="text"
                      disabled={readOnly}
                      value={attr.unit || ''}
                      onChange={(e) => handleChangeField(index, 'unit', e.target.value)}
                      placeholder="GB, inch"
                      style={{
                        width: '100%',
                        padding: 'var(--space-1) var(--space-2)',
                        borderRadius: 'var(--radius-control)',
                        border: '1px solid var(--color-border)',
                        fontSize: 'var(--text-sm)',
                        backgroundColor: 'var(--color-surface)',
                        color: 'var(--color-ink)',
                      }}
                    />
                  </div>
                </div>

                {/* Các tùy chọn dạng checkbox và danh sách options khi là kiểu enum */}
                <div
                  style={{
                    display: 'flex',
                    flexWrap: 'wrap',
                    alignItems: 'center',
                    gap: 'var(--space-4)',
                    paddingTop: 'var(--space-1)',
                  }}
                >
                  <label
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 'var(--space-1)',
                      fontSize: 'var(--text-xs)',
                      cursor: readOnly ? 'default' : 'pointer',
                    }}
                  >
                    <input
                      type="checkbox"
                      disabled={readOnly}
                      checked={attr.required}
                      onChange={(e) => handleChangeField(index, 'required', e.target.checked)}
                    />
                    Bắt buộc
                  </label>

                  <label
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 'var(--space-1)',
                      fontSize: 'var(--text-xs)',
                      cursor: readOnly ? 'default' : 'pointer',
                    }}
                  >
                    <input
                      type="checkbox"
                      disabled={readOnly}
                      checked={attr.filterable}
                      onChange={(e) => handleChangeField(index, 'filterable', e.target.checked)}
                    />
                    Lọc được
                  </label>

                  {attr.type === 'enum' && (
                    <div style={{ flex: '1 1 100%', minWidth: 0 }}>
                      <label
                        htmlFor={`attr-${index}-options`}
                        style={{
                          display: 'block',
                          fontSize: 'var(--text-xs)',
                          color: 'var(--color-ink-muted)',
                          marginBottom: 'var(--space-1)',
                        }}
                      >
                        Danh sách options (cách nhau bằng dấu phẩy) *
                      </label>
                      <input
                        id={`attr-${index}-options`}
                        type="text"
                        disabled={readOnly}
                        value={rawOptionsMap[index] ?? attr.options?.join(', ') ?? ''}
                        onChange={(e) => {
                          const val = e.target.value;
                          setRawOptionsMap((prev) => ({ ...prev, [index]: val }));
                          handleChangeField(index, 'options', parseEnumOptions(val));
                        }}
                        placeholder="8GB, 16GB, 32GB"
                        aria-describedby={optionsError ? `attr-error-${index}-options` : undefined}
                        aria-invalid={optionsError ? true : undefined}
                        style={{
                          width: '100%',
                          padding: 'var(--space-1) var(--space-2)',
                          borderRadius: 'var(--radius-control)',
                          border: `1px solid ${optionsError ? 'var(--color-danger)' : 'var(--color-border)'}`,
                          fontSize: 'var(--text-sm)',
                          backgroundColor: 'var(--color-surface)',
                          color: 'var(--color-ink)',
                        }}
                      />
                      {optionsError && (
                        <div
                          id={`attr-error-${index}-options`}
                          role="alert"
                          style={{
                            color: 'var(--color-danger)',
                            fontSize: 'var(--text-xs)',
                            marginTop: 'var(--space-1)',
                          }}
                        >
                          {optionsError}
                        </div>
                      )}
                    </div>
                  )}
                </div>

                {/* Nút Xóa nằm trên hàng riêng. Lưới ô nhập không phải chừa cột cố định. */}
                {!readOnly && (
                  <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                    <button
                      type="button"
                      onClick={() => handleRemove(index)}
                      aria-label={`Xóa thuộc tính ${attr.label || attr.key || index + 1}`}
                      style={{
                        padding: 'var(--space-1) var(--space-2)',
                        backgroundColor: 'transparent',
                        color: 'var(--color-danger)',
                        border: '1px solid var(--color-border)',
                        borderRadius: 'var(--radius-control)',
                        cursor: 'pointer',
                        fontSize: 'var(--text-xs)',
                      }}
                    >
                      Xóa
                    </button>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
