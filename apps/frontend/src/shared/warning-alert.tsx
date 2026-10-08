import type { ReactNode } from 'react';
import { AlertLayout } from './error-alert';

interface WarningAlertProps {
  children: ReactNode;
}

// Giữ icon cảnh báo khác icon lỗi để người dùng phân biệt mức độ thông báo.
export function WarningAlert({ children }: WarningAlertProps) {
  return (
    <AlertLayout icon="alertTriangle" className="alert-warn">
      <div className="alert-list-title">{children}</div>
    </AlertLayout>
  );
}
