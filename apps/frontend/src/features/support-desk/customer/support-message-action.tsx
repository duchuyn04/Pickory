import { CallbackRequestForm } from './callback-request-form';
import { CsatForm } from './csat-form';

interface SupportMessageActionProps {
  action: 'callback_form' | 'csat_form';
  conversationId?: string | null;
}

// Render form thật theo hành động của tin hệ thống trong khung chat.
export function SupportMessageAction({ action, conversationId = null }: SupportMessageActionProps) {
  // Tin hệ thống yêu cầu khách để lại số điện thoại để gọi lại sau.
  if (action === 'callback_form') {
    return <CallbackRequestForm conversationId={conversationId} />;
  }

  // Tin hệ thống mời khách đánh giá mức độ hài lòng về phiên hỗ trợ.
  if (action === 'csat_form') {
    return <CsatForm conversationId={conversationId} />;
  }

  return null;
}
