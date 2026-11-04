// Chỗ đặt form của tin hệ thống. SD-UI6 thay phần tử này bằng form thật theo `action`.
interface SupportMessageActionProps {
  action: 'callback_form' | 'csat_form';
}

// Không hiện chữ kỹ thuật cho khách. `data-action` để test và SD-UI6 xác định loại form.
export function SupportMessageAction({ action }: SupportMessageActionProps) {
  return <div className="support-message-action" data-action={action} />;
}
