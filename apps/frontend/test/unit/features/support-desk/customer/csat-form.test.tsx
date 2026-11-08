// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { ApiError } from '../../../../../src/shared/api';
import { CsatForm } from '../../../../../src/features/support-desk/customer/csat-form';
import * as supportApi from '../../../../../src/features/support-desk/customer/support-customer-api';

let container: HTMLDivElement;
let root: Root;

async function renderCsatForm(conversationId: string | null = 'conv-123') {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  await act(async () => {
    root.render(<CsatForm conversationId={conversationId} />);
  });
}

function scoreButtons(): HTMLButtonElement[] {
  return Array.from(container.querySelectorAll<HTMLButtonElement>('.csat-score-btn'));
}

function submitButton(): HTMLButtonElement {
  const element = container.querySelector<HTMLButtonElement>('button[type="submit"]');
  if (!element) throw new Error('Không tìm thấy nút gửi đánh giá.');
  return element;
}

async function clickButton(button: HTMLButtonElement) {
  await act(async () => {
    button.click();
  });
}

beforeEach(() => {
  vi.restoreAllMocks();
});

afterEach(async () => {
  if (root) await act(async () => root.unmount());
  container?.remove();
  vi.unstubAllGlobals();
});

describe('CsatForm giao diện và khả năng tiếp cận', () => {
  it('có 5 nút chọn điểm với tên truy cập nêu rõ điểm và mức hài lòng', async () => {
    await renderCsatForm();

    const buttons = scoreButtons();
    expect(buttons).toHaveLength(5);

    // Tên truy cập phải nêu rõ điểm số và mức độ hài lòng tương ứng.
    const expectedLabels = [
      '1 điểm - Rất không hài lòng',
      '2 điểm - Không hài lòng',
      '3 điểm - Bình thường',
      '4 điểm - Hài lòng',
      '5 điểm - Rất hài lòng',
    ];

    buttons.forEach((button, index) => {
      expect(button.getAttribute('aria-label')).toBe(expectedLabels[index]);
      expect(button.getAttribute('aria-pressed')).toBe('false');
    });
  });

  it('bấm nút điểm thì đánh dấu aria-pressed', async () => {
    await renderCsatForm();

    const buttons = scoreButtons();
    const fourthButton = buttons[3]!;

    // Bấm nút chọn mức điểm 4 hài lòng.
    await clickButton(fourthButton);

    expect(fourthButton.getAttribute('aria-pressed')).toBe('true');
    expect(buttons[0]!.getAttribute('aria-pressed')).toBe('false');
  });
});

describe('CsatForm gửi dữ liệu và xử lý lỗi', () => {
  it('gửi đúng điểm CSAT và hiển thị xác nhận giữ điểm đã chọn', async () => {
    const submitSpy = vi.spyOn(supportApi, 'submitSupportCsat').mockResolvedValueOnce({
      sessionId: 'sess-001',
      score: 5,
    });

    await renderCsatForm('conv-test');

    const buttons = scoreButtons();
    // Khách chọn mức 5 rất hài lòng.
    await clickButton(buttons[4]!);
    await clickButton(submitButton());

    // API nhận đúng mã hội thoại và điểm số đã chọn.
    expect(submitSpy).toHaveBeenCalledWith('conv-test', 5);

    // Giao diện đổi thành thông báo xác nhận thành công chứa điểm đã chọn.
    expect(container.querySelector('form')).toBeNull();
    const alert = container.querySelector('.alert-ok');
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toContain('5/5');
    expect(alert?.textContent).toContain('Rất hài lòng');
  });

  it('lỗi 409 SUPPORT_SESSION_OPEN hiện thông báo tiếng Việt và giữ lựa chọn', async () => {
    const submitSpy = vi
      .spyOn(supportApi, 'submitSupportCsat')
      .mockRejectedValueOnce(
        new ApiError(
          'SUPPORT_SESSION_OPEN',
          409,
          'Phiên hỗ trợ chưa kết thúc nên chưa chấm điểm được.',
        ),
      );

    await renderCsatForm();

    const buttons = scoreButtons();
    // Khách chọn mức 4 hài lòng.
    await clickButton(buttons[3]!);
    await clickButton(submitButton());

    // Thông báo lỗi hiển thị rõ ràng bằng tiếng Việt.
    const errorAlert = container.querySelector('.alert-list');
    expect(errorAlert?.textContent).toContain(
      'Phiên hỗ trợ chưa kết thúc nên chưa chấm điểm được.',
    );

    // Điểm đã chọn phải được giữ nguyên sau khi API báo lỗi.
    expect(buttons[3]!.getAttribute('aria-pressed')).toBe('true');
    expect(submitButton().disabled).toBe(false);
    expect(submitSpy).toHaveBeenCalledTimes(1);
  });

  it('chống request trùng khi nhấn nút gửi liên tiếp', async () => {
    // TypeScript của repo chưa có `Promise.withResolvers`, nên giữ `resolve` bằng biến ngoài.
    let resolveSubmit: () => void = () => {};
    const pendingPromise = new Promise<{ sessionId: string; score: number }>((resolve) => {
      resolveSubmit = () => resolve({ sessionId: 'sess-002', score: 3 });
    });
    const submitSpy = vi.spyOn(supportApi, 'submitSupportCsat').mockReturnValue(pendingPromise);

    await renderCsatForm();

    const buttons = scoreButtons();
    await clickButton(buttons[2]!);

    // Nhấn gửi hai lần liên tiếp khi request đang chạy.
    await clickButton(submitButton());
    await clickButton(submitButton());

    expect(submitSpy).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolveSubmit();
    });
  });
});
