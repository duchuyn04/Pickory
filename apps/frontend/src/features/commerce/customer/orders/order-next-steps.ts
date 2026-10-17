export type NextStepKey = 'payment' | 'confirm' | 'ship' | 'receive';
export type NextStepState = 'done' | 'current' | 'upcoming';

export interface NextStep {
  key: NextStepKey;
  title: string;
  description: string;
  state: NextStepState;
}

interface NextStepsInput {
  status: string;
  paymentMethod: string;
  paymentStatus: string;
}

// Trạng thái đơn đã qua bước xác nhận. Từ đây shop chuẩn bị giao hoặc đã giao.
const CONFIRMED_STATUSES = ['confirmed', 'shipping', 'delivered'];

// Tạo danh sách bước tiếp theo từ trạng thái thật của đơn. Đơn đã hủy hoặc đã trả không còn bước nào.
export function buildNextSteps({
  status,
  paymentMethod,
  paymentStatus,
}: NextStepsInput): NextStep[] {
  // Đơn đã hủy hoặc đã trả không đi tiếp, nên hiện các bước sẽ gây hiểu nhầm.
  if (status === 'cancelled' || status === 'returned') return [];

  const steps: NextStep[] = [];
  const isBankTransfer = paymentMethod === 'sepay';
  const isPaid = paymentStatus === 'paid';
  // Đơn COD không cần chờ tiền. Đơn chuyển khoản chỉ sang bước xác nhận khi đã nhận tiền.
  const canConfirm = !isBankTransfer || isPaid;

  if (isBankTransfer) {
    steps.push({
      key: 'payment',
      title: 'Thanh toán chuyển khoản',
      description: isPaid
        ? 'Shop đã nhận tiền chuyển khoản.'
        : 'Hoàn tất chuyển khoản trước hạn thanh toán của đơn.',
      state: isPaid ? 'done' : 'current',
    });
  }

  const isConfirmed = CONFIRMED_STATUSES.includes(status);
  const confirmState: NextStepState = isConfirmed ? 'done' : canConfirm ? 'current' : 'upcoming';
  steps.push({
    key: 'confirm',
    title: 'Shop xác nhận đơn',
    description: 'Shop kiểm tra đơn của bạn. Trạng thái đơn đổi sang Đã xác nhận.',
    state: confirmState,
  });

  const shipState: NextStepState =
    status === 'delivered'
      ? 'done'
      : status === 'confirmed' || status === 'shipping'
        ? 'current'
        : 'upcoming';
  steps.push({
    key: 'ship',
    title: 'Giao hàng',
    description: 'Shop bàn giao đơn cho đơn vị vận chuyển. Mã vận đơn hiện trên trang đơn hàng.',
    state: shipState,
  });

  steps.push({
    key: 'receive',
    title: 'Nhận hàng',
    description: isBankTransfer
      ? 'Bạn nhận hàng. Trạng thái đơn đổi sang Đã giao hàng.'
      : 'Bạn kiểm tra hàng và thanh toán khi nhận hàng.',
    state: status === 'delivered' ? 'done' : 'upcoming',
  });

  return steps;
}
