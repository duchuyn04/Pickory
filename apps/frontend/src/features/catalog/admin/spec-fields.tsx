import type { AttributeDefinition } from '../../../shared/catalog/attributes';

interface SpecFieldsProps {
  attributes: AttributeDefinition[];
  values: Record<string, unknown>;
  errors: Record<string, string>;
  disabled: boolean;
  onChange: (key: string, value: unknown) => void;
}

export function SpecFields({ attributes, values, errors, disabled, onChange }: SpecFieldsProps) {
  // Danh mục không có thuộc tính thì không tạo ô nhập hoặc báo lỗi thiếu thông số.
  if (attributes.length === 0) return <p>Danh mục chưa có thông số.</p>;

  return (
    <div className="product-fields">
      {attributes.map((attribute) => {
        const inputId = `spec-${attribute.key}`;
        const errorId = `${inputId}-error`;
        const error = errors[attribute.key];
        return (
          <div className="product-field" key={attribute.key}>
            <label htmlFor={inputId}>
              {attribute.label}
              {attribute.unit ? ` (${attribute.unit})` : ''}
              {attribute.required ? ' *' : ''}
            </label>
            {/* Thuộc tính boolean giữ trạng thái chưa chọn khác với giá trị false. */}
            {attribute.type === 'boolean' ? (
              <select
                id={inputId}
                name={inputId}
                disabled={disabled}
                value={values[attribute.key] === undefined ? '' : String(values[attribute.key])}
                aria-invalid={!!error}
                aria-describedby={error ? errorId : undefined}
                onChange={(event) => {
                  // Chuỗi rỗng biểu thị chưa chọn, còn chuỗi true phải thành boolean cho API.
                  onChange(
                    attribute.key,
                    event.target.value === '' ? undefined : event.target.value === 'true',
                  );
                }}
              >
                <option value="">Chưa chọn</option>
                <option value="true">Có</option>
                <option value="false">Không</option>
              </select>
            ) : attribute.type === 'enum' ? (
              <select
                id={inputId}
                name={inputId}
                disabled={disabled}
                value={String(values[attribute.key] ?? '')}
                aria-invalid={!!error}
                aria-describedby={error ? errorId : undefined}
                onChange={(event) => {
                  // Bỏ khóa của giá trị enum rỗng để không gửi thông số không được chọn.
                  onChange(attribute.key, event.target.value || undefined);
                }}
              >
                <option value="">Chọn giá trị</option>
                {attribute.options?.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            ) : (
              <input
                id={inputId}
                name={inputId}
                type={attribute.type === 'number' ? 'number' : 'text'}
                disabled={disabled}
                value={String(values[attribute.key] ?? '')}
                aria-invalid={!!error}
                aria-describedby={error ? errorId : undefined}
                onChange={(event) => {
                  // Ô số phải gửi number, ô trống bỏ khóa để kiểm tra bắt buộc báo đúng lỗi.
                  onChange(
                    attribute.key,
                    attribute.type === 'number'
                      ? event.target.value === ''
                        ? undefined
                        : Number(event.target.value)
                      : event.target.value || undefined,
                  );
                }}
              />
            )}
            {/* Liên kết lỗi với ô nhập giúp trình đọc màn hình nêu đúng thông số sai. */}
            {error && (
              <span id={errorId} className="product-error">
                {error}
              </span>
            )}
          </div>
        );
      })}
    </div>
  );
}
