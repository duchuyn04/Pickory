import { ErrorAlert } from '../../shared/error-alert';

interface SectionErrorProps {
  message: string;
  onRetry: () => void;
}

// Khối báo lỗi dùng chung cho từng phần trên trang chủ để khách thử tải lại riêng phần hỏng.
export function SectionError({ message, onRetry }: SectionErrorProps) {
  return (
    <div className="section-error">
      <ErrorAlert message={message} />
      <button type="button" className="btn btn-sm" onClick={onRetry}>
        Thử lại
      </button>
    </div>
  );
}
