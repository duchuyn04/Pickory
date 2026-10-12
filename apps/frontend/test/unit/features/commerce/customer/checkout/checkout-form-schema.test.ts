import { describe, expect, it } from 'vitest';
import {
  NOTE_MAX_LENGTH,
  validateCheckoutForm,
  type CheckoutFormValues,
} from '../../../../../../src/features/commerce/customer/checkout/checkout-form-schema';

// Form hợp lệ làm gốc. Mỗi test chỉ đổi đúng một trường để biết trường nào gây lỗi.
function buildValues(overrides: Partial<CheckoutFormValues> = {}): CheckoutFormValues {
  return {
    contact: { name: 'Nguyễn Văn An', phone: '0912345678', email: 'an@example.com' },
    address: {
      provinceCode: '201',
      provinceName: 'Hà Nội',
      wardCode: '1A0101',
      wardName: 'Phường Ba Đình',
      line: '12 Phố Test',
    },
    note: '',
    paymentMethod: 'cod',
    wantsInvoice: false,
    invoice: { companyName: '', taxCode: '', address: '', email: '' },
    ...overrides,
  };
}

function withContact(contact: Partial<CheckoutFormValues['contact']>): CheckoutFormValues {
  return buildValues({ contact: { ...buildValues().contact, ...contact } });
}

function withAddress(address: Partial<CheckoutFormValues['address']>): CheckoutFormValues {
  return buildValues({ address: { ...buildValues().address, ...address } });
}

function withInvoice(invoice: Partial<CheckoutFormValues['invoice']>): CheckoutFormValues {
  return buildValues({
    wantsInvoice: true,
    invoice: {
      companyName: 'Công ty TNHH Pickory',
      taxCode: '0312345678',
      address: '1 Đường Công Ty, Quận 1',
      email: 'ketoan@example.com',
      ...invoice,
    },
  });
}

describe('validateCheckoutForm: form hợp lệ', () => {
  it('không báo lỗi khi mọi trường đúng và không xuất hóa đơn', () => {
    expect(validateCheckoutForm(buildValues())).toEqual({});
  });
});

describe('validateCheckoutForm: liên hệ', () => {
  it('bắt buộc nhập họ tên, chuỗi chỉ có khoảng trắng cũng là trống', () => {
    expect(validateCheckoutForm(withContact({ name: '   ' }))['contact.name']).toBe(
      'Cần nhập họ và tên.',
    );
  });

  it('nhận họ tên đúng 100 ký tự và từ chối 101 ký tự', () => {
    expect(
      validateCheckoutForm(withContact({ name: 'a'.repeat(100) }))['contact.name'],
    ).toBeUndefined();
    expect(validateCheckoutForm(withContact({ name: 'a'.repeat(101) }))['contact.name']).toBe(
      'Họ và tên tối đa 100 ký tự.',
    );
  });

  it('từ chối ký tự điều khiển trong họ tên', () => {
    expect(validateCheckoutForm(withContact({ name: 'An\u0007' }))['contact.name']).toBe(
      'Họ và tên chứa ký tự không hợp lệ.',
    );
  });

  it('bắt buộc nhập số điện thoại', () => {
    expect(validateCheckoutForm(withContact({ phone: '' }))['contact.phone']).toBe(
      'Cần nhập số điện thoại.',
    );
  });

  it.each(['0912345678', '+84 912 345 678', '091-234.5678'])('nhận số điện thoại %s', (phone) => {
    expect(validateCheckoutForm(withContact({ phone }))['contact.phone']).toBeUndefined();
  });

  it.each(['0212345678', '091234567', '09123456789', 'abc'])(
    'từ chối số điện thoại %s',
    (phone) => {
      expect(validateCheckoutForm(withContact({ phone }))['contact.phone']).toBe(
        'Số điện thoại không đúng định dạng.',
      );
    },
  );

  it('bắt buộc nhập email và kiểm định dạng', () => {
    expect(validateCheckoutForm(withContact({ email: '' }))['contact.email']).toBe(
      'Cần nhập email.',
    );
    expect(validateCheckoutForm(withContact({ email: 'an@example' }))['contact.email']).toBe(
      'Email không đúng định dạng.',
    );
  });

  it('từ chối email dài hơn 254 ký tự', () => {
    const longEmail = `${'a'.repeat(250)}@example.com`;
    expect(validateCheckoutForm(withContact({ email: longEmail }))['contact.email']).toBe(
      'Email không đúng định dạng.',
    );
  });
});

describe('validateCheckoutForm: địa chỉ', () => {
  it('bắt buộc chọn tỉnh, phường và nhập số nhà', () => {
    const errors = validateCheckoutForm(
      withAddress({ provinceCode: '', wardCode: '', line: '  ' }),
    );
    expect(errors['address.provinceCode']).toBe('Cần chọn tỉnh/thành phố.');
    expect(errors['address.wardCode']).toBe('Cần chọn phường/xã.');
    expect(errors['address.line']).toBe('Cần nhập số nhà, tên đường.');
  });

  it('nhận số nhà đúng 200 ký tự và từ chối 201 ký tự', () => {
    expect(
      validateCheckoutForm(withAddress({ line: 'a'.repeat(200) }))['address.line'],
    ).toBeUndefined();
    expect(validateCheckoutForm(withAddress({ line: 'a'.repeat(201) }))['address.line']).toBe(
      'Số nhà, tên đường tối đa 200 ký tự.',
    );
  });
});

describe('validateCheckoutForm: ghi chú', () => {
  it('nhận ghi chú đúng 500 ký tự và từ chối 501 ký tự', () => {
    expect(
      validateCheckoutForm(buildValues({ note: 'a'.repeat(NOTE_MAX_LENGTH) })).note,
    ).toBeUndefined();
    expect(validateCheckoutForm(buildValues({ note: 'a'.repeat(NOTE_MAX_LENGTH + 1) })).note).toBe(
      'Ghi chú tối đa 500 ký tự.',
    );
  });

  it('cắt khoảng trắng đầu và cuối trước khi đếm, giống backend', () => {
    const note = `  ${'a'.repeat(NOTE_MAX_LENGTH)}  `;
    expect(validateCheckoutForm(buildValues({ note })).note).toBeUndefined();
  });
});

describe('validateCheckoutForm: hóa đơn VAT', () => {
  it('bỏ qua mọi ô hóa đơn khi khách không đánh dấu xuất hóa đơn', () => {
    const values = buildValues({
      wantsInvoice: false,
      invoice: { companyName: '', taxCode: 'sai', address: '', email: 'sai' },
    });
    expect(validateCheckoutForm(values)).toEqual({});
  });

  it('bắt buộc đủ 4 ô khi đánh dấu xuất hóa đơn', () => {
    const errors = validateCheckoutForm(
      withInvoice({ companyName: '', taxCode: '', address: '', email: '' }),
    );
    expect(errors['invoice.companyName']).toBe('Cần nhập tên công ty.');
    expect(errors['invoice.taxCode']).toBe('Cần nhập mã số thuế.');
    expect(errors['invoice.address']).toBe('Cần nhập địa chỉ hóa đơn.');
    expect(errors['invoice.email']).toBe('Cần nhập email nhận hóa đơn.');
  });

  it.each(['0312345678', '0312345678-001', '0312345678001', ' 0312345678 '])(
    'nhận mã số thuế %s',
    (taxCode) => {
      expect(validateCheckoutForm(withInvoice({ taxCode }))['invoice.taxCode']).toBeUndefined();
    },
  );

  it.each([
    '031234567',
    '03123456789',
    '031234567890',
    '03123456789012',
    '031234567A',
    '０３１２３４５６７８',
  ])('từ chối mã số thuế %s', (taxCode) => {
    expect(validateCheckoutForm(withInvoice({ taxCode }))['invoice.taxCode']).toBe(
      'Mã số thuế gồm 10 hoặc 13 chữ số, có thể có dấu gạch ngang.',
    );
  });

  it('giới hạn độ dài tên công ty 200 và địa chỉ hóa đơn 300', () => {
    expect(
      validateCheckoutForm(withInvoice({ companyName: 'a'.repeat(200) }))['invoice.companyName'],
    ).toBeUndefined();
    expect(
      validateCheckoutForm(withInvoice({ companyName: 'a'.repeat(201) }))['invoice.companyName'],
    ).toBe('Tên công ty tối đa 200 ký tự.');
    expect(
      validateCheckoutForm(withInvoice({ address: 'a'.repeat(300) }))['invoice.address'],
    ).toBeUndefined();
    expect(validateCheckoutForm(withInvoice({ address: 'a'.repeat(301) }))['invoice.address']).toBe(
      'Địa chỉ hóa đơn tối đa 300 ký tự.',
    );
  });

  it('kiểm định dạng email nhận hóa đơn', () => {
    expect(validateCheckoutForm(withInvoice({ email: 'ketoan@' }))['invoice.email']).toBe(
      'Email nhận hóa đơn không đúng định dạng.',
    );
  });
});
