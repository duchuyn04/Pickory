import { describe, expect, it } from 'vitest';
import {
  buildVietQrUrl,
  formatPaymentCountdown,
} from '../../../../../../src/features/commerce/customer/checkout/payment-format';

describe('buildVietQrUrl', () => {
  it('mã hóa từng tham số và giữ nguyên mã đơn chữ hoa', () => {
    const url = buildVietQrUrl('0123&45', 'VC B', 123456, 'PKABC&12?');
    expect(url).toBe(
      'https://vietqr.app/img?acc=0123%2645&bank=VC+B&amount=123456&des=PKABC%2612%3F',
    );
    expect(new URL(url).searchParams.get('des')).toBe('PKABC&12?');
  });

  it('giữ số tiền nguyên ở tham số amount', () => {
    const url = buildVietQrUrl('001234', 'VCB', 200000, 'PKABCDEFGH');
    expect(new URL(url).searchParams.get('amount')).toBe('200000');
  });
});

describe('formatPaymentCountdown', () => {
  it('hiển thị số phút và giây còn lại theo hạn từ máy chủ', () => {
    expect(formatPaymentCountdown('2030-01-02T03:05:01Z', Date.parse('2030-01-02T03:00:00Z'))).toBe(
      '05:01',
    );
  });

  it('dừng ở 00:00 tại hạn và sau hạn', () => {
    const deadline = '2030-01-02T03:00:00Z';
    expect(formatPaymentCountdown(deadline, Date.parse(deadline))).toBe('00:00');
    expect(formatPaymentCountdown(deadline, Date.parse(deadline) + 1000)).toBe('00:00');
  });
});
