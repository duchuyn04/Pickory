import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { ConnectionStatus } from '../../../../../src/features/support-desk/admin/connection-status';

describe('ConnectionStatus', () => {
  it('hiện chữ Mất kết nối khi luồng ở trạng thái lost', () => {
    const markup = renderToStaticMarkup(<ConnectionStatus status="lost" />);

    expect(markup).toContain('Mất kết nối');
  });

  it.each(['connecting', 'open'] as const)(
    'không hiện chữ nào khi luồng ở trạng thái %s',
    (status) => {
      const markup = renderToStaticMarkup(<ConnectionStatus status={status} />);

      expect(markup).not.toContain('Mất kết nối');
    },
  );

  it('luôn có vùng aria-live để trình đọc màn hình đọc chữ khi nó xuất hiện sau đó', () => {
    const markup = renderToStaticMarkup(<ConnectionStatus status="open" />);

    expect(markup).toContain('aria-live="polite"');
  });

  it('không dùng role="status", vì các màn quản trị đếm vùng status để biết trang còn đang tải', () => {
    const markup = renderToStaticMarkup(<ConnectionStatus status="lost" />);

    expect(markup).not.toContain('role="status"');
  });
});
