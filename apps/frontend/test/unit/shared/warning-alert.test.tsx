// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { describe, expect, it, vi } from 'vitest';
import { WarningAlert } from '../../../src/shared/warning-alert';

describe('WarningAlert', () => {
  it('giữ chữ đậm, màu cảnh báo và icon tam giác khác icon lỗi', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () =>
        root.render(
          <WarningAlert>
            <strong>Cảnh báo ngân sách tháng:</strong> Đã dùng 80% ngân sách.
          </WarningAlert>,
        ),
      );
      const alert = container.querySelector<HTMLElement>('[role="alert"]')!;
      expect(alert.classList.contains('alert-warn')).toBe(true);
      expect(alert.classList.contains('alert-list')).toBe(true);
      expect(alert.querySelector('.alert-list-body strong')?.textContent).toBe(
        'Cảnh báo ngân sách tháng:',
      );
      const icon = alert.querySelector('svg.alert-list-icon');
      expect(icon?.getAttribute('aria-hidden')).toBe('true');
      expect(icon?.querySelector('path[d="M12 9v4"]')).not.toBeNull();
      expect(icon?.querySelector('circle[cx="12"][cy="12"]')).toBeNull();
    } finally {
      await act(async () => root.unmount());
      vi.unstubAllGlobals();
    }
  });
});
