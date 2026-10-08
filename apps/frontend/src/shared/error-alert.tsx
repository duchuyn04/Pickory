import { Icon } from './icons';

interface ErrorAlertProps {
  // Dòng tóm tắt lỗi, in đậm. Bỏ trống khi chỉ có danh sách.
  title?: string;
  // Mỗi phần tử là một lý do, hiện thành một dòng riêng.
  items?: string[];
}

// Thông báo lỗi có icon, tiêu đề và danh sách lý do không chấm tròn. Mẫu `.alert-list` trong `components.css`.
export function ErrorAlert({ title, items = [] }: ErrorAlertProps) {
  return (
    <div role="alert" className="alert alert-list">
      <Icon name="alertCircle" className="icon alert-list-icon" />
      <div className="alert-list-body">
        {title && <div className="alert-list-title">{title}</div>}
        {items.length > 0 && (
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
