import { describe, expect, it } from 'vitest';
import {
  MANUAL_SHIPPING_NOTICE,
  buildNextSteps,
} from '../../../../../../src/features/commerce/customer/orders/order-next-steps';

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

  describe('đơn giao hàng thủ công', () => {
    const OLD_SHIP_TEXT =
      'Shop bàn giao đơn cho đơn vị vận chuyển. Mã vận đơn hiện trên trang đơn hàng.';
    const base = { paymentMethod: 'cod', paymentStatus: 'unpaid' };

    it('đơn shipping có trackingCode null hiện đúng chữ giao thủ công', () => {
      const steps = buildNextSteps({ ...base, status: 'shipping', trackingCode: null });
      const shipStep = steps.find((step) => step.key === 'ship');

      expect(shipStep?.description).toBe(MANUAL_SHIPPING_NOTICE);
      expect(shipStep?.description).toBe('Shop đang giao hàng thủ công. Không có mã vận đơn GHN.');
    });

    it('đơn shipping có mã vận đơn giữ chữ cũ', () => {
      const steps = buildNextSteps({ ...base, status: 'shipping', trackingCode: 'GHN-1' });

      expect(steps.find((step) => step.key === 'ship')?.description).toBe(OLD_SHIP_TEXT);
    });

    it('đơn shipping không có khóa trackingCode giữ chữ cũ', () => {
      const steps = buildNextSteps({ ...base, status: 'shipping' });

      expect(steps.find((step) => step.key === 'ship')?.description).toBe(OLD_SHIP_TEXT);
    });

    it('đơn shipping có trackingCode là chuỗi rỗng giữ chữ cũ', () => {
      const steps = buildNextSteps({ ...base, status: 'shipping', trackingCode: '' });

      expect(steps.find((step) => step.key === 'ship')?.description).toBe(OLD_SHIP_TEXT);
    });

    it('trạng thái và số bước của đơn giao thủ công giống đơn shipping thường', () => {
      const manual = buildNextSteps({ ...base, status: 'shipping', trackingCode: null });
      const normal = buildNextSteps({ ...base, status: 'shipping', trackingCode: 'GHN-1' });

      expect(manual.map(({ key, title, state }) => ({ key, title, state }))).toEqual(
        normal.map(({ key, title, state }) => ({ key, title, state })),
      );
    });

    it.each(['pending_confirmation', 'confirmed', 'delivered', 'cancelled', 'returned'])(
      'trạng thái %s có trackingCode null giữ nguyên danh sách bước',
      (status) => {
        expect(buildNextSteps({ ...base, status, trackingCode: null })).toEqual(
          buildNextSteps({ ...base, status }),
        );
      },
    );
  });
});
