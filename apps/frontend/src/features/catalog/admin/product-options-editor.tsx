import { useState } from 'react';
import { MAX_AXES, type ProductOption } from './product-logic';

interface ProductOptionsEditorProps {
  options: ProductOption[];
  disabled: boolean;
  onChange: (options: ProductOption[]) => void;
}

export function ProductOptionsEditor({ options, disabled, onChange }: ProductOptionsEditorProps) {
  // Giữ văn bản gốc để khoảng trắng vừa gõ không biến mất trước ký tự tiếp theo.
  const [valueDrafts, setValueDrafts] = useState(() =>
    options.map((option) => option.values.join(', ')),
  );
  // Chỉ thay trục đang soạn để các tổ hợp ở trục khác giữ nguyên dữ liệu.
  function changeOption(index: number, option: ProductOption) {
    onChange(options.map((current, position) => (position === index ? option : current)));
  }

  return (
    <div className="product-section">
      <h2>Tùy chọn</h2>
      <p>Thêm tối đa {MAX_AXES} trục. Phân cách các giá trị bằng dấu phẩy.</p>
      {options.map((option, index) => (
        <div className="product-option" key={index}>
          <div className="product-field">
            <label htmlFor={`option-name-${index}`}>Tên trục {index + 1}</label>
            <input
              id={`option-name-${index}`}
              name={`option-name-${index}`}
              disabled={disabled}
              value={option.name}
              onChange={(event) => changeOption(index, { ...option, name: event.target.value })}
            />
          </div>
          <div className="product-field">
            <label htmlFor={`option-values-${index}`}>Giá trị trục {index + 1}</label>
            <input
              id={`option-values-${index}`}
              name={`option-values-${index}`}
              disabled={disabled}
              value={valueDrafts[index] ?? option.values.join(', ')}
              onChange={(event) => {
                // Giữ dấu cách đang nhập trong bản nháp trước khi sinh tên tổ hợp.
                const draft = event.target.value;
                setValueDrafts((current) => {
                  const next = [...current];
                  next[index] = draft;
                  return next;
                });
                // Chỉ tách theo dấu phẩy; hàm sinh tổ hợp chuẩn hóa giá trị sau khi nhập.
                changeOption(index, { ...option, values: draft.split(',') });
              }}
            />
          </div>
          {!disabled && (
            <button
              type="button"
              className="product-button-secondary"
              onClick={() => {
                // Dịch bản nháp cùng trục còn lại để không gắn giá trị cũ sang ô khác.
                setValueDrafts((current) => current.filter((_, position) => position !== index));
                onChange(options.filter((_, position) => position !== index));
              }}
            >
              Bỏ trục {index + 1}
            </button>
          )}
        </div>
      ))}
      {/* Giới hạn trục trước khi thêm để số tổ hợp không tăng ngoài mức API cho phép. */}
      {!disabled && options.length < MAX_AXES && (
        <button
          name="add-option"
          className="product-button-secondary"
          type="button"
          onClick={() => onChange([...options, { name: '', values: [''] }])}
        >
          Thêm trục
        </button>
      )}
    </div>
  );
}
