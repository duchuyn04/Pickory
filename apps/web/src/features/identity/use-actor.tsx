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
 * Hook nạp thông tin Actor hiện tại từ GET /api/me.
 */
export function useActor() {
  return useQuery<ActorData>({
    queryKey: ['actor'],
    queryFn: () => apiRequest<ActorData>('/api/me'),
    staleTime: 30_000,
  });
}

/**
 * Kiểm tra actor hiện tại có quyền cụ thể không.
 * Trả về false nếu chưa có dữ liệu hoặc không có quyền.
 */
export function checkPermission(actor: ActorData | undefined | null, permission: string): boolean {
  if (!actor || !Array.isArray(actor.permissions)) {
    return false;
  }
  // admin có mọi quyền
  if (actor.roles.includes('admin')) {
    return true;
  }
  return actor.permissions.includes(permission);
}

/**
 * Hook kiểm tra quyền của actor hiện tại.
 */
export function useCan(permission: string): boolean {
  const { data: actor } = useActor();
  return checkPermission(actor, permission);
}

/**
 * Hook kiểm tra actor có phải nhân viên không.
 */
export function useIsStaff(): boolean {
  const { data: actor } = useActor();
  return actor?.kind === 'staff';
}
