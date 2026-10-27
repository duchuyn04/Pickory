import { FieldError, fieldErrorProps } from '../../../../shared/field-error';
import type {
  CheckoutFieldErrors,
  CheckoutFieldName,
  CheckoutFormValues,
} from './checkout-form-schema';

interface CheckoutInvoiceSectionProps {
  wantsInvoice: boolean;
  values: CheckoutFormValues['invoice'];
  errors: CheckoutFieldErrors;
  fieldIds: Record<CheckoutFieldName, string>;
  isSubmitting: boolean;
  onToggle: (checked: boolean) => void;
  onChange: (field: 'companyName' | 'taxCode' | 'address' | 'email', value: string) => void;
  onBlur: (field: CheckoutFieldName) => void;
}

export function CheckoutInvoiceSection({
  wantsInvoice,
  values,
  errors,
  fieldIds,
  isSubmitting,
  onToggle,
  onChange,
  onBlur,
}: CheckoutInvoiceSectionProps) {
  return (
    <section className="card checkout-section" aria-labelledby="checkout-invoice-title">
      <h2 id="checkout-invoice-title">
        <span className="checkout-section-number" data-step="4" aria-hidden="true" />
        <span>Hóa đơn VAT</span>
        <span className="checkout-section-optional">Không bắt buộc</span>
      </h2>
      <label className="checkout-radio checkout-invoice-toggle">
        <input
          id={fieldIds.invoice}
          type="checkbox"
          checked={wantsInvoice}
          disabled={isSubmitting}
          onChange={(event) => onToggle(event.target.checked)}
        />
        <span>Xuất hóa đơn công ty</span>
      </label>
      <FieldError inputId={fieldIds.invoice} message={errors.invoice} />

      {wantsInvoice && (
        <div className="form-grid form-grid-2 checkout-invoice-fields">
          <div className="field field-full">
            <label htmlFor={fieldIds['invoice.companyName']}>Tên công ty</label>
            <input
              id={fieldIds['invoice.companyName']}
              type="text"
              autoComplete="organization"
              disabled={isSubmitting}
              value={values.companyName}
              onChange={(event) => onChange('companyName', event.target.value)}
              onBlur={() => onBlur('invoice.companyName')}
              {...fieldErrorProps(fieldIds['invoice.companyName'], errors['invoice.companyName'])}
            />
            <FieldError
              inputId={fieldIds['invoice.companyName']}
              message={errors['invoice.companyName']}
            />
          </div>
          <div className="field">
            <label htmlFor={fieldIds['invoice.taxCode']}>Mã số thuế</label>
            <input
              id={fieldIds['invoice.taxCode']}
              type="text"
              inputMode="numeric"
              autoComplete="off"
              disabled={isSubmitting}
              value={values.taxCode}
              onChange={(event) => onChange('taxCode', event.target.value)}
              onBlur={() => onBlur('invoice.taxCode')}
              {...fieldErrorProps(fieldIds['invoice.taxCode'], errors['invoice.taxCode'])}
            />
            <FieldError inputId={fieldIds['invoice.taxCode']} message={errors['invoice.taxCode']} />
          </div>
          <div className="field">
            <label htmlFor={fieldIds['invoice.email']}>Email nhận hóa đơn</label>
            <input
              id={fieldIds['invoice.email']}
              type="email"
              autoComplete="off"
              disabled={isSubmitting}
              value={values.email}
              onChange={(event) => onChange('email', event.target.value)}
              onBlur={() => onBlur('invoice.email')}
              {...fieldErrorProps(fieldIds['invoice.email'], errors['invoice.email'])}
            />
            <FieldError inputId={fieldIds['invoice.email']} message={errors['invoice.email']} />
          </div>
          <div className="field field-full">
            <label htmlFor={fieldIds['invoice.address']}>Địa chỉ công ty</label>
            <input
              id={fieldIds['invoice.address']}
              type="text"
              autoComplete="off"
              disabled={isSubmitting}
              value={values.address}
              onChange={(event) => onChange('address', event.target.value)}
              onBlur={() => onBlur('invoice.address')}
              {...fieldErrorProps(fieldIds['invoice.address'], errors['invoice.address'])}
            />
            <FieldError inputId={fieldIds['invoice.address']} message={errors['invoice.address']} />
          </div>
        </div>
      )}
    </section>
  );
}
