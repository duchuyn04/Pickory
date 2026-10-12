import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import type { ApiError } from '../../../../shared/api';
import type { AddressValue } from '../../../shipping/address-picker';
import { fetchCheckoutPreview, type CheckoutPreview, type PaymentMethod } from './checkout-api';
import { buildPreviewInput } from './checkout-rules';

// Mỗi preview gọi GHN và backend giới hạn 20 lần mỗi phút. Chờ khách ngừng gõ rồi mới gọi.
export const PREVIEW_DEBOUNCE_MS = 400;

// Trả giá trị sau khi nó đứng yên đủ `delayMs`. Giá trị đổi liên tục thì chưa trả giá trị mới.
export function useDebouncedValue<Value>(value: Value, delayMs: number): Value {
  const [debouncedValue, setDebouncedValue] = useState(value);

  useEffect(() => {
    const timerId = setTimeout(() => setDebouncedValue(value), delayMs);
    // Hủy hẹn giờ cũ khi giá trị đổi tiếp, để chỉ giá trị cuối cùng được trả.
    return () => clearTimeout(timerId);
  }, [value, delayMs]);

  return debouncedValue;
}

interface CheckoutPreviewState {
  data: CheckoutPreview | undefined;
  error: ApiError | null;
  // Đúng khi có địa chỉ đủ để gọi và đang chờ hoặc đang gọi preview.
  isLoading: boolean;
  // Sai khi số nhà đang gõ dở và chưa đến lượt gọi. Khi đó dữ liệu chưa khớp với địa chỉ trên form.
  isCurrent: boolean;
  // Đúng khi địa chỉ chưa đủ để gọi preview.
  isAddressIncomplete: boolean;
  refetch: () => Promise<{ data: CheckoutPreview | undefined; isError: boolean }>;
}

// Preview theo địa chỉ và phương thức thanh toán. Địa chỉ chưa đủ thì không gọi.
export function useCheckoutPreview(
  address: AddressValue,
  paymentMethod: PaymentMethod,
  isEnabled: boolean,
): CheckoutPreviewState {
  const debouncedLine = useDebouncedValue(address.line, PREVIEW_DEBOUNCE_MS);

  // Tỉnh và phường là lựa chọn rời rạc nên áp dụng ngay. Chỉ số nhà gõ phím mới cần chờ.
  const previewInput = isEnabled
    ? buildPreviewInput({ ...address, line: debouncedLine }, paymentMethod)
    : null;

  const query = useQuery<CheckoutPreview, ApiError>({
    // Khóa gồm địa chỉ và phương thức. Đổi một trong hai thì dữ liệu cũ không bao giờ hiện cho khóa mới.
    queryKey: ['checkout-preview', previewInput],
    queryFn: () => fetchCheckoutPreview(previewInput!),
    enabled: previewInput !== null,
    // Không tự thử lại và không gọi khi focus lại cửa sổ, để không tốn hạn mức 20 lần mỗi phút.
    retry: false,
    refetchOnWindowFocus: false,
    // Không giữ phí cũ trong cache. Phí vận chuyển và tồn kho có thể đổi giữa hai lần xem.
    staleTime: 0,
    gcTime: 0,
  });

  const isCurrent = debouncedLine === address.line;
  const isAddressIncomplete = previewInput === null;

  return {
    // Số nhà đang gõ dở thì dữ liệu của số nhà cũ không được hiện, để khách không thấy phí của địa chỉ khác.
    data: previewInput === null || !isCurrent ? undefined : query.data,
    error: previewInput === null || !isCurrent ? null : query.error,
    isLoading: !isAddressIncomplete && (!isCurrent || query.isFetching),
    isCurrent,
    isAddressIncomplete,
    refetch: async () => {
      const result = await query.refetch();
      return { data: result.data, isError: result.isError };
    },
  };
}
