import { FieldError, fieldErrorProps } from '../../../../shared/field-error';
import type {
  CheckoutFieldErrors,
  CheckoutFieldName,
  CheckoutFormValues,
} from './checkout-form-schema';

interface CheckoutContactSectionProps {
  values: CheckoutFormValues['contact'];
  errors: CheckoutFieldErrors;
  fieldIds: Record<CheckoutFieldName, string>;
  isSubmitting: boolean;
  onChange: (field: 'name' | 'phone' | 'email', value: string) => void;
  onBlur: (field: CheckoutFieldName) => void;
}

export function CheckoutContactSection({
  values,
  errors,
  fieldIds,
  isSubmitting,
  onChange,
  onBlur,
}: CheckoutContactSectionProps) {
  return (
    <section className="card checkout-section" aria-labelledby="checkout-contact-title">
      <h2 id="checkout-contact-title">
        <span className="checkout-section-number" data-step="1" aria-hidden="true" />
        <span>Thông tin liên hệ</span>
      </h2>
      <div className="form-grid form-grid-2">
        <div className="field">
          <label htmlFor={fieldIds['contact.name']}>Họ và tên</label>
          <input
            id={fieldIds['contact.name']}
            type="text"
            autoComplete="name"
            disabled={isSubmitting}
            value={values.name}
            onChange={(event) => onChange('name', event.target.value)}
            onBlur={() => onBlur('contact.name')}
            {...fieldErrorProps(fieldIds['contact.name'], errors['contact.name'])}
          />
          <FieldError inputId={fieldIds['contact.name']} message={errors['contact.name']} />
        </div>
        <div className="field">
          <label htmlFor={fieldIds['contact.phone']}>Số điện thoại</label>
          <input
            id={fieldIds['contact.phone']}
            type="tel"
            autoComplete="tel"
            inputMode="tel"
            disabled={isSubmitting}
            value={values.phone}
            onChange={(event) => onChange('phone', event.target.value)}
            onBlur={() => onBlur('contact.phone')}
            {...fieldErrorProps(fieldIds['contact.phone'], errors['contact.phone'])}
          />
          <FieldError inputId={fieldIds['contact.phone']} message={errors['contact.phone']} />
        </div>
        <div className="field field-full">
          <label htmlFor={fieldIds['contact.email']}>Email</label>
          <input
            id={fieldIds['contact.email']}
            type="email"
            autoComplete="email"
            disabled={isSubmitting}
            value={values.email}
            onChange={(event) => onChange('email', event.target.value)}
            onBlur={() => onBlur('contact.email')}
            {...fieldErrorProps(fieldIds['contact.email'], errors['contact.email'])}
          />
          <FieldError inputId={fieldIds['contact.email']} message={errors['contact.email']} />
        </div>
      </div>
    </section>
  );
}
