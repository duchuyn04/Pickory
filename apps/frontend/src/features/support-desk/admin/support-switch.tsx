import './support-switch.css';

interface SupportSwitchProps {
  label: string;
  checked: boolean;
  onToggle: () => void;
  // Dùng `aria-disabled` thay cho `disabled`. Nút `disabled` làm focus bàn phím rơi khỏi công tắc lúc đang lưu.
  isBusy?: boolean;
}

// Công tắc bật tắt theo mẫu `role="switch"`. Tên truy cập là nhãn, còn trạng thái nằm ở `aria-checked`.
export function SupportSwitch({ label, checked, onToggle, isBusy = false }: SupportSwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      className="support-switch"
      aria-checked={checked}
      aria-disabled={isBusy || undefined}
      onClick={() => {
        // Đang bận thì bỏ qua lần bấm. Hai lệnh `PUT` chồng nhau có thể về sai thứ tự.
        if (!isBusy) onToggle();
      }}
    >
      <span className="support-switch-track" aria-hidden="true">
        <span className="support-switch-thumb" />
      </span>
      <span>{label}</span>
    </button>
  );
}
