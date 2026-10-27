import { Link } from 'react-router-dom';
import { Icon } from '../../../shared/icons';
import './purchase-progress.css';

type PurchaseStage = 'cart' | 'checkout' | 'payment' | 'success';
type StepState = 'done' | 'current' | 'upcoming';

const steps = ['Giỏ hàng', 'Thông tin đặt hàng', 'Thanh toán và hoàn tất'];
const currentStepByStage: Record<PurchaseStage, number> = {
  cart: 1,
  checkout: 2,
  payment: 3,
  success: 4,
};
const stateLabels: Record<StepState, string> = {
  done: 'Đã xong',
  current: 'Đang thực hiện',
  upcoming: 'Chưa bắt đầu',
};

export function PurchaseProgress({ stage }: { stage: PurchaseStage }) {
  const currentStep = currentStepByStage[stage];

  return (
    <nav className="purchase-progress" aria-label="Tiến trình đặt hàng">
      <ol className="purchase-progress-list">
        {steps.map((label, index) => {
          const stepNumber = index + 1;
          const state: StepState =
            stepNumber < currentStep ? 'done' : stepNumber === currentStep ? 'current' : 'upcoming';
          const content = (
            <>
              <span className="purchase-progress-marker" aria-hidden="true">
                {state === 'done' ? <Icon name="check" /> : stepNumber}
              </span>
              <span className="purchase-progress-label">{label}</span>
              <span className="sr"> ({stateLabels[state]})</span>
            </>
          );

          return (
            <li
              key={label}
              className="purchase-progress-step"
              data-state={state}
              aria-current={state === 'current' ? 'step' : undefined}
            >
              {stepNumber === 1 && state === 'done' ? (
                <Link to="/cart" className="purchase-progress-link">
                  {content}
                </Link>
              ) : (
                <span className="purchase-progress-link">{content}</span>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
