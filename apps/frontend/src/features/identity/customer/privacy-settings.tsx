import { useQueryClient } from '@tanstack/react-query';
import React, { useEffect, useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useActor } from '../hooks/use-actor';
import './account.css';

// Trang để khách vãng lai và khách đã đăng nhập xem và đổi lựa chọn đồng ý bất cứ lúc nào.
export function PrivacySettingsPage() {
  const { data: actor, isLoading } = useActor();
  const queryClient = useQueryClient();

  // Trạng thái các ô tick đang chỉnh, tách khỏi actor để chỉ lưu khi người dùng bấm nút Lưu.
  // Mặc định tắt vì chưa chọn thì không được thu thập.
  const [personalization, setPersonalization] = useState(false);
  const [marketing, setMarketing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [savedMessage, setSavedMessage] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // Điền các ô tick theo lựa chọn hiện tại đang lưu trên server.
    if (actor?.consent) {
      setPersonalization(actor.consent.personalization);
      setMarketing(actor.consent.marketing);
    }
  }, [actor]);

  async function handleSave(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setSaving(true);
    // Xóa thông báo của lần lưu trước để không hiện lẫn với kết quả mới.
    setSavedMessage(false);
    setError(null);

    try {
      await apiRequest('/api/me/consent', {
        method: 'PUT',
        body: JSON.stringify({
          // Nhóm cần thiết luôn bật, không cho tắt.
          necessary: true,
          personalization,
          marketing,
        }),
      });
      // Người vào trang này trực tiếp mà chưa qua banner cũng coi là đã chọn, để banner không hỏi lại.
      localStorage.setItem('pk_consent_chosen', 'true');
      // Nạp lại actor để các nơi đọc lựa chọn đồng ý dùng giá trị mới ngay.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      setSavedMessage(true);
    } catch (err: unknown) {
      // Phải báo lỗi: nếu im lặng thì người dùng tưởng đã lưu trong khi server chưa ghi nhận.
      setError(err instanceof Error ? err.message : 'Lưu cài đặt quyền riêng tư thất bại.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <main className="account-page">
      <div>
        <h1>Cài đặt quyền riêng tư</h1>
        <p className="muted">
          Bạn có toàn quyền kiểm soát dữ liệu cá nhân của mình trên Pickory. Bạn có thể thay đổi các
          lựa chọn này bất cứ lúc nào.
        </p>
      </div>

      {savedMessage && (
        <p role="status" className="alert alert-ok">
          Lựa chọn của bạn đã được lưu thành công!
        </p>
      )}

      {error && (
        <p role="alert" className="alert">
          {error}
        </p>
      )}

      {isLoading ? (
        <p role="status" className="empty-state">
          Đang tải cài đặt...
        </p>
      ) : (
        <form noValidate onSubmit={handleSave} className="card account-section">
          {/* Nhóm tùy chọn cookie bắt buộc */}
          <div className="consent-option">
            <div>
              <span className="consent-option-title">Cookie cần thiết (Necessary)</span>
              <span className="cell-sub">
                Duy trì phiên đăng nhập, giỏ hàng và bảo mật hệ thống. Luôn bật và không thể tắt.
              </span>
            </div>
            <span className="badge badge-info">Bắt buộc</span>
          </div>

          {/* Nhóm tùy chọn cá nhân hóa trải nghiệm */}
          <label className="check consent-option">
            <input
              type="checkbox"
              checked={personalization}
              onChange={(e) => setPersonalization(e.target.checked)}
            />
            <span>
              <span className="consent-option-title">
                Cá nhân hóa nội dung &amp; gợi ý (Personalization)
              </span>
              <span className="cell-sub">
                Cho phép hệ thống ghi nhớ hành vi xem và lịch sử tìm kiếm để gợi ý sản phẩm phù hợp
                với nhu cầu của bạn.
              </span>
            </span>
          </label>

          {/* Nhóm tùy chọn nhận thông tin tiếp thị */}
          <label className="check consent-option">
            <input
              type="checkbox"
              checked={marketing}
              onChange={(e) => setMarketing(e.target.checked)}
            />
            <span>
              <span className="consent-option-title">
                Thông tin ưu đãi &amp; marketing (Marketing)
              </span>
              <span className="cell-sub">
                Nhận thông báo về các chương trình khuyến mãi, giảm giá đặc biệt qua email.
              </span>
            </span>
          </label>

          <div>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Đang lưu...' : 'Lưu cài đặt quyền riêng tư'}
            </button>
          </div>
        </form>
      )}
    </main>
  );
}
