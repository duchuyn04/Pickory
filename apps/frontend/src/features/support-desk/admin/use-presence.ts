import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useActor, useCan } from '../../identity/hooks/use-actor';
import { applyPresenceChange, isStaffAvailable } from './presence-state';
import { PRESENCE_QUERY_KEY, fetchAvailableStaff, putPresence } from './support-desk-api';
import type { StaffPresence } from './support-desk-schemas';

// Trạng thái sẵn sàng của nhân viên đang đăng nhập. Danh sách được `SupportEventsProvider` cập nhật khi có `presence.changed`.
export function usePresence() {
  const { data: actor } = useActor();
  const canReply = useCan('conversations:reply');
  const queryClient = useQueryClient();

  const availableStaffQuery = useQuery({
    queryKey: PRESENCE_QUERY_KEY,
    queryFn: fetchAvailableStaff,
    // Người không có `conversations:reply` không gọi được route này và cũng không thể sẵn sàng.
    enabled: canReply,
  });

  const savePresence = useMutation({
    mutationFn: putPresence,
    // Một lần đọc danh sách đang chạy có thể về sau `PUT` và ghi đè trạng thái vừa lưu. Hủy lần đọc đó trước khi gửi `PUT`.
    onMutate: () => queryClient.cancelQueries({ queryKey: PRESENCE_QUERY_KEY }),
    // Lần đọc bị hủy ở `onMutate` phải chạy lại. Không chờ lần đọc đó, để dòng lỗi hiện ngay.
    onError: () => {
      void queryClient.invalidateQueries({ queryKey: PRESENCE_QUERY_KEY });
    },
    // Chỉ đổi công tắc sau khi API trả lời. Lỗi thì công tắc giữ trạng thái cũ tới khi lần đọc lại về.
    onSuccess: (presence) => {
      queryClient.setQueryData<StaffPresence[]>(PRESENCE_QUERY_KEY, (current) =>
        current ? applyPresenceChange(current, presence, presence.lastSeenAt) : current,
      );
    },
  });

  const isAvailable = isStaffAvailable(availableStaffQuery.data, actor?.userId);

  function toggle() {
    savePresence.mutate(isAvailable ? 'away' : 'available');
  }

  return {
    isAvailable,
    isLoading: availableStaffQuery.isPending && canReply,
    loadFailed: availableStaffQuery.isError,
    isSaving: savePresence.isPending,
    saveFailed: savePresence.isError,
    toggle,
  };
}
