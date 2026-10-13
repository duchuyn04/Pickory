// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MoneyInput } from '../../../src/shared/money-input';

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
});

interface HarnessProps {
  initialValue?: number | null;
  receivedValues: (number | null)[];
  describedBy?: string;
  invalid?: boolean;
  disabled?: boolean;
}

// Form cha giữ số, đúng như hợp đồng của `MoneyInput`: component nhận và trả `number | null`.
function Harness({
  initialValue = null,
  receivedValues,
  describedBy,
  invalid,
  disabled,
}: HarnessProps) {
  const [value, setValue] = useState<number | null>(initialValue);
  return (
    <MoneyInput
      id="amount"
      name="amount"
      aria-label="Giá bán"
      aria-invalid={invalid ? true : undefined}
      aria-describedby={describedBy}
      required
      disabled={disabled}
      value={value}
      onChange={(next) => {
        receivedValues.push(next);
        setValue(next);
      }}
    />
  );
}

async function renderHarness(props: Partial<HarnessProps> = {}) {
  const receivedValues: (number | null)[] = [];
  await act(async () => root.render(<Harness receivedValues={receivedValues} {...props} />));
  const input = container.querySelector('input') as HTMLInputElement;
  return { input, receivedValues };
}

// Mô phỏng trình duyệt: đổi chuỗi, đặt con trỏ, rồi phát sự kiện `input`.
async function enterText(input: HTMLInputElement, text: string, caret = text.length) {
  await act(async () => {
    const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
    // Người dùng chỉ gõ vào ô đang có focus, và component chỉ đặt con trỏ cho ô có focus.
    input.focus();
    valueSetter?.call(input, text);
    input.setSelectionRange(caret, caret);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

describe('MoneyInput', () => {
  it('là ô chữ mở bàn phím số và tắt tự điền', async () => {
    const { input } = await renderHarness();
    expect(input.type).toBe('text');
    expect(input.inputMode).toBe('numeric');
    expect(input.autocomplete).toBe('off');
  });

  it('gõ từng phím hiện dấu chấm nghìn và trả về số cho form cha', async () => {
    const { input, receivedValues } = await renderHarness();

    let typed = '';
    for (const key of '1250000') {
      typed += key;
      await enterText(input, input.value + key);
    }

    expect(typed).toBe('1250000');
    expect(input.value).toBe('1.250.000');
    expect(receivedValues.at(-1)).toBe(1250000);
    expect(receivedValues.every((value) => typeof value === 'number')).toBe(true);
  });

  it('dán chuỗi có dấu chấm và đơn vị ra cùng số với chuỗi chỉ có chữ số', async () => {
    const withUnit = await renderHarness();
    await enterText(withUnit.input, '1.250.000 đ');
    expect(withUnit.input.value).toBe('1.250.000');
    expect(withUnit.receivedValues.at(-1)).toBe(1250000);

    await act(async () => root.unmount());
    root = createRoot(container);

    const digitsOnly = await renderHarness();
    await enterText(digitsOnly.input, '1250000');
    expect(digitsOnly.input.value).toBe('1.250.000');
    expect(digitsOnly.receivedValues.at(-1)).toBe(1250000);
  });

  it('bỏ ký tự khác số khi gõ chữ', async () => {
    const { input, receivedValues } = await renderHarness({ initialValue: 12 });
    await enterText(input, '12abc');
    expect(input.value).toBe('12');
    expect(receivedValues.at(-1)).toBe(12);
  });

  it('xóa hết ô trả null và ô rỗng', async () => {
    const { input, receivedValues } = await renderHarness({ initialValue: 500000 });
    expect(input.value).toBe('500.000');

    await enterText(input, '');

    expect(input.value).toBe('');
    expect(receivedValues.at(-1)).toBeNull();
  });

  it('sửa giữa số giữ con trỏ sau chữ số vừa gõ, không nhảy về cuối', async () => {
    const { input } = await renderHarness({ initialValue: 1250000 });
    expect(input.value).toBe('1.250.000');

    // Con trỏ sau `1.2`, người dùng gõ `9`.
    await enterText(input, '1.2950.000', 4);

    expect(input.value).toBe('12.950.000');
    expect(input.selectionStart).toBe(4);
    expect(input.selectionEnd).toBe(4);
  });

  it('xóa ngay sau dấu chấm giữ con trỏ ở chỗ sửa', async () => {
    const { input } = await renderHarness({ initialValue: 1250000 });

    // Con trỏ sau `1.`, người dùng bấm Backspace xóa dấu chấm.
    await enterText(input, '1250.000', 1);

    expect(input.value).toBe('1.250.000');
    expect(input.selectionStart).toBe(1);
  });

  it('từ chối chữ số vượt giới hạn và giữ giá trị cũ', async () => {
    const { input, receivedValues } = await renderHarness({ initialValue: 2147483647 });
    expect(input.value).toBe('2.147.483.647');

    await enterText(input, '2.147.483.6479');

    expect(input.value).toBe('2.147.483.647');
    expect(receivedValues).toEqual([]);
  });

  it('hiện hậu tố đ trong khung ô và ẩn khỏi trình đọc màn hình', async () => {
    await renderHarness();
    const suffix = container.querySelector('.money-input-suffix');
    expect(suffix?.textContent).toBe('đ');
    expect(suffix?.getAttribute('aria-hidden')).toBe('true');
    expect(container.querySelector('.money-input')?.contains(suffix ?? null)).toBe(true);
  });

  it('nối đơn vị đọc được vào aria-describedby', async () => {
    const { input } = await renderHarness();
    const describedBy = input.getAttribute('aria-describedby') ?? '';
    const unitText = describedBy
      .split(' ')
      .map((id) => document.getElementById(id)?.textContent)
      .join(' ');
    expect(unitText).toContain('đồng');
  });

  it('giữ aria-describedby của lỗi validate và thêm đơn vị sau nó', async () => {
    const { input } = await renderHarness({ describedBy: 'amount-error', invalid: true });
    const ids = (input.getAttribute('aria-describedby') ?? '').split(' ');
    expect(ids[0]).toBe('amount-error');
    expect(ids).toHaveLength(2);
    expect(input.getAttribute('aria-invalid')).toBe('true');
  });

  it('chuyển id, name, aria-label, required và disabled xuống ô nhập', async () => {
    const { input } = await renderHarness({ disabled: true });
    expect(input.id).toBe('amount');
    expect(input.name).toBe('amount');
    expect(input.getAttribute('aria-label')).toBe('Giá bán');
    expect(input.required).toBe(true);
    expect(input.disabled).toBe(true);
  });
});

describe('MoneyInput căn lề', () => {
  async function renderWithAlign(align?: 'start' | 'end') {
    await act(async () =>
      root.render(<MoneyInput value={1250000} onChange={() => {}} align={align} />),
    );
    return container.querySelector('.money-input') as HTMLElement;
  }

  it('mặc định căn trái, không có kiểu căn phải', async () => {
    const frame = await renderWithAlign();
    expect(frame.classList.contains('money-input-end')).toBe(false);
  });

  it('align="end" căn phải cho cột bảng', async () => {
    const frame = await renderWithAlign('end');
    expect(frame.classList.contains('money-input-end')).toBe(true);
  });

  it('hậu tố đ vẫn có mặt ở cả hai kiểu căn', async () => {
    expect((await renderWithAlign('start')).querySelector('.money-input-suffix')?.textContent).toBe(
      'đ',
    );
    expect((await renderWithAlign('end')).querySelector('.money-input-suffix')?.textContent).toBe(
      'đ',
    );
  });
});
