import type { ProductOptionInfo, ProductSkuInfo } from './catalog-api';
import {
  isOptionValueAvailable,
  isOptionValueCombinable,
  type OptionSelection,
} from './variant-selection';

interface VariantPickerProps {
  options: ProductOptionInfo[];
  skus: ProductSkuInfo[];
  selection: OptionSelection;
  // Khi true, bộ chọn chỉ để xem: sản phẩm ngừng kinh doanh không cho đổi biến thể.
  readOnly: boolean;
  onSelect: (optionName: string, value: string) => void;
}

export function VariantPicker({
  options,
  skus,
  selection,
  readOnly,
  onSelect,
}: VariantPickerProps) {
  return (
    <div className="variant-picker" data-readonly={readOnly || undefined}>
      {options.map((option) => (
        // Mỗi tùy chọn là một nhóm radio gốc, nên phím mũi tên đổi giá trị mà không cần mã riêng.
        <fieldset key={option.name} className="variant-group">
          <legend>{option.name}</legend>
          <div className="variant-choices">
            {option.values.map((value) => {
              const isAvailable = isOptionValueAvailable(skus, option.name, value);
              const isSelected = selection[option.name] === value;
              // Giá trị có SKU nhưng không ghép được với lựa chọn hiện tại vẫn bấm được và kéo theo lựa chọn khác đổi.
              const switchesOtherOptions =
                isAvailable &&
                !isSelected &&
                !isOptionValueCombinable(skus, selection, option.name, value);
              return (
                <label
                  key={value}
                  className={
                    switchesOtherOptions ? 'variant-choice is-switching' : 'variant-choice'
                  }
                >
                  <input
                    type="radio"
                    name={`option-${option.name}`}
                    value={value}
                    checked={isSelected}
                    // Chỉ tắt giá trị không có SKU nào, để khách không chọn vào giá trị không bán.
                    disabled={readOnly || !isAvailable}
                    onChange={() => onSelect(option.name, value)}
                  />
                  <span>{value}</span>
                  {switchesOtherOptions && !readOnly && (
                    // Trình đọc màn hình cần biết lựa chọn khác sẽ đổi theo, vì mắt thấy qua viền nét đứt.
                    <span className="sr"> (đổi cả lựa chọn khác)</span>
                  )}
                </label>
              );
            })}
          </div>
        </fieldset>
      ))}
    </div>
  );
}
