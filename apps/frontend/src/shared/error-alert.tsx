import type { ReactNode } from 'react';
import { Icon, type IconName } from './icons';

interface AlertLayoutProps {
  icon: IconName;
  className?: string;
  children: ReactNode;
}

// Dùng một khung cho lỗi và cảnh báo để chữ xuống hàng luôn thẳng dưới dòng đầu.
export function AlertLayout({ icon, className, children }: AlertLayoutProps) {
  const alertClassName = className ? `alert alert-list ${className}` : 'alert alert-list';
  return (
    <div role="alert" className={alertClassName}>
      <Icon name={icon} className="icon alert-list-icon" />
      <div className="alert-list-body">{children}</div>
    </div>
  );
}

interface ErrorAlertProps {
  // Nội dung chính của lỗi. Nhận chuỗi hoặc phần tử con khi chữ có phần ghép.
  message?: ReactNode;
  // Mỗi phần tử là một lý do, hiện thành một dòng riêng dưới `message`.
  items?: string[];
  // Class bố cục riêng của chỗ dùng, ví dụ lề ngoài. Không đặt màu hay cỡ chữ ở đây.
  className?: string;
}

// Thông báo lỗi có icon, nội dung và danh sách lý do không chấm tròn. Mẫu `.alert-list` trong `components.css`.
export function ErrorAlert({ message, items = [], className }: ErrorAlertProps) {
  const hasItems = items.length > 0;
  // Lỗi một dòng giữ chữ thường. Chỉ khi có danh sách bên dưới, dòng chính mới đậm để tách khỏi các lý do.
  const messageClassName = hasItems ? 'alert-list-title alert-list-heading' : 'alert-list-title';
  return (
    <AlertLayout icon="alertCircle" className={className}>
      {message && <div className={messageClassName}>{message}</div>}
      {hasItems && (
        <ul className="alert-list-items">
          {items.map((item, index) => (
            // Hai lý do có thể trùng chữ, nên khóa thêm vị trí.
            <li key={`${index}-${item}`}>{item}</li>
          ))}
        </ul>
      )}
    </AlertLayout>
  );
}
