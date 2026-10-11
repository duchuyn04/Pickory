import { useEffect, useState, type ChangeEvent, type KeyboardEvent, type ReactNode } from 'react';
import { FieldError, fieldErrorProps } from '../../shared/field-error';
import type { AddressOption } from './address-api';
import { filterAddressOptions } from './address-search';
import './address-picker.css';

interface AddressComboboxProps {
  // Id của ô nhập. Listbox, dòng và lỗi dựng id của chúng từ chuỗi này.
  id: string;
  label: string;
  options: AddressOption[];
  selected: AddressOption | null;
  onSelect: (option: AddressOption) => void;
  disabled?: boolean;
  // Đang tải thì danh sách không có dòng nào để chọn, kể cả khi `options` còn dữ liệu cũ.
  isLoading?: boolean;
  errorMessage?: string | null;
  placeholder?: string;
  // Chỗ cho thông báo nằm giữa ô và lỗi nhập, ví dụ lỗi tải danh sách kèm nút "Thử lại".
  statusSlot?: ReactNode;
}

// Ô chọn có lọc theo mẫu "editable combobox with list autocomplete" của WAI-ARIA.
export function AddressCombobox({
  id,
  label,
  options,
  selected,
  onSelect,
  disabled = false,
  isLoading = false,
  errorMessage,
  placeholder = 'Chọn hoặc gõ để tìm',
  statusSlot,
}: AddressComboboxProps) {
  // `null` là khách chưa gõ gì, nên ô hiện tên đã chọn. Chuỗi là chữ khách đang gõ dở.
  const [query, setQuery] = useState<string | null>(null);
  const [isOpen, setIsOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);

  const listboxId = `${id}-listbox`;
  const visibleOptions = isLoading ? [] : filterAddressOptions(options, query ?? '');
  // Số dòng có thể giảm khi khách gõ thêm. Kẹp chỉ số để `aria-activedescendant` không trỏ ra ngoài.
  const safeActiveIndex =
    visibleOptions.length === 0 ? -1 : Math.min(activeIndex, visibleOptions.length - 1);
  const inputValue = query ?? selected?.name ?? '';

  function optionId(index: number): string {
    return `${id}-option-${index}`;
  }

  // Cuộn dòng đang trỏ vào tầm nhìn, vì danh sách có chiều cao tối đa và cuộn bên trong.
  useEffect(() => {
    if (!isOpen || safeActiveIndex < 0) return;
    document
      .getElementById(`${id}-option-${safeActiveIndex}`)
      ?.scrollIntoView({ block: 'nearest' });
  }, [id, isOpen, safeActiveIndex]);

  function openList() {
    // Mở khi đã có lựa chọn thì trỏ vào dòng đang chọn, để khách thấy mình đang ở đâu trong danh sách.
    const selectedIndex = selected
      ? visibleOptions.findIndex((option) => option.code === selected.code)
      : -1;
    setActiveIndex(selectedIndex >= 0 ? selectedIndex : 0);
    setIsOpen(true);
  }

  function closeAndRevert() {
    // Bỏ chữ gõ dở để ô trở về tên đã chọn. Chữ tự gõ không bao giờ thành giá trị.
    setQuery(null);
    setIsOpen(false);
  }

  function selectOption(option: AddressOption) {
    onSelect(option);
    setQuery(null);
    setIsOpen(false);
  }

  function handleChange(event: ChangeEvent<HTMLInputElement>) {
    setQuery(event.target.value);
    // Gõ chữ mới thì kết quả lọc đổi, nên trỏ lại dòng đầu để Enter chọn ngay dòng khớp nhất.
    setActiveIndex(0);
    setIsOpen(true);
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp': {
        // Chặn con trỏ nhảy về đầu hoặc cuối chữ trong ô khi dùng mũi tên để duyệt danh sách.
        event.preventDefault();
        if (!isOpen) {
          openList();
          return;
        }
        if (visibleOptions.length === 0) return;
        const step = event.key === 'ArrowDown' ? 1 : -1;
        // Dừng ở hai đầu thay vì quay vòng, để khách biết đã tới đầu hoặc cuối danh sách.
        setActiveIndex(Math.max(0, Math.min(safeActiveIndex + step, visibleOptions.length - 1)));
        return;
      }
      case 'Enter': {
        // Danh sách đóng thì Enter thuộc về form cha, ví dụ gửi form. Không chặn.
        if (!isOpen) return;
        // Danh sách mở thì Enter chỉ dùng để chọn dòng, không gửi form khi khách đang chọn dở.
        event.preventDefault();
        const activeOption = visibleOptions[safeActiveIndex];
        if (activeOption) selectOption(activeOption);
        return;
      }
      case 'Escape': {
        if (!isOpen) return;
        // Chặn Escape lan lên hộp thoại cha, để chỉ danh sách đóng chứ hộp thoại không đóng theo.
        event.preventDefault();
        event.stopPropagation();
        closeAndRevert();
        return;
      }
      default:
        // Tab không có nhánh riêng. Ô mất focus nên `onBlur` đóng danh sách mà không chọn dòng nào.
        return;
    }
  }

  const message = isLoading
    ? 'Đang tải danh sách...'
    : visibleOptions.length === 0
      ? 'Không tìm thấy'
      : null;
  const activeOptionId = isOpen && safeActiveIndex >= 0 ? optionId(safeActiveIndex) : undefined;

  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <div className="address-combobox">
        <input
          id={id}
          type="text"
          role="combobox"
          aria-expanded={isOpen}
          aria-controls={listboxId}
          aria-autocomplete="list"
          aria-activedescendant={activeOptionId}
          aria-busy={isLoading || undefined}
          autoComplete="off"
          spellCheck={false}
          disabled={disabled}
          placeholder={isLoading ? 'Đang tải...' : placeholder}
          value={inputValue}
          onChange={handleChange}
          onKeyDown={handleKeyDown}
          onClick={() => {
            if (!isOpen) openList();
          }}
          onBlur={closeAndRevert}
          {...fieldErrorProps(id, errorMessage)}
        />
        {/* Giữ focus ở ô nhập khi bấm chuột vào danh sách, kể cả thanh cuộn. Mất focus sẽ kích hoạt `onBlur` và đóng danh sách trước khi click tới. */}
        <div
          className="address-combobox-popup"
          hidden={!isOpen}
          onMouseDown={(event) => event.preventDefault()}
        >
          <ul id={listboxId} role="listbox" aria-label={label} className="address-combobox-list">
            {visibleOptions.map((option, index) => {
              const isActive = index === safeActiveIndex;
              const isSelected = option.code === selected?.code;
              const classNames = ['address-combobox-option'];
              if (isActive) classNames.push('address-combobox-option-active');
              if (isSelected) classNames.push('address-combobox-option-selected');
              return (
                <li
                  key={option.code}
                  id={optionId(index)}
                  role="option"
                  aria-selected={isSelected}
                  className={classNames.join(' ')}
                  onClick={() => selectOption(option)}
                  onMouseMove={() => setActiveIndex(index)}
                >
                  {option.name}
                </li>
              );
            })}
          </ul>
          {message && <p className="address-combobox-message">{message}</p>}
        </div>
      </div>
      {statusSlot}
      <FieldError inputId={id} message={errorMessage} />
    </div>
  );
}
