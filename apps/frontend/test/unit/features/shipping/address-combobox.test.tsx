// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AddressOption } from '../../../../src/features/shipping/address-api';
import { AddressCombobox } from '../../../../src/features/shipping/address-combobox';

const provinces: AddressOption[] = [
  { code: '1', name: 'Cần Thơ' },
  { code: '2', name: 'Đà Nẵng' },
  { code: '3', name: 'Hà Nội' },
  { code: '4', name: 'Hồ Chí Minh' },
];

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

interface RenderOptions {
  options?: AddressOption[];
  selected?: AddressOption | null;
  disabled?: boolean;
  isLoading?: boolean;
  errorMessage?: string | null;
}

async function renderCombobox(settings: RenderOptions = {}) {
  const onSelect = vi.fn();
  await act(async () =>
    root.render(
      <AddressCombobox
        id="province"
        label="Tỉnh/Thành phố"
        options={settings.options ?? provinces}
        selected={settings.selected ?? null}
        onSelect={onSelect}
        disabled={settings.disabled}
        isLoading={settings.isLoading}
        errorMessage={settings.errorMessage}
      />,
    ),
  );
  return { onSelect };
}

function getInput(): HTMLInputElement {
  return container.querySelector('input[role="combobox"]') as HTMLInputElement;
}

function getOptions(): HTMLElement[] {
  return Array.from(container.querySelectorAll('[role="option"]'));
}

function optionAt(index: number): HTMLElement {
  const option = getOptions()[index];
  if (!option) throw new Error(`Không có dòng thứ ${index} trong danh sách.`);
  return option;
}

async function pressKey(key: string): Promise<KeyboardEvent> {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
  await act(async () => {
    getInput().dispatchEvent(event);
  });
  return event;
}

async function typeText(text: string) {
  // React theo dõi giá trị ô nhập bằng setter gốc, nên đặt giá trị qua setter của prototype rồi bắn sự kiện `input`.
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(getInput(), text);
    getInput().dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function focusInput() {
  await act(async () => getInput().focus());
}

async function blurInput() {
  await act(async () => getInput().blur());
}

describe('AddressCombobox: ARIA và nhãn', () => {
  it('ô nhập có role combobox, aria-autocomplete list và đóng khi chưa mở', async () => {
    await renderCombobox();
    const input = getInput();
    expect(input.getAttribute('aria-autocomplete')).toBe('list');
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(input.getAttribute('aria-activedescendant')).toBeNull();
  });

  it('aria-controls trỏ tới listbox thật', async () => {
    await renderCombobox();
    const listboxId = getInput().getAttribute('aria-controls');
    const listbox = container.querySelector(`#${listboxId}`);
    expect(listbox?.getAttribute('role')).toBe('listbox');
  });

  it('nhãn nối với ô qua htmlFor', async () => {
    await renderCombobox();
    const label = container.querySelector('label') as HTMLLabelElement;
    expect(label.textContent).toBe('Tỉnh/Thành phố');
    expect(label.htmlFor).toBe(getInput().id);
  });

  it('hiện tên đã chọn trong ô và đánh dấu aria-selected đúng dòng', async () => {
    await renderCombobox({ selected: provinces[2] });
    expect(getInput().value).toBe('Hà Nội');
    await pressKey('ArrowDown');
    const selectedNames = getOptions()
      .filter((option) => option.getAttribute('aria-selected') === 'true')
      .map((option) => option.textContent);
    expect(selectedNames).toEqual(['Hà Nội']);
  });
});

describe('AddressCombobox: bàn phím', () => {
  it('mũi tên xuống mở danh sách và trỏ vào dòng đầu', async () => {
    await renderCombobox();
    await pressKey('ArrowDown');
    expect(getInput().getAttribute('aria-expanded')).toBe('true');
    const options = getOptions();
    expect(options.map((option) => option.textContent)).toEqual([
      'Cần Thơ',
      'Đà Nẵng',
      'Hà Nội',
      'Hồ Chí Minh',
    ]);
    expect(getInput().getAttribute('aria-activedescendant')).toBe(optionAt(0).id);
  });

  it('mũi tên xuống đi xuống, mũi tên lên đi lên và dừng ở hai đầu', async () => {
    await renderCombobox();
    await pressKey('ArrowDown');
    await pressKey('ArrowDown');
    expect(getInput().getAttribute('aria-activedescendant')).toBe(optionAt(1).id);
    await pressKey('ArrowUp');
    expect(getInput().getAttribute('aria-activedescendant')).toBe(optionAt(0).id);
    await pressKey('ArrowUp');
    expect(getInput().getAttribute('aria-activedescendant')).toBe(optionAt(0).id);
    for (let step = 0; step < 6; step += 1) await pressKey('ArrowDown');
    expect(getInput().getAttribute('aria-activedescendant')).toBe(optionAt(3).id);
  });

  it('mở danh sách khi đã có lựa chọn thì trỏ vào dòng đang chọn', async () => {
    await renderCombobox({ selected: provinces[2] });
    await pressKey('ArrowDown');
    expect(getInput().getAttribute('aria-activedescendant')).toBe(optionAt(2).id);
  });

  it('Enter chọn dòng đang trỏ, đóng danh sách và hiện tên đã chọn', async () => {
    const { onSelect } = await renderCombobox();
    await pressKey('ArrowDown');
    await pressKey('ArrowDown');
    const event = await pressKey('Enter');
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith(provinces[1]);
    // Enter đã dùng để chọn dòng, nên form cha không bị gửi đi.
    expect(event.defaultPrevented).toBe(true);
    expect(getInput().getAttribute('aria-expanded')).toBe('false');
  });

  it('Enter khi danh sách đóng không bị chặn, để form cha gửi đi', async () => {
    const { onSelect } = await renderCombobox();
    const event = await pressKey('Enter');
    expect(event.defaultPrevented).toBe(false);
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('Escape đóng danh sách và trả chữ trong ô về tên đã chọn', async () => {
    await renderCombobox({ selected: provinces[2] });
    await typeText('xyz');
    expect(getInput().value).toBe('xyz');
    expect(getInput().getAttribute('aria-expanded')).toBe('true');
    await pressKey('Escape');
    expect(getInput().getAttribute('aria-expanded')).toBe('false');
    expect(getInput().value).toBe('Hà Nội');
  });

  it('Tab rời ô, không tự chọn dòng đang trỏ', async () => {
    const { onSelect } = await renderCombobox();
    await focusInput();
    await pressKey('ArrowDown');
    await pressKey('Tab');
    await blurInput();
    expect(onSelect).not.toHaveBeenCalled();
    expect(getInput().getAttribute('aria-expanded')).toBe('false');
  });

  it('dòng đang trỏ cuộn vào tầm nhìn khi đi bằng phím', async () => {
    const scrollIntoView = vi.fn();
    Element.prototype.scrollIntoView = scrollIntoView;
    await renderCombobox();
    await pressKey('ArrowDown');
    await pressKey('ArrowDown');
    expect(scrollIntoView).toHaveBeenCalled();
    const lastCallTarget = scrollIntoView.mock.contexts.at(-1) as HTMLElement;
    expect(lastCallTarget.id).toBe(optionAt(1).id);
  });
});

describe('AddressCombobox: gõ chữ và rời ô', () => {
  it('gõ "ha noi" chỉ còn dòng "Hà Nội"', async () => {
    await renderCombobox();
    await typeText('ha noi');
    expect(getOptions().map((option) => option.textContent)).toEqual(['Hà Nội']);
  });

  it('gõ chữ trỏ sẵn vào dòng đầu để Enter chọn được ngay', async () => {
    const { onSelect } = await renderCombobox();
    await typeText('da nang');
    await pressKey('Enter');
    expect(onSelect).toHaveBeenCalledWith(provinces[1]);
  });

  it('không khớp dòng nào thì hiện "Không tìm thấy" và không có option', async () => {
    await renderCombobox();
    await typeText('khong co');
    expect(getOptions()).toHaveLength(0);
    expect(container.textContent).toContain('Không tìm thấy');
  });

  it('Enter khi không có dòng khớp không chọn gì', async () => {
    const { onSelect } = await renderCombobox();
    await typeText('khong co');
    await pressKey('Enter');
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('rời ô khi chữ không khớp thì trả chữ về tên đã chọn', async () => {
    const { onSelect } = await renderCombobox({ selected: provinces[0] });
    await focusInput();
    await typeText('Hà N');
    await blurInput();
    expect(getInput().value).toBe('Cần Thơ');
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('rời ô khi chưa chọn gì thì xóa chữ tự gõ', async () => {
    const { onSelect } = await renderCombobox();
    await focusInput();
    await typeText('Hà Nội');
    await blurInput();
    expect(getInput().value).toBe('');
    expect(onSelect).not.toHaveBeenCalled();
  });
});

describe('AddressCombobox: chuột', () => {
  it('bấm một dòng chọn dòng đó và ô giữ focus', async () => {
    const { onSelect } = await renderCombobox();
    await focusInput();
    await pressKey('ArrowDown');
    const target = optionAt(3);
    const mouseDown = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    await act(async () => {
      target.dispatchEvent(mouseDown);
    });
    await act(async () => {
      target.click();
    });
    expect(onSelect).toHaveBeenCalledWith(provinces[3]);
    // Chặn mousedown để ô nhập không mất focus trước khi click tới nơi.
    expect(mouseDown.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(getInput());
  });
});

describe('AddressCombobox: trạng thái', () => {
  it('ô tắt khi disabled', async () => {
    await renderCombobox({ disabled: true });
    expect(getInput().disabled).toBe(true);
  });

  it('đang tải thì hiện chữ đang tải và không có dòng nào để chọn', async () => {
    await renderCombobox({ isLoading: true });
    await pressKey('ArrowDown');
    expect(getOptions()).toHaveLength(0);
    expect(container.textContent).toContain('Đang tải');
    expect(container.textContent).not.toContain('Không tìm thấy');
  });

  it('có lỗi thì hiện dưới ô và nối aria-invalid, aria-describedby', async () => {
    await renderCombobox({ errorMessage: 'Vui lòng chọn tỉnh/thành phố.' });
    const input = getInput();
    const error = container.querySelector('.form-error') as HTMLElement;
    expect(error.textContent).toBe('Vui lòng chọn tỉnh/thành phố.');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe(error.id);
  });

  it('không lỗi thì không có aria-invalid', async () => {
    await renderCombobox();
    expect(getInput().getAttribute('aria-invalid')).toBeNull();
  });
});
