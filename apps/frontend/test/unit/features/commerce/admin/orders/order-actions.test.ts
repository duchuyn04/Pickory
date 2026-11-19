import { describe, expect, it } from 'vitest';
import {
  availableOrderActions,
  type OrderActionSubject,
} from '../../../../../../src/features/commerce/admin/orders/order-actions';

const everyPermission = () => true;
const noPermission = () => false;

function orderIn(overrides: Partial<OrderActionSubject> = {}): OrderActionSubject {
  return {
    status: 'pending_confirmation',
    paymentStatus: 'unpaid',
    paymentMethod: 'cod',
    flags: [],
    invoice: null,
    shipment: null,
    ...overrides,
  };
}

function permissions(...granted: string[]) {
  return (permission: string) => granted.includes(permission);
}

describe('availableOrderActions: xác nhận đơn', () => {
  it('hiện khi đơn chờ xác nhận và có orders:update-status', () => {
    expect(availableOrderActions(orderIn(), permissions('orders:update-status'))).toEqual([
      'confirm',
    ]);
  });

  it.each(['pending_payment', 'confirmed', 'shipping', 'delivered', 'cancelled', 'returned'])(
    'không hiện khi đơn ở trạng thái %s',
    (status) => {
      const result = availableOrderActions(
        orderIn({ status }),
        permissions('orders:update-status'),
      );

      expect(result).not.toContain('confirm');
    },
  );

  it('không hiện khi thiếu quyền', () => {
    expect(availableOrderActions(orderIn(), permissions('orders:read'))).toEqual([]);
  });
});

describe('availableOrderActions: hủy đơn', () => {
  it.each(['pending_payment', 'pending_confirmation', 'confirmed'])(
    'hiện khi đơn %s và có orders:cancel',
    (status) => {
      const result = availableOrderActions(orderIn({ status }), permissions('orders:cancel'));

      expect(result).toContain('cancel');
    },
  );

  it.each(['shipping', 'delivered', 'cancelled', 'returned'])('không hiện khi đơn %s', (status) => {
    const result = availableOrderActions(orderIn({ status }), permissions('orders:cancel'));

    expect(result).not.toContain('cancel');
  });

  it('không hiện khi thiếu orders:cancel dù có quyền khác', () => {
    expect(availableOrderActions(orderIn(), permissions('orders:update-status'))).not.toContain(
      'cancel',
    );
  });
});

describe('availableOrderActions: hóa đơn', () => {
  const invoice = { companyName: 'Công ty ABC' };

  it('hiện khi đơn có hóa đơn, chưa có cờ invoice_issued và có quyền', () => {
    const result = availableOrderActions(orderIn({ invoice }), permissions('orders:update-status'));

    expect(result).toContain('issueInvoice');
  });

  it('không hiện khi đơn không có hóa đơn', () => {
    expect(availableOrderActions(orderIn(), permissions('orders:update-status'))).not.toContain(
      'issueInvoice',
    );
  });

  it('không hiện khi đã có cờ invoice_issued', () => {
    const result = availableOrderActions(
      orderIn({ invoice, flags: ['invoice_issued'] }),
      permissions('orders:update-status'),
    );

    expect(result).not.toContain('issueInvoice');
  });

  it.each(['cancelled', 'returned'])('không hiện khi đơn %s', (status) => {
    const result = availableOrderActions(
      orderIn({ invoice, status }),
      permissions('orders:update-status'),
    );

    expect(result).not.toContain('issueInvoice');
  });
});

describe('availableOrderActions: vận đơn', () => {
  it('hiện nút tạo vận đơn khi đơn confirmed và có orders:fulfill', () => {
    const result = availableOrderActions(
      orderIn({ status: 'confirmed' }),
      permissions('orders:fulfill'),
    );

    expect(result).toEqual(['startManualShipping', 'createShipment']);
  });

  it('không hiện nút tạo vận đơn khi đơn chờ xác nhận', () => {
    expect(availableOrderActions(orderIn(), permissions('orders:fulfill'))).toEqual([]);
  });

  it('hiện nút hủy vận đơn khi đơn shipping, có vận đơn và có orders:fulfill', () => {
    const shipment = { trackingCode: 'GHN1' };
    const result = availableOrderActions(
      orderIn({ status: 'shipping', shipment }),
      permissions('orders:fulfill'),
    );

    expect(result).toEqual(['cancelShipment']);
  });

  it('không hiện nút hủy vận đơn khi đơn shipping nhưng không có vận đơn', () => {
    const result = availableOrderActions(
      orderIn({ status: 'shipping' }),
      permissions('orders:fulfill'),
    );

    expect(result).toEqual([]);
  });
});

describe('availableOrderActions: giao hàng thủ công', () => {
  it('hiện nút bắt đầu giao thủ công khi đơn confirmed và có orders:fulfill', () => {
    const result = availableOrderActions(
      orderIn({ status: 'confirmed' }),
      permissions('orders:fulfill'),
    );

    expect(result).toContain('startManualShipping');
  });

  it.each(['pending_confirmation', 'shipping', 'delivered', 'cancelled', 'returned'])(
    'không hiện nút bắt đầu giao thủ công khi đơn ở trạng thái %s',
    (status) => {
      const result = availableOrderActions(orderIn({ status }), permissions('orders:fulfill'));

      expect(result).not.toContain('startManualShipping');
    },
  );

  it('hiện nút quay về đã xác nhận khi đơn shipping có cờ manual_shipping', () => {
    const result = availableOrderActions(
      orderIn({ status: 'shipping', flags: ['manual_shipping'] }),
      permissions('orders:fulfill'),
    );

    expect(result).toEqual(['returnToConfirmed']);
  });

  it('không hiện nút quay về khi đơn shipping không có cờ, dù có hay không có vận đơn', () => {
    const withShipment = orderIn({ status: 'shipping', shipment: { trackingCode: 'GHN1' } });
    const withoutShipment = orderIn({ status: 'shipping' });

    expect(availableOrderActions(withShipment, permissions('orders:fulfill'))).not.toContain(
      'returnToConfirmed',
    );
    expect(availableOrderActions(withoutShipment, permissions('orders:fulfill'))).not.toContain(
      'returnToConfirmed',
    );
  });

  it.each(['pending_confirmation', 'confirmed', 'delivered', 'cancelled', 'returned'])(
    'không hiện nút quay về khi đơn ở trạng thái %s dù có cờ manual_shipping',
    (status) => {
      const result = availableOrderActions(
        orderIn({ status, flags: ['manual_shipping'] }),
        permissions('orders:fulfill'),
      );

      expect(result).not.toContain('returnToConfirmed');
    },
  );

  it('đơn shipping có cờ và có vận đơn hiện cả hủy vận đơn và quay về, theo thứ tự', () => {
    const result = availableOrderActions(
      orderIn({
        status: 'shipping',
        flags: ['manual_shipping'],
        shipment: { trackingCode: 'GHN1' },
      }),
      permissions('orders:fulfill'),
    );

    expect(result).toEqual(['cancelShipment', 'returnToConfirmed']);
  });

  it.each([['orders:read'], ['orders:update-status'], ['orders:cancel']])(
    'không hiện hai nút khi chỉ có quyền %s',
    (permission) => {
      const confirmed = orderIn({ status: 'confirmed' });
      const shipping = orderIn({ status: 'shipping', flags: ['manual_shipping'] });

      expect(availableOrderActions(confirmed, permissions(permission))).not.toContain(
        'startManualShipping',
      );
      expect(availableOrderActions(shipping, permissions(permission))).not.toContain(
        'returnToConfirmed',
      );
    },
  );

  it('đơn confirmed có đủ quyền hiện đúng tập nút theo thứ tự', () => {
    const result = availableOrderActions(orderIn({ status: 'confirmed' }), everyPermission);

    expect(result).toEqual(['cancel', 'startManualShipping', 'createShipment']);
  });
});

describe('availableOrderActions: nhận tiền COD', () => {
  it('hiện khi COD, đơn đã giao, chưa thanh toán và có payments:reconcile', () => {
    const result = availableOrderActions(
      orderIn({ status: 'delivered' }),
      permissions('payments:reconcile'),
    );

    expect(result).toEqual(['receiveCod']);
  });

  it('không hiện khi phương thức là sepay', () => {
    const result = availableOrderActions(
      orderIn({ status: 'delivered', paymentMethod: 'sepay' }),
      permissions('payments:reconcile'),
    );

    expect(result).toEqual([]);
  });

  it('không hiện khi đơn chưa giao', () => {
    const result = availableOrderActions(
      orderIn({ status: 'shipping' }),
      permissions('payments:reconcile'),
    );

    expect(result).toEqual([]);
  });

  it('không hiện khi đã thanh toán', () => {
    const result = availableOrderActions(
      orderIn({ status: 'delivered', paymentStatus: 'paid' }),
      permissions('payments:reconcile'),
    );

    expect(result).toEqual([]);
  });
});

describe('availableOrderActions: đã hoàn tiền', () => {
  it('hiện khi thanh toán refund_pending và có refunds:manage', () => {
    const result = availableOrderActions(
      orderIn({ status: 'cancelled', paymentStatus: 'refund_pending', paymentMethod: 'sepay' }),
      permissions('refunds:manage'),
    );

    expect(result).toEqual(['refund']);
  });

  it('không hiện khi chỉ có payments:reconcile', () => {
    const result = availableOrderActions(
      orderIn({ status: 'cancelled', paymentStatus: 'refund_pending', paymentMethod: 'sepay' }),
      permissions('payments:reconcile'),
    );

    expect(result).toEqual([]);
  });

  it('không hiện khi đã hoàn tiền', () => {
    const result = availableOrderActions(
      orderIn({ status: 'cancelled', paymentStatus: 'refunded' }),
      permissions('refunds:manage'),
    );

    expect(result).toEqual([]);
  });
});

describe('availableOrderActions: tổng hợp', () => {
  it('không có quyền nào thì không có nút nào', () => {
    expect(availableOrderActions(orderIn(), noPermission)).toEqual([]);
  });

  it('đơn chờ xác nhận có hóa đơn và đủ quyền hiện hủy đơn, hóa đơn rồi xác nhận', () => {
    const result = availableOrderActions(
      orderIn({ invoice: { companyName: 'ABC' } }),
      everyPermission,
    );

    expect(result).toEqual(['cancel', 'issueInvoice', 'confirm']);
  });
});
