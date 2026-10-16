// Từ số báo cáo này trở lên, đánh giá có nhãn "Ưu tiên". Khớp `PriorityReportThreshold` của backend.
export const PRIORITY_REPORT_THRESHOLD = 3;

// Mã lý do ẩn. Khớp `ReviewModerationRequest.HideReasons` của backend.
export const HIDE_REASON_CODES = [
  'spam',
  'offensive',
  'personal_info',
  'off_topic',
  'misleading',
] as const;

// Mã lý do hiện lại. Khớp `ReviewModerationRequest.ShowReasons` của backend.
export const SHOW_REASON_CODES = ['report_rejected', 'hidden_by_mistake'] as const;

export type ModerationAction = 'hide' | 'show';

// Bảng nhãn duy nhất cho mã lý do. Hộp thoại và cột trạng thái cùng dùng bảng này.
const REASON_LABELS: Record<string, string> = {
  spam: 'Spam hoặc quảng cáo',
  offensive: 'Ngôn từ xúc phạm',
  personal_info: 'Chứa thông tin cá nhân',
  off_topic: 'Không liên quan đến sản phẩm',
  misleading: 'Sai sự thật, gây hiểu lầm',
  report_rejected: 'Báo cáo không đúng',
  hidden_by_mistake: 'Ẩn nhầm',
};

const REASON_REQUIRED_MESSAGE = 'Chọn lý do';

// Nhãn tiếng Việt của mã lý do. Mã lạ hiện nguyên mã, để backend thêm mã mới không làm vỡ trang.
export function formatReasonLabel(code: string | null): string {
  if (code === null) return '';
  return REASON_LABELS[code] ?? code;
}

// Danh sách mã mà hộp thoại cho chọn theo hành động.
export function reasonCodesFor(action: ModerationAction): readonly string[] {
  return action === 'hide' ? HIDE_REASON_CODES : SHOW_REASON_CODES;
}

export function isPriorityReview(reportCount: number): boolean {
  return reportCount >= PRIORITY_REPORT_THRESHOLD;
}

// Kiểm lựa chọn lý do trước khi gửi. Trả câu lỗi, hoặc `null` khi hợp lệ.
export function validateReasonChoice(action: ModerationAction, reason: string): string | null {
  // Mã không thuộc danh sách của hành động cũng coi là chưa chọn, vì backend sẽ trả 400.
  return reasonCodesFor(action).includes(reason) ? null : REASON_REQUIRED_MESSAGE;
}
