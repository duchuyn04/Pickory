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

function getClearButton() {
  return container.querySelector<HTMLButtonElement>('button[aria-label="Xóa từ khóa"]');
}

// Gửi phím tới ô và trả về sự kiện, để test kiểm được `defaultPrevented`.
async function pressKey(key: string) {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
  await act(async () => {
    getInput().dispatchEvent(event);
  });
  return event;
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

  describe('nút xóa từ khóa', () => {
    it('không có khi ô rỗng', async () => {
      await renderInput('', vi.fn());

      expect(getClearButton()).toBeNull();
    });

    it('không có khi ô chỉ có khoảng trắng', async () => {
      await renderInput('', vi.fn());

      await type('   ');

      expect(getClearButton()).toBeNull();
    });

    it('có khi ô có chữ, là nút `button` có tên truy cập Xóa từ khóa', async () => {
      await renderInput('', vi.fn());

      await type('an');

      const button = getClearButton();
      expect(button).not.toBeNull();
      expect(button!.type).toBe('button');
      expect(button!.getAttribute('aria-label')).toBe('Xóa từ khóa');
    });

    it('có ngay khi ô nhận từ khóa ban đầu từ prop value', async () => {
      await renderInput('an', vi.fn());

      expect(getClearButton()).not.toBeNull();
    });

    it('bấm nút thì ô rỗng, onSearch("") gọi ngay một lần, timer cũ bị hủy và focus về ô', async () => {
      const onSearch = vi.fn();
      await renderInput('', onSearch);
      await type('an');

      await act(async () => getClearButton()!.click());

      expect(getInput().value).toBe('');
      expect(onSearch).toHaveBeenCalledTimes(1);
      expect(onSearch).toHaveBeenCalledWith('');
      expect(document.activeElement).toBe(getInput());

      await act(async () => vi.advanceTimersByTime(300));

      expect(onSearch).toHaveBeenCalledTimes(1);
      expect(getClearButton()).toBeNull();
    });
  });

  describe('phím Enter', () => {
    it('gọi onSearch ngay với chữ vừa gõ, rồi timer 300 ms không gọi thêm', async () => {
      const onSearch = vi.fn();
      await renderInput('', onSearch);
      await type('anh');

      const event = await pressKey('Enter');

      expect(event.defaultPrevented).toBe(true);
      expect(onSearch).toHaveBeenCalledTimes(1);
      expect(onSearch).toHaveBeenCalledWith('anh');

      await act(async () => vi.advanceTimersByTime(300));

      expect(onSearch).toHaveBeenCalledTimes(1);
    });

    it('vẫn gọi onSearch khi chữ trùng giá trị đang áp dụng', async () => {
      const onSearch = vi.fn();
      await renderInput('anh', onSearch);

      await pressKey('Enter');

      expect(onSearch).toHaveBeenCalledTimes(1);
      expect(onSearch).toHaveBeenCalledWith('anh');
    });
  });

  describe('phím Esc', () => {
    it('khi ô có chữ thì ô rỗng, onSearch("") gọi ngay và timer sau đó không gọi thêm', async () => {
      const onSearch = vi.fn();
      await renderInput('', onSearch);
      await type('an');

      const event = await pressKey('Escape');

      // Chặn hành vi mặc định để trình duyệt không tự xóa ô rồi phát thêm sự kiện `input`.
      expect(event.defaultPrevented).toBe(true);
      expect(getInput().value).toBe('');
      expect(onSearch).toHaveBeenCalledTimes(1);
      expect(onSearch).toHaveBeenCalledWith('');
      expect(document.activeElement).toBe(getInput());

      await act(async () => vi.advanceTimersByTime(300));

      expect(onSearch).toHaveBeenCalledTimes(1);
    });

    it('khi ô chỉ có khoảng trắng thì vẫn xóa ô, vì nút xóa ẩn và Esc là cách duy nhất để dọn', async () => {
      const onSearch = vi.fn();
      await renderInput('', onSearch);
      await type('   ');

      await pressKey('Escape');
      await act(async () => vi.advanceTimersByTime(300));

      expect(getInput().value).toBe('');
      expect(onSearch).toHaveBeenCalledTimes(1);
      expect(onSearch).toHaveBeenCalledWith('');
    });

    it('khi ô rỗng thì không gọi onSearch', async () => {
      const onSearch = vi.fn();
      await renderInput('', onSearch);

      await pressKey('Escape');
      await act(async () => vi.advanceTimersByTime(300));

      expect(onSearch).not.toHaveBeenCalled();
    });
  });

  it('phím khác Enter và Esc không gọi onSearch ngoài lần hẹn giờ', async () => {
    const onSearch = vi.fn();
    await renderInput('', onSearch);
    await type('an');

    await pressKey('a');

    expect(onSearch).not.toHaveBeenCalled();
  });
});
