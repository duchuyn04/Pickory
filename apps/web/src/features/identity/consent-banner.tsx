import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { apiRequest } from '../../shared/api';
import { useActor } from './use-actor';

const CONSENT_CHOSEN_KEY = 'pk_consent_chosen';

export function ConsentBanner() {
  const [visible, setVisible] = useState(false);
  const [saving, setSaving] = useState(false);
  const queryClient = useQueryClient();
  const { data: actor } = useActor();

  useEffect(() => {
    // Nếu chưa từng lưu cờ ở localStorage và actor chưa có lựa chọn
    const chosen = localStorage.getItem(CONSENT_CHOSEN_KEY);
    if (!chosen) {
      setVisible(true);
    }
  }, [actor]);

  if (!visible) return null;

  async function handleChoice(personalization: boolean, marketing: boolean) {
    setSaving(true);
    try {
      await apiRequest('/api/me/consent', {
        method: 'PUT',
        body: JSON.stringify({
          necessary: true,
          personalization,
          marketing,
        }),
      });
      localStorage.setItem(CONSENT_CHOSEN_KEY, 'true');
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      setVisible(false);
    } catch {
      // Bỏ qua lỗi mạng cục bộ để không chặn người dùng
      setVisible(false);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div
      role="region"
      aria-label="Đồng ý cookie và quyền riêng tư"
      style={{
        position: 'fixed',
        bottom: 0,
        left: 0,
        right: 0,
        backgroundColor: 'var(--color-surface)',
        borderTop: '1px solid var(--color-border)',
        boxShadow: '0 -4px 20px rgba(0, 0, 0, 0.08)',
        padding: 'var(--space-4) var(--space-6)',
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 'var(--space-4)',
        zIndex: 9999,
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div style={{ flex: '1 1 500px', fontSize: 'var(--text-sm)', color: 'var(--color-ink)' }}>
        <strong>Quyền riêng tư của bạn:</strong> Chúng tôi sử dụng cookie cần thiết để vận hành
        trang web. Bạn có thể chọn cho phép tính năng cá nhân hóa (gợi ý sản phẩm phù hợp) và nhận
        thông tin khuyến mãi.{' '}
        <Link
          to="/privacy"
          style={{ color: 'var(--color-brand)', textDecoration: 'none', fontWeight: 600 }}
        >
          Tùy chỉnh lựa chọn
        </Link>
      </div>

      <div style={{ display: 'flex', gap: 'var(--space-3)', flexWrap: 'wrap' }}>
        {/* Hai nút cùng kích thước và độ nổi bật theo spec/SPEC-identity.md */}
        <button
          type="button"
          disabled={saving}
          onClick={() => handleChoice(false, false)}
          style={{
            flex: '1 1 140px',
            minWidth: 130,
            padding: 'var(--space-2) var(--space-4)',
            backgroundColor: 'var(--color-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-control)',
            color: 'var(--color-ink)',
            fontSize: 'var(--text-sm)',
            fontWeight: 600,
            cursor: saving ? 'not-allowed' : 'pointer',
          }}
        >
          Chỉ cần thiết
        </button>

        <button
          type="button"
          disabled={saving}
          onClick={() => handleChoice(true, false)}
          style={{
            flex: '1 1 140px',
            minWidth: 130,
            padding: 'var(--space-2) var(--space-4)',
            backgroundColor: 'var(--color-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-control)',
            color: 'var(--color-ink)',
            fontSize: 'var(--text-sm)',
            fontWeight: 600,
            cursor: saving ? 'not-allowed' : 'pointer',
          }}
        >
          Đồng ý
        </button>
      </div>
    </div>
  );
}
