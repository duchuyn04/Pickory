import { useQueryClient } from '@tanstack/react-query';
import React, { useEffect, useState } from 'react';
import { apiRequest } from '../../shared/api';
import { useActor } from './use-actor';

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
    <div
      style={{
        minHeight: '100vh',
        backgroundColor: 'var(--color-surface-muted)',
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div
        style={{
          maxWidth: 640,
          margin: '0 auto',
          padding: 'var(--space-6) var(--space-4)',
        }}
      >
        <div
          style={{
            backgroundColor: 'var(--color-surface)',
            borderRadius: 'var(--radius-card)',
            border: '1px solid var(--color-border)',
            padding: 'var(--space-6)',
            boxShadow: '0 4px 20px rgba(0,0,0,0.06)',
          }}
        >
          <h1
            style={{
              fontSize: 'var(--text-xl)',
              color: 'var(--color-ink)',
              margin: '0 0 var(--space-2) 0',
              fontWeight: 700,
            }}
          >
            Cài đặt quyền riêng tư
          </h1>
          <p
            style={{
              color: 'var(--color-ink-muted)',
              fontSize: 'var(--text-sm)',
              margin: '0 0 var(--space-5) 0',
            }}
          >
            Bạn có toàn quyền kiểm soát dữ liệu cá nhân của mình trên Pickory. Bạn có thể thay đổi
            các lựa chọn này bất cứ lúc nào.
          </p>

          {savedMessage && (
            <div
              style={{
                backgroundColor: '#e6f4ea',
                border: '1px solid var(--color-success)',
                color: 'var(--color-success)',
                padding: 'var(--space-3)',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-sm)',
                marginBottom: 'var(--space-4)',
              }}
            >
              Lựa chọn của bạn đã được lưu thành công!
            </div>
          )}

          {error && (
            <div
              style={{
                backgroundColor: '#fee4e2',
                border: '1px solid var(--color-danger)',
                color: 'var(--color-danger)',
                padding: 'var(--space-3)',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-sm)',
                marginBottom: 'var(--space-4)',
              }}
            >
              {error}
            </div>
          )}

          {isLoading ? (
            <div>Đang tải cài đặt...</div>
          ) : (
            <form
              noValidate
              onSubmit={handleSave}
              style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}
            >
              {/* Nhóm tùy chọn cookie bắt buộc */}
              <div
                style={{
                  padding: 'var(--space-4)',
                  backgroundColor: 'var(--color-surface-muted)',
                  borderRadius: 'var(--radius-control)',
                  border: '1px solid var(--color-border)',
                }}
              >
                <div
                  style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}
                >
                  <div>
                    <div
                      style={{
                        fontWeight: 600,
                        color: 'var(--color-ink)',
                        fontSize: 'var(--text-md)',
                      }}
                    >
                      Cookie cần thiết (Necessary)
                    </div>
                    <div
                      style={{
                        fontSize: 'var(--text-xs)',
                        color: 'var(--color-ink-muted)',
                        marginTop: 'var(--space-1)',
                      }}
                    >
                      Duy trì phiên đăng nhập, giỏ hàng và bảo mật hệ thống. Luôn bật và không thể
                      tắt.
                    </div>
                  </div>
                  <span
                    style={{
                      fontSize: 'var(--text-xs)',
                      fontWeight: 600,
                      backgroundColor: 'var(--color-border)',
                      padding: 'var(--space-1) var(--space-2)',
                      borderRadius: 'var(--radius-pill)',
                    }}
                  >
                    Bắt buộc
                  </span>
                </div>
              </div>

              {/* Nhóm tùy chọn cá nhân hóa trải nghiệm */}
              <div
                style={{
                  padding: 'var(--space-4)',
                  borderRadius: 'var(--radius-control)',
                  border: '1px solid var(--color-border)',
                }}
              >
                <label
                  style={{
                    display: 'flex',
                    gap: 'var(--space-3)',
                    cursor: 'pointer',
                    alignItems: 'flex-start',
                  }}
                >
                  <input
                    type="checkbox"
                    checked={personalization}
                    onChange={(e) => setPersonalization(e.target.checked)}
                    style={{
                      marginTop: 'var(--space-1)',
                      width: 18,
                      height: 18,
                      accentColor: 'var(--color-brand)',
                    }}
                  />
                  <div>
                    <div
                      style={{
                        fontWeight: 600,
                        color: 'var(--color-ink)',
                        fontSize: 'var(--text-md)',
                      }}
                    >
                      Cá nhân hóa nội dung & gợi ý (Personalization)
                    </div>
                    <div
                      style={{
                        fontSize: 'var(--text-xs)',
                        color: 'var(--color-ink-muted)',
                        marginTop: 'var(--space-1)',
                      }}
                    >
                      Cho phép hệ thống ghi nhớ hành vi xem và lịch sử tìm kiếm để gợi ý sản phẩm
                      phù hợp với nhu cầu của bạn.
                    </div>
                  </div>
                </label>
              </div>

              {/* Nhóm tùy chọn nhận thông tin tiếp thị */}
              <div
                style={{
                  padding: 'var(--space-4)',
                  borderRadius: 'var(--radius-control)',
                  border: '1px solid var(--color-border)',
                }}
              >
                <label
                  style={{
                    display: 'flex',
                    gap: 'var(--space-3)',
                    cursor: 'pointer',
                    alignItems: 'flex-start',
                  }}
                >
                  <input
                    type="checkbox"
                    checked={marketing}
                    onChange={(e) => setMarketing(e.target.checked)}
                    style={{
                      marginTop: 'var(--space-1)',
                      width: 18,
                      height: 18,
                      accentColor: 'var(--color-brand)',
                    }}
                  />
                  <div>
                    <div
                      style={{
                        fontWeight: 600,
                        color: 'var(--color-ink)',
                        fontSize: 'var(--text-md)',
                      }}
                    >
                      Thông tin ưu đãi & marketing (Marketing)
                    </div>
                    <div
                      style={{
                        fontSize: 'var(--text-xs)',
                        color: 'var(--color-ink-muted)',
                        marginTop: 'var(--space-1)',
                      }}
                    >
                      Nhận thông báo về các chương trình khuyến mãi, giảm giá đặc biệt qua email.
                    </div>
                  </div>
                </label>
              </div>

              <button
                type="submit"
                disabled={saving}
                style={{
                  backgroundColor: 'var(--color-brand)',
                  color: '#ffffff',
                  border: 'none',
                  padding: 'var(--space-3)',
                  borderRadius: 'var(--radius-control)',
                  fontSize: 'var(--text-md)',
                  fontWeight: 600,
                  cursor: saving ? 'not-allowed' : 'pointer',
                  opacity: saving ? 0.7 : 1,
                  marginTop: 'var(--space-2)',
                }}
              >
                {saving ? 'Đang lưu...' : 'Lưu cài đặt quyền riêng tư'}
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}
