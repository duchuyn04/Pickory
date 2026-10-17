import { describe, expect, it } from 'vitest';
import { buildNextSteps } from '../../../../../../src/features/commerce/customer/orders/order-next-steps';

function summarize(steps: ReturnType<typeof buildNextSteps>) {
  return steps.map((step) => `${step.key}:${step.state}`);
}

describe('các bước tiếp theo của đơn hàng', () => {
  it('đơn COD chờ xác nhận: bước xác nhận đang diễn ra, giao và nhận sắp tới', () => {
    const steps = buildNextSteps({
      status: 'pending_confirmation',
      paymentMethod: 'cod',
      paymentStatus: 'unpaid',
    });

    expect(summarize(steps)).toEqual(['confirm:current', 'ship:upcoming', 'receive:upcoming']);
  });

  it('đơn COD không có bước thanh toán riêng và nhắc thanh toán khi nhận hàng', () => {
    const steps = buildNextSteps({
      status: 'pending_confirmation',
      paymentMethod: 'cod',
      paymentStatus: 'unpaid',
    });

    expect(steps.some((step) => step.key === 'payment')).toBe(false);
    expect(steps.find((step) => step.key === 'receive')?.description).toContain(
      'thanh toán khi nhận hàng',
    );
  });

  it('đơn chuyển khoản đã nhận tiền: bước thanh toán đã xong', () => {
    const steps = buildNextSteps({
      status: 'pending_confirmation',
      paymentMethod: 'sepay',
      paymentStatus: 'paid',
    });

    expect(summarize(steps)).toEqual([
      'payment:done',
      'confirm:current',
      'ship:upcoming',
      'receive:upcoming',
    ]);
    expect(steps[0]?.description).toBe('Shop đã nhận tiền chuyển khoản.');
  });

  it('đơn chuyển khoản chưa nhận tiền: bước thanh toán đang diễn ra', () => {
    const steps = buildNextSteps({
      status: 'pending_payment',
      paymentMethod: 'sepay',
      paymentStatus: 'unpaid',
    });

    expect(summarize(steps)).toEqual([
      'payment:current',
      'confirm:upcoming',
      'ship:upcoming',
      'receive:upcoming',
    ]);
  });

  it.each([
    ['confirmed', ['confirm:done', 'ship:current', 'receive:upcoming']],
    ['shipping', ['confirm:done', 'ship:current', 'receive:upcoming']],
    ['delivered', ['confirm:done', 'ship:done', 'receive:done']],
  ])('đơn COD ở trạng thái %s', (status, expected) => {
    const steps = buildNextSteps({ status, paymentMethod: 'cod', paymentStatus: 'unpaid' });

    expect(summarize(steps)).toEqual(expected);
  });

  it.each(['cancelled', 'returned'])('không có bước nào khi đơn ở trạng thái %s', (status) => {
    expect(buildNextSteps({ status, paymentMethod: 'cod', paymentStatus: 'unpaid' })).toEqual([]);
  });
});
