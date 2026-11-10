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

// Hai hộp thoại chồng nhau: `inner` nằm lồng trong DOM của `outer`, mở sau và nằm trên cùng. Mỗi hộp đếm số lần nhận Esc.
function StackFixture() {
  const [innerOpen, setInnerOpen] = useState(false);
  const [escapes, setEscapes] = useState({ outer: 0, inner: 0 });
  return (
    <>
      <output data-testid="escapes">{`${escapes.outer},${escapes.inner}`}</output>
      <button data-testid="open-inner" onClick={() => setInnerOpen(true)}>
        Mở trong
      </button>
      <button data-testid="close-inner" onClick={() => setInnerOpen(false)}>
        Đóng trong
      </button>
      <StackedDialog
        label="outer"
        onEscape={() => setEscapes((current) => ({ ...current, outer: current.outer + 1 }))}
      >
        {innerOpen && (
          <StackedDialog
            label="inner"
            onEscape={() => {
              setEscapes((current) => ({ ...current, inner: current.inner + 1 }));
              setInnerOpen(false);
            }}
          />
        )}
      </StackedDialog>
    </>
  );
}

function StackedDialog({
  label,
  onEscape,
  children,
}: {
  label: string;
  onEscape: () => void;
  children?: React.ReactNode;
}) {
  const ref = useDialogFocus({ onClose: onEscape });
  return (
    <div ref={ref} role="dialog" aria-label={label} tabIndex={-1}>
      <button>{`nút ${label}`}</button>
      {children}
    </div>
  );
}

function escapeCounts(): string {
  return container.querySelector('[data-testid="escapes"]')?.textContent ?? '';
}

async function pressEscape() {
  await act(async () => {
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
  });
}

async function openInner() {
  const button = container.querySelector<HTMLButtonElement>('[data-testid="open-inner"]');
  await act(async () => button?.click());
}

describe('chồng hộp thoại', () => {
  beforeEach(async () => {
    await act(async () => root.render(<StackFixture />));
    // Hộp trong mở sau hộp ngoài, giống hộp "Thêm mẫu" mở từ hộp "Mẫu của tôi".
    await openInner();
  });

  it('Tab chỉ bẫy focus trong hộp trên cùng', async () => {
    const innerButton = container.querySelector<HTMLButtonElement>('[aria-label="inner"] > button');
    const outerButton = container.querySelector<HTMLButtonElement>('[aria-label="outer"] > button');
    if (!innerButton || !outerButton) throw new Error('Thiếu nút của hộp thoại.');
    innerButton.focus();
    // Nếu hộp ngoài cũng xử lý Tab, nó kéo focus sang nút của nó rồi hộp trong kéo về, nên kết quả cuối vẫn đúng. Spy bắt bước kéo thừa đó.
    const outerFocusSpy = vi.spyOn(outerButton, 'focus');

    // Hộp trong chỉ có một nút, nên Tab quay về chính nút đó.
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', cancelable: true }));

    expect(outerFocusSpy).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(innerButton);
  });

  it('đóng hộp trên cùng thì hộp dưới nhận lại Esc', async () => {
    await pressEscape();
    expect(escapeCounts()).toBe('0,1');
    expect(container.querySelector('[aria-label="inner"]')).toBeNull();

    await pressEscape();
    expect(escapeCounts()).toBe('1,1');
  });

  it('hộp trong đóng từ bên ngoài (không qua Esc) thì bị gỡ khỏi chồng và hộp ngoài nhận Esc', async () => {
    // Đóng hộp trong bằng state của cha. Nếu chồng còn giữ hộp trong đã gỡ, hộp ngoài không phải hộp trên cùng và không nhận Esc.
    const closeInner = container.querySelector<HTMLButtonElement>('[data-testid="close-inner"]');
    await act(async () => closeInner?.click());
    expect(container.querySelector('[aria-label="inner"]')).toBeNull();

    await pressEscape();

    expect(escapeCounts()).toBe('1,0');
  });
});
