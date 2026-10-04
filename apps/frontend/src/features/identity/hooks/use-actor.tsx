import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '../../../shared/api';

// Dữ liệu GET /api/me trả về: người đang dùng web là ai và được làm gì.
export interface ActorData {
  anonymousId: string;
  // null khi chưa đăng nhập; email, tên, số điện thoại cũng chỉ có khi đã đăng nhập.
  userId: string | null;
  email?: string | null;
  name?: string | null;
  phone?: string | null;
  kind: 'guest' | 'customer' | 'staff';
  roles: string[];
  // Mảng chuỗi (không phải Set) vì dữ liệu đi qua JSON.
  permissions: string[];
  consent: {
    personalization: boolean;
    marketing: boolean;
  };
}

// Lấy actor hiện tại; mọi nơi gọi hook này dùng chung một bản trong cache.
export function useActor() {
  return useQuery<ActorData>({
    // Khóa cố định để nơi khác gọi invalidateQueries(['actor']) là mọi chỗ dùng actor nạp lại.
    queryKey: ['actor'],
    queryFn: () => apiRequest<ActorData>('/api/me'),
    // Coi dữ liệu còn mới trong 30 giây để chuyển trang không gọi lại /api/me liên tục.
    staleTime: 30_000,
  });
}

// Actor có quyền này không; chỉ để ẩn hiện giao diện, backend vẫn tự kiểm tra mọi request.
export function checkPermission(actor: ActorData | undefined | null, permission: string): boolean {
  // Chưa có actor (đang nạp) hoặc dữ liệu không đúng dạng thì coi như không có quyền, để mặc định là ẩn.
  if (!actor || !Array.isArray(actor.permissions)) {
    return false;
  }
  // Admin có mọi quyền, kể cả quyền thêm sau này mà danh sách permissions từ server chưa chắc liệt kê hết.
  if (actor.roles.includes('admin')) {
    return true;
  }
  return actor.permissions.includes(permission);
}

// Dạng hook của checkPermission, dùng ở component để ẩn hiện menu và nút.
export function useCan(permission: string): boolean {
  const { data: actor } = useActor();
  return checkPermission(actor, permission);
}

// Actor hiện tại có phải nhân viên không.
export function useIsStaff(): boolean {
  const { data: actor } = useActor();
  return actor?.kind === 'staff';
}
