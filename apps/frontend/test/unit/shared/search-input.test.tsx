// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SearchInput } from '../../../src/shared/search-input';

let container: HTMLDivElement;
let root: Root;

function renderInput(value: string, onSearch: (text: string) => void) {
  return act(async () =>
    root.render(<SearchInput label="Tìm nhân viên" value={value} onSearch={onSearch} />),
  );
}

function getInput() {
  return container.querySelector<HTMLInputElement>('input')!;
}

// React theo dõi giá trị ô qua setter gốc. Gán trực tiếp `.value` sẽ không kích hoạt `onChange`.
async function type(text: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(getInput(), text);
    getInput().dispatchEvent(new Event('input', { bubbles: true }));
  });
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('SearchInput', () => {
  it('gõ nhiều ký tự trong 300 ms chỉ gọi onSearch một lần với chữ cuối', async () => {
    const onSearch = vi.fn();
    await renderInput('', onSearch);

    await type('a');
    await act(async () => vi.advanceTimersByTime(100));
    await type('an');
    await act(async () => vi.advanceTimersByTime(100));
    await type('anh');
    await act(async () => vi.advanceTimersByTime(300));

    expect(onSearch).toHaveBeenCalledTimes(1);
    expect(onSearch).toHaveBeenCalledWith('anh');
  });

  it('chưa đủ 300 ms thì chưa gọi onSearch', async () => {
    const onSearch = vi.fn();
    await renderInput('', onSearch);

    await type('an');
    await act(async () => vi.advanceTimersByTime(299));

    expect(onSearch).not.toHaveBeenCalled();

    await act(async () => vi.advanceTimersByTime(1));

    expect(onSearch).toHaveBeenCalledTimes(1);
  });

  it('prop value đổi từ bên ngoài thì ô hiện giá trị mới và không gọi onSearch', async () => {
    const onSearch = vi.fn();
    await renderInput('an', onSearch);
    expect(getInput().value).toBe('an');

    await renderInput('binh', onSearch);
    await act(async () => vi.advanceTimersByTime(1000));

    expect(getInput().value).toBe('binh');
    expect(onSearch).not.toHaveBeenCalled();
  });

  it('prop value đổi từ bên ngoài hủy lần gọi đang chờ của chữ cũ', async () => {
    const onSearch = vi.fn();
    await renderInput('', onSearch);

    await type('an');
    await renderInput('binh', onSearch);
    await act(async () => vi.advanceTimersByTime(1000));

    expect(onSearch).not.toHaveBeenCalled();
  });

  it('gỡ component trước 300 ms thì không gọi onSearch', async () => {
    const onSearch = vi.fn();
    await renderInput('', onSearch);

    await type('an');
    await act(async () => root.unmount());
    await act(async () => vi.advanceTimersByTime(1000));

    expect(onSearch).not.toHaveBeenCalled();
    // `afterEach` gỡ lần nữa. Dựng lại root để lần gỡ đó không lỗi.
    root = createRoot(container);
  });

  it('ô có tên truy cập lấy từ nhãn nhìn thấy', async () => {
    await renderInput('', vi.fn());

    const input = getInput();
    const label = container.querySelector<HTMLLabelElement>('label')!;

    expect(input.type).toBe('search');
    expect(label.textContent).toBe('Tìm nhân viên');
    expect(label.htmlFor).toBe(input.id);
    expect(input.id).not.toBe('');
  });
});
