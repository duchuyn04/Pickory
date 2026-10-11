import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { FieldError, fieldErrorProps, hasFieldErrors } from '../../../shared/field-error';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { WarningAlert } from '../../../shared/warning-alert';
import { useCan } from '../../identity/hooks/use-actor';
import { AddressPicker, EMPTY_ADDRESS, type AddressValue } from '../address-picker';
import './shipping-settings.css';
import {
  fetchShippingSettings,
  saveShippingSettings,
  shippingSettingsQueryKey,
  type ShippingSettings,
  type ShippingSettingsResponse,
} from '../shipping-settings-api';
import {
  mapServerErrors,
  validateShippingForm,
  type ShippingFieldErrors,
  type ShippingFieldName,
} from '../shipping-settings-validation';

const SHIPPING_SETTINGS_PERMISSION = 'shipping-settings:manage';

// Id ổn định cho từng ô, để nhãn, lỗi và E2E nối với đúng ô.
const SENDER_NAME_ID = 'shipping-sender-name';
const SENDER_PHONE_ID = 'shipping-sender-phone';
const SERVICE_TYPE_ID = 'shipping-service-type';
const GHN_SHOP_ID_FIELD_ID = 'shipping-ghn-shop-id';
const ADDRESS_ID_PREFIX = 'shipping-address';

const SAVED_MESSAGE = 'Đã lưu cấu hình vận chuyển.';

// Giá trị của ô chọn gói dịch vụ. Chuỗi rỗng là chưa chọn, tức `null` ở backend.
const SERVICE_TYPE_OPTIONS = [
  { value: '', label: 'Theo mặc định của GHN (hàng nhẹ)' },
  { value: '2', label: 'Hàng nhẹ' },
  { value: '5', label: 'Hàng nặng' },
];

// Màn cấu hình địa chỉ lấy hàng và gói dịch vụ mặc định của shop.
export function ShippingSettingsPage() {
  const canManage = useCan(SHIPPING_SETTINGS_PERMISSION);

  // Thiếu quyền thì không gọi API, vì backend chắc chắn trả 403.
  // Tắt tải lại khi focus cửa sổ để dữ liệu đang sửa dở không bị ghi đè.
  const settingsQuery = useQuery({
    queryKey: shippingSettingsQueryKey,
    queryFn: fetchShippingSettings,
    enabled: canManage,
    refetchOnWindowFocus: false,
  });

  // Hiện thông báo thiếu quyền khi mở thẳng URL, vì menu chỉ ẩn mục chứ không chặn đường dẫn.
  if (!canManage) {
    return (
      <ForbiddenNotice permission={SHIPPING_SETTINGS_PERMISSION} subject="cấu hình vận chuyển" />
    );
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Cấu hình vận chuyển</h1>
          <p>Địa chỉ lấy hàng và gói dịch vụ mặc định khi báo phí, tạo vận đơn qua GHN.</p>
        </div>
      </div>

      {settingsQuery.isError && (
        <ErrorAlert
          message={
            settingsQuery.error instanceof Error && settingsQuery.error.message
              ? settingsQuery.error.message
              : 'Không tải được cấu hình vận chuyển.'
          }
        />
      )}

      {settingsQuery.isLoading && (
        <p role="status" className="empty-state">
          Đang tải cấu hình...
        </p>
      )}

      {settingsQuery.data && (
        <ShippingSettingsForm
          initialSettings={settingsQuery.data.settings}
          ghnShopId={settingsQuery.data.ghnShopId}
        />
      )}
    </div>
  );
}

interface ShippingSettingsFormProps {
  initialSettings: ShippingSettings | null;
  ghnShopId: string | null;
}

function toAddressValue(settings: ShippingSettings | null): AddressValue {
  if (!settings) return EMPTY_ADDRESS;
  return {
    provinceCode: settings.provinceCode,
    provinceName: settings.provinceName,
    wardCode: settings.wardCode,
    wardName: settings.wardName,
    line: settings.line,
  };
}

// Số `null` ở backend là chuỗi rỗng ở ô chọn, vì `select` chỉ giữ chuỗi.
function toServiceTypeValue(settings: ShippingSettings | null): string {
  if (!settings || settings.defaultServiceTypeId === null) return '';
  return String(settings.defaultServiceTypeId);
}

function ShippingSettingsForm({ initialSettings, ghnShopId }: ShippingSettingsFormProps) {
  const queryClient = useQueryClient();
  const [senderName, setSenderName] = useState(initialSettings?.senderName ?? '');
  const [senderPhone, setSenderPhone] = useState(initialSettings?.senderPhone ?? '');
  const [address, setAddress] = useState<AddressValue>(toAddressValue(initialSettings));
  const [serviceType, setServiceType] = useState(toServiceTypeValue(initialSettings));

  const [fieldErrors, setFieldErrors] = useState<ShippingFieldErrors>({});
  // Lỗi không gắn được vào ô nào, hiện bằng `ErrorAlert` đầu form.
  const [formErrors, setFormErrors] = useState<string[]>([]);
  const [isSaved, setIsSaved] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Sửa ô nào thì lỗi của ô đó hết hiệu lực, vì lỗi cũ nói về giá trị cũ.
  function clearFieldError(...fields: ShippingFieldName[]) {
    setFieldErrors((current) => {
      const next = { ...current };
      for (const field of fields) delete next[field];
      return next;
    });
    setIsSaved(false);
  }

  // Form nhận lại dữ liệu từ response để hiện đúng giá trị backend đã lưu, ví dụ số điện thoại đã chuẩn hóa.
  function applySavedSettings(saved: ShippingSettings) {
    setSenderName(saved.senderName);
    setSenderPhone(saved.senderPhone);
    setAddress(toAddressValue(saved));
    setServiceType(toServiceTypeValue(saved));
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    // Nút đã tắt khi đang gửi, nhưng phím Enter trong ô nhập vẫn gửi form. Chặn tại đây để không gửi hai lần.
    if (isSubmitting) return;

    setIsSaved(false);
    setFormErrors([]);

    // Kiểm phía client trước. Còn lỗi thì không gửi request, để lỗi hiện ngay dưới đúng ô.
    const clientErrors = validateShippingForm({
      senderName,
      senderPhone,
      provinceCode: address.provinceCode,
      wardCode: address.wardCode,
      line: address.line,
    });
    setFieldErrors(clientErrors);
    if (hasFieldErrors(clientErrors)) return;

    setIsSubmitting(true);
    try {
      // Chỉ gửi 6 trường backend nhận. Tên tỉnh và tên phường do backend lấy từ danh mục.
      const response: ShippingSettingsResponse = await saveShippingSettings({
        senderName: senderName.trim(),
        senderPhone: senderPhone.trim(),
        provinceCode: address.provinceCode,
        wardCode: address.wardCode,
        line: address.line.trim(),
        defaultServiceTypeId: serviceType === '' ? null : Number(serviceType),
      });

      // Cập nhật cache để lần mở lại màn trong phiên này hiện dữ liệu vừa lưu.
      queryClient.setQueryData(shippingSettingsQueryKey, response);
      if (response.settings) applySavedSettings(response.settings);
      setFieldErrors({});
      setIsSaved(true);
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        // Lỗi 400 gán vào đúng ô. Phần không gắn được vào ô nào hiện đầu form.
        const mapped = mapServerErrors(error);
        setFieldErrors(mapped.fieldErrors);
        setFormErrors(mapped.unmatched);
      } else {
        // Lỗi khác (403, 503, mạng) hiện đầu form. Dữ liệu khách đã nhập giữ nguyên để thử lại.
        setFormErrors([error instanceof Error ? error.message : 'Không lưu được cấu hình.']);
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <>
      {ghnShopId === null && (
        <WarningAlert>
          <strong>Chưa đặt GHN_SHOP_ID.</strong> Hãy đặt biến môi trường này ở máy chủ trước khi báo
          phí hoặc tạo vận đơn.
        </WarningAlert>
      )}

      {formErrors.length > 0 && <ErrorAlert message={formErrors[0]} items={formErrors.slice(1)} />}

      {isSaved && (
        <p role="alert" className="alert alert-ok">
          {SAVED_MESSAGE}
        </p>
      )}

      <form noValidate className="card" onSubmit={handleSubmit}>
        <div className="card-head">
          <h2>Người gửi</h2>
        </div>
        <div className="form-grid form-grid-2">
          <div className="field">
            <label htmlFor={SENDER_NAME_ID}>Tên người gửi</label>
            <input
              id={SENDER_NAME_ID}
              type="text"
              autoComplete="off"
              disabled={isSubmitting}
              value={senderName}
              onChange={(event) => {
                setSenderName(event.target.value);
                clearFieldError('senderName');
              }}
              {...fieldErrorProps(SENDER_NAME_ID, fieldErrors.senderName)}
            />
            <FieldError inputId={SENDER_NAME_ID} message={fieldErrors.senderName} />
          </div>
          <div className="field">
            <label htmlFor={SENDER_PHONE_ID}>Số điện thoại người gửi</label>
            <input
              id={SENDER_PHONE_ID}
              type="tel"
              autoComplete="off"
              disabled={isSubmitting}
              value={senderPhone}
              onChange={(event) => {
                setSenderPhone(event.target.value);
                clearFieldError('senderPhone');
              }}
              {...fieldErrorProps(SENDER_PHONE_ID, fieldErrors.senderPhone)}
            />
            <FieldError inputId={SENDER_PHONE_ID} message={fieldErrors.senderPhone} />
          </div>
        </div>

        <div className="card-head shipping-settings-section">
          <h2>Địa chỉ lấy hàng</h2>
        </div>
        <AddressPicker
          value={address}
          onChange={(next) => {
            setAddress(next);
            clearFieldError('province', 'ward', 'line');
          }}
          errors={{
            province: fieldErrors.province,
            ward: fieldErrors.ward,
            line: fieldErrors.line,
          }}
          idPrefix={ADDRESS_ID_PREFIX}
          disabled={isSubmitting}
        />

        <div className="card-head shipping-settings-section">
          <h2>Dịch vụ giao hàng</h2>
        </div>
        <div className="form-grid form-grid-2">
          <div className="field">
            <label htmlFor={SERVICE_TYPE_ID}>Gói dịch vụ mặc định</label>
            <select
              id={SERVICE_TYPE_ID}
              disabled={isSubmitting}
              value={serviceType}
              onChange={(event) => {
                setServiceType(event.target.value);
                clearFieldError('serviceType');
              }}
              {...fieldErrorProps(SERVICE_TYPE_ID, fieldErrors.serviceType)}
            >
              {SERVICE_TYPE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <FieldError inputId={SERVICE_TYPE_ID} message={fieldErrors.serviceType} />
          </div>
          <div className="field">
            <label htmlFor={GHN_SHOP_ID_FIELD_ID}>GHN ShopId</label>
            {/* Ô xám và bị khóa vì giá trị đến từ biến môi trường của máy chủ, không sửa ở màn này. */}
            <input
              id={GHN_SHOP_ID_FIELD_ID}
              type="text"
              disabled
              value={ghnShopId ?? ''}
              placeholder="Chưa đặt"
            />
          </div>
        </div>

        <div className="head-actions">
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
            {isSubmitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </div>
      </form>
    </>
  );
}
