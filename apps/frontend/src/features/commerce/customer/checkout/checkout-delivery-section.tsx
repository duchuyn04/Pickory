import type { FocusEvent } from 'react';
import { FieldError, fieldErrorProps } from '../../../../shared/field-error';
import { AddressPicker, type AddressValue } from '../../../shipping/address-picker';
import { describeAddress, type SavedAddress } from '../addresses/addresses-api';
import {
  NOTE_MAX_LENGTH,
  type CheckoutFieldErrors,
  type CheckoutFieldName,
} from './checkout-form-schema';

const ADDRESS_ID_PREFIX = 'checkout-address';
const ADDRESS_ERROR_ANCHOR_ID = 'checkout-address-group';

interface CheckoutDeliverySectionProps {
  address: AddressValue;
  note: string;
  savedAddresses: SavedAddress[];
  selectedSavedId: string;
  errors: CheckoutFieldErrors;
  fieldIds: Record<CheckoutFieldName, string>;
  isSubmitting: boolean;
  onSavedAddressChange: (id: string) => void;
  onAddressChange: (address: AddressValue) => void;
  onAddressBlur: (event: FocusEvent<HTMLDivElement>) => void;
  onNoteChange: (note: string) => void;
  onNoteBlur: () => void;
}

export function CheckoutDeliverySection({
  address,
  note,
  savedAddresses,
  selectedSavedId,
  errors,
  fieldIds,
  isSubmitting,
  onSavedAddressChange,
  onAddressChange,
  onAddressBlur,
  onNoteChange,
  onNoteBlur,
}: CheckoutDeliverySectionProps) {
  return (
    <section className="card checkout-section" aria-labelledby="checkout-address-title">
      <h2 id="checkout-address-title">
        <span className="checkout-section-number" data-step="2" aria-hidden="true" />
        <span>Địa chỉ nhận hàng</span>
      </h2>
      {savedAddresses.length > 0 && (
        <div className="field">
          <label htmlFor="checkout-saved-address">Địa chỉ đã lưu</label>
          <select
            id="checkout-saved-address"
            disabled={isSubmitting}
            value={selectedSavedId}
            onChange={(event) => onSavedAddressChange(event.target.value)}
          >
            <option value="">Nhập địa chỉ khác</option>
            {savedAddresses.map((saved) => (
              <option key={saved.id} value={saved.id}>
                {saved.label}: {describeAddress(saved.address)}
              </option>
            ))}
          </select>
        </div>
      )}
      <div id={ADDRESS_ERROR_ANCHOR_ID} onBlur={onAddressBlur}>
        <AddressPicker
          value={address}
          onChange={onAddressChange}
          idPrefix={ADDRESS_ID_PREFIX}
          disabled={isSubmitting}
          errors={{
            province: errors['address.provinceCode'],
            ward: errors['address.wardCode'],
            line: errors['address.line'],
          }}
        />
      </div>
      <FieldError inputId={ADDRESS_ID_PREFIX} message={errors.address} />

      <div className="checkout-note-group">
        <h3 id="checkout-note-title">Ghi chú</h3>
        <div className="field">
          <label htmlFor={fieldIds.note}>Ghi chú cho đơn hàng (không bắt buộc)</label>
          <textarea
            id={fieldIds.note}
            rows={3}
            disabled={isSubmitting}
            value={note}
            onChange={(event) => onNoteChange(event.target.value)}
            onBlur={onNoteBlur}
            {...fieldErrorProps(fieldIds.note, errors.note)}
          />
          <FieldError inputId={fieldIds.note} message={errors.note} />
          <span className="muted small">Tối đa {NOTE_MAX_LENGTH} ký tự.</span>
        </div>
      </div>
    </section>
  );
}
