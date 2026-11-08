import { useState, type FormEvent } from 'react';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { Icon } from '../../../shared/icons';
import { submitSupportCsat } from './support-customer-api';

interface CsatLevel {
  score: number;
  label: string;
}

// Danh sách 5 mức điểm đánh giá kèm ý nghĩa hài lòng theo hợp đồng CSAT.
const CSAT_LEVELS: readonly CsatLevel[] = [
  { score: 1, label: 'Rất không hài lòng' },
  { score: 2, label: 'Không hài lòng' },
  { score: 3, label: 'Bình thường' },
  { score: 4, label: 'Hài lòng' },
  { score: 5, label: 'Rất hài lòng' },
];

interface CsatFormProps {
  conversationId: string | null;
}

// Form để khách đánh giá mức độ hài lòng về phiên hỗ trợ vừa kết thúc.
export function CsatForm({ conversationId }: CsatFormProps) {
  const [selectedScore, setSelectedScore] = useState<number | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [confirmedScore, setConfirmedScore] = useState<number | null>(null);

  // Khi đã gửi thành công, hiển thị xác nhận và giữ điểm số đã chấm.
  if (confirmedScore !== null) {
    const levelInfo = CSAT_LEVELS.find((level) => level.score === confirmedScore);
    const labelText = levelInfo ? levelInfo.label : '';

    return (
      <div className="support-message-action support-form-card" data-action="csat_form">
        <div role="status" className="alert alert-list alert-ok">
          <Icon name="check" className="icon alert-list-icon" />
          <div className="alert-list-body">
            <div className="alert-list-title">
              Cảm ơn bạn đã đánh giá {confirmedScore}/5 điểm ({labelText}). Ý kiến của bạn giúp
              Pickory nâng cao chất lượng dịch vụ.
            </div>
          </div>
        </div>
      </div>
    );
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    // Chặn bấm nhiều lần trong lúc request đang gửi lên máy chủ.
    if (isSubmitting || selectedScore === null) {
      return;
    }

    // Không gửi request khi chưa có mã hội thoại hợp lệ.
    if (!conversationId) {
      setSubmitError('Chưa xác định được hội thoại. Hãy tải lại trang.');
      return;
    }

    setSubmitError(null);
    setIsSubmitting(true);

    try {
      // Gửi điểm đánh giá CSAT cho phiên hỗ trợ của hội thoại.
      await submitSupportCsat(conversationId, selectedScore);
      setConfirmedScore(selectedScore);
    } catch (error) {
      // Giữ nguyên lựa chọn điểm khi gặp lỗi để khách không phải bấm lại.
      if (error instanceof ApiError) {
        setSubmitError(error.message);
      } else {
        setSubmitError('Đã có lỗi xảy ra khi gửi đánh giá. Hãy thử lại.');
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  const activeLevel = CSAT_LEVELS.find((level) => level.score === selectedScore);

  return (
    <div className="support-message-action support-form-card" data-action="csat_form">
      <h3 className="support-form-title">Đánh giá phiên hỗ trợ</h3>
      <p className="support-form-desc">Bạn có hài lòng với sự hỗ trợ của nhân viên CSKH không?</p>

      {submitError && <ErrorAlert message={submitError} className="support-form-alert" />}

      <form onSubmit={handleSubmit}>
        <div
          role="group"
          aria-label="Chọn mức điểm hài lòng từ 1 đến 5"
          className="csat-scores-row"
        >
          {CSAT_LEVELS.map(({ score, label }) => {
            const isSelected = selectedScore === score;
            return (
              <button
                key={score}
                type="button"
                className={`csat-score-btn${isSelected ? ' is-selected' : ''}`}
                aria-label={`${score} điểm - ${label}`}
                aria-pressed={isSelected}
                disabled={isSubmitting}
                onClick={() => setSelectedScore(score)}
              >
                {score}
              </button>
            );
          })}
        </div>

        {activeLevel && (
          <p className="csat-selected-label" aria-live="polite">
            Mức đánh giá: <strong>{activeLevel.label}</strong>
          </p>
        )}

        <button
          type="submit"
          className="btn btn-primary btn-block"
          disabled={selectedScore === null || isSubmitting}
        >
          {isSubmitting ? 'Đang gửi đánh giá...' : 'Gửi đánh giá'}
        </button>
      </form>
    </div>
  );
}
