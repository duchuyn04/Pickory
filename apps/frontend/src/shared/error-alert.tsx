import type { ReactNode } from 'react';
import { Icon } from './icons';

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
  const alertClassName = className ? `alert alert-list ${className}` : 'alert alert-list';
  // Lỗi một dòng giữ chữ thường. Chỉ khi có danh sách bên dưới, dòng chính mới đậm để tách khỏi các lý do.
  const messageClassName = hasItems ? 'alert-list-title alert-list-heading' : 'alert-list-title';
  return (
    <div role="alert" className={alertClassName}>
      <Icon name="alertCircle" className="icon alert-list-icon" />
      <div className="alert-list-body">
        {message && <div className={messageClassName}>{message}</div>}
        {hasItems && (
          <ul className="alert-list-items">
            {items.map((item, index) => (
              // Hai lý do có thể trùng chữ, nên khóa thêm vị trí.
              <li key={`${index}-${item}`}>{item}</li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
