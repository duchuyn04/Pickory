import type { PresenceChange, StaffPresence } from './support-desk-schemas';

// Nhân viên có trong danh sách đang sẵn sàng thì công tắc bật. Chưa biết danh sách hoặc người dùng thì coi là chưa sẵn sàng.
export function isStaffAvailable(
  availableStaff: readonly StaffPresence[] | undefined,
  staffId: string | null | undefined,
): boolean {
  if (!availableStaff || !staffId) return false;
  return availableStaff.some((entry) => entry.staffId === staffId);
}

// Áp một thay đổi trạng thái lên danh sách đang sẵn sàng, không sửa mảng đầu vào.
// Sự kiện không mang `lastSeenAt`, nên `changedAt` là giờ nhận sự kiện.
export function applyPresenceChange(
  availableStaff: readonly StaffPresence[],
  change: PresenceChange,
  changedAt: string,
): StaffPresence[] {
  // Bỏ dòng cũ của người đó trước. Nhận `available` hai lần không tạo hai dòng.
  const others = availableStaff.filter((entry) => entry.staffId !== change.staffId);
  if (change.status === 'away') return others;
  return [...others, { staffId: change.staffId, status: 'available', lastSeenAt: changedAt }];
}
