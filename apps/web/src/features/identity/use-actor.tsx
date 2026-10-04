import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '../../shared/api';

export interface ActorData {
  anonymousId: string;
  userId: string | null;
  email?: string | null;
  name?: string | null;
  phone?: string | null;
  kind: 'guest' | 'customer' | 'staff';
  roles: string[];
  permissions: string[];
  consent: {
    personalization: boolean;
    marketing: boolean;
  };
}

/**
 * Hook truy vấn thông tin Actor hiện tại kèm trạng thái phiên và quyền hạn từ endpoint /api/me.
 */
export function useActor() {
  return useQuery<ActorData>({
    queryKey: ['actor'],
    queryFn: () => apiRequest<ActorData>('/api/me'),
    // Giữ cache trong 30 giây để giảm tải số lượng request GET /api/me khi người dùng chuyển qua lại giữa các trang.
    staleTime: 30_000,
  });
}

/**
 * Kiểm tra xem Actor có sở hữu quyền hạn cụ thể hay không.
 */
export function checkPermission(actor: ActorData | undefined | null, permission: string): boolean {
  if (!actor || !Array.isArray(actor.permissions)) {
    return false;
  }
  // Quản trị viên (admin) mặc định có toàn bộ quyền trong hệ thống nên luôn trả về true.
  if (actor.roles.includes('admin')) {
    return true;
  }
  return actor.permissions.includes(permission);
}

/**
 * Hook kiểm tra nhanh quyền hạn của người dùng đang đăng nhập để ẩn hiện các thành phần giao diện.
 */
export function useCan(permission: string): boolean {
  const { data: actor } = useActor();
  return checkPermission(actor, permission);
}

/**
 * Hook xác định xem Actor hiện tại có phải là nhân viên nội bộ hay không.
 */
export function useIsStaff(): boolean {
  const { data: actor } = useActor();
  return actor?.kind === 'staff';
}
