// @vitest-environment jsdom
import React, { act, StrictMode, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useDialogFocus } from '../../../src/shared/use-dialog-focus';

let container: HTMLDivElement;
let root: Root;

function DialogFixture({ version = 0 }: { version?: number }) {
  const [open, setOpen] = useState(false);
  const [closedVersion, setClosedVersion] = useState<number | null>(null);
  const ref = useDialogFocus({
    isOpen: open,
    onClose: () => {
      setClosedVersion(version);
      setOpen(false);
    },
  });
  return (
    <>
      <button onClick={() => setOpen(true)}>Mở</button>
      <output>{closedVersion}</output>
      {open && (
        <div ref={ref} role="dialog" tabIndex={-1}>
          <input aria-label="Tên" />
          <button onClick={() => setOpen(false)}>Đóng</button>
        </div>
      )}
    </>
  );
}

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

async function openDialog() {
  const opener = container.querySelector('button');
  if (!opener) throw new Error('Thiếu nút mở dialog.');
  opener.focus();
  await act(async () => opener.click());
  return opener;
}

describe('tiêu điểm dialog', () => {
  it('StrictMode và rerender giữ focus, Escape đóng bằng callback mới và trả về đúng opener', async () => {
    await act(async () =>
      root.render(
        <StrictMode>
          <DialogFixture />
        </StrictMode>,
      ),
    );
    const opener = await openDialog();
    const input = container.querySelector('input');
    expect(document.activeElement).toBe(input);
    await act(async () =>
      root.render(
        <StrictMode>
          <DialogFixture version={1} />
        </StrictMode>,
      ),
    );
    expect(document.activeElement).toBe(input);
    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    });
    expect(container.querySelector('[role="dialog"]')).toBeNull();
    expect(container.querySelector('output')?.textContent).toBe('1');
    expect(document.activeElement).toBe(opener);
  });

  it('Tab và Shift+Tab không thoát khỏi dialog đang mở', async () => {
    await act(async () => root.render(<DialogFixture />));
    await openDialog();
    const input = container.querySelector('input');
    const close = container.querySelector<HTMLButtonElement>('[role="dialog"] button');
    if (!close) throw new Error('Thiếu nút đóng.');
    close.focus();
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', cancelable: true }));
    expect(document.activeElement).toBe(input);
    window.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, cancelable: true }),
    );
    expect(document.activeElement).toBe(close);
  });
});
