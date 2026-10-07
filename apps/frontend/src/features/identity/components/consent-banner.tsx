import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { useActor } from '../hooks/use-actor';
import './consent-banner.css';

// Khóa localStorage ghi nhớ trình duyệt này đã chọn rồi, để không hỏi lại ở mỗi lần tải trang.
const CONSENT_CHOSEN_KEY = 'pk_consent_chosen';

// Banner hỏi đồng ý thu thập dữ liệu, hiện ở mọi trang cho tới khi người dùng chọn.
export function ConsentBanner() {
  // Mặc định ẩn, rồi effect bên dưới mới bật; nếu mặc định hiện thì người đã chọn rồi sẽ thấy banner nháy lên một khung hình.
  const [visible, setVisible] = useState(false);
  // saving khóa hai nút trong lúc gửi để không chọn hai lần.
  const [saving, setSaving] = useState(false);
  const queryClient = useQueryClient();
  const { data: actor } = useActor();

  useEffect(() => {
    // Đã từng chọn trên trình duyệt này thì không hỏi lại, để không làm phiền người dùng.
    const chosen = localStorage.getItem(CONSENT_CHOSEN_KEY);
    if (!chosen) {
      setVisible(true);
    }
  }, [actor]);

  if (!visible) return null;

  // Lưu lựa chọn lên server; "Đồng ý" chỉ bật cá nhân hóa còn marketing để người dùng tự bật ở trang quyền riêng tư.
  async function handleChoice(personalization: boolean, marketing: boolean) {
    setSaving(true);
    try {
      await apiRequest('/api/me/consent', {
        method: 'PUT',
        body: JSON.stringify({
          // Nhóm cần thiết luôn bật, gửi kèm cho đầy đủ dù server không cho tắt.
          necessary: true,
          personalization,
          marketing,
        }),
      });
      // Ghi cờ sau khi server nhận thành công, để chọn thất bại thì lần sau banner vẫn hỏi lại.
      localStorage.setItem(CONSENT_CHOSEN_KEY, 'true');
      // Nạp lại actor vì lựa chọn đồng ý nằm trong actor mà các trang khác đọc.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      setVisible(false);
    } catch {
      // Lỗi mạng thì vẫn ẩn banner để không chặn người dùng dùng trang; không ghi cờ nên lần tải sau sẽ hỏi lại.
      setVisible(false);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div role="region" aria-label="Đồng ý cookie và quyền riêng tư" className="consent-banner">
      <p className="consent-text">
        <strong>Quyền riêng tư của bạn:</strong> Chúng tôi sử dụng cookie cần thiết để vận hành
        trang web. Bạn có thể chọn cho phép tính năng cá nhân hóa (gợi ý sản phẩm phù hợp) và nhận
        thông tin khuyến mãi. <Link to="/privacy">Tùy chỉnh lựa chọn</Link>
      </p>

      <div className="consent-actions">
        {/* Hai nút cùng lớp, cùng kích thước và độ nổi bật theo `spec/SPEC-identity.md`. */}
        <button
          type="button"
          className="btn consent-choice"
          disabled={saving}
          onClick={() => handleChoice(false, false)}
        >
          Chỉ cần thiết
        </button>

        <button
          type="button"
          className="btn consent-choice"
          disabled={saving}
          onClick={() => handleChoice(true, false)}
        >
          Đồng ý
        </button>
      </div>
    </div>
  );
}
