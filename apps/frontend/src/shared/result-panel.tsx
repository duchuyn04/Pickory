import type { ReactNode } from 'react';
import { Icon, type IconName } from './icons';
import './result-panel.css';

export type ResultTone = 'success' | 'info' | 'error';

// Mỗi loại có icon mặc định riêng. Màu của vòng tròn đi theo loại, nên không chỉ dựa vào màu để phân biệt.
const DEFAULT_ICON_BY_TONE: Record<ResultTone, IconName> = {
  success: 'check',
  info: 'info',
  error: 'alertCircle',
};

interface ResultIconProps {
  tone: ResultTone;
  icon?: IconName;
}

// Vòng tròn icon trạng thái. Icon chỉ trang trí, vì tiêu đề đã nói rõ kết quả.
export function ResultIcon({ tone, icon }: ResultIconProps) {
  return (
    <span className={`result-panel-icon result-panel-icon-${tone}`} aria-hidden="true">
      <Icon name={icon ?? DEFAULT_ICON_BY_TONE[tone]} />
    </span>
  );
}

interface ResultPanelProps {
  tone: ResultTone;
  icon?: IconName;
  title: string;
  // Cấp thẻ tiêu đề. Màn đã có `h1` riêng (ví dụ thẻ tài khoản) dùng cấp 2.
  headingLevel?: 1 | 2;
  description?: ReactNode;
  // Bật khi mô tả là kết quả vừa xảy ra sau thao tác của người dùng, để trình đọc màn hình đọc ngay.
  announceDescription?: boolean;
  actions?: ReactNode;
  // Bọc trong thẻ riêng khi màn không nằm sẵn trong một thẻ.
  framed?: boolean;
}

// Khối kết quả dùng chung: icon trạng thái, tiêu đề, mô tả và khu nút.
export function ResultPanel({
  tone,
  icon,
  title,
  headingLevel = 2,
  description,
  announceDescription = false,
  actions,
  framed = false,
}: ResultPanelProps) {
  const Heading = headingLevel === 1 ? 'h1' : 'h2';
  // Lỗi cần ngắt lời trình đọc màn hình. Thành công và thông tin chỉ cần đọc khi rảnh.
  const descriptionRole = announceDescription ? (tone === 'error' ? 'alert' : 'status') : undefined;
  const className = framed ? 'card result-panel' : 'result-panel';

  return (
    <div className={className}>
      <ResultIcon tone={tone} icon={icon} />
      <Heading className="result-panel-title">{title}</Heading>
      {description && (
        <p className="result-panel-description" role={descriptionRole}>
          {description}
        </p>
      )}
      {actions && <div className="result-panel-actions">{actions}</div>}
    </div>
  );
}
