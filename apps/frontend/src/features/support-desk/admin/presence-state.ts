import type { PresenceChange, StaffPresence } from './support-desk-schemas';

// Nhân viên có trong danh sách đang sẵn sàng thì công tắc bật. Chưa biết danh sách hoặc người dùng thì coi là chưa sẵn sàng.
export function isStaffAvailable(
  availableStaff: readonly StaffPresence[] | undefined,
  staffId: string | null | undefined,
): boolean {
  if (!availableStaff || !staffId) return false;
  return availableStaff.some((entry) => entry.staffId === staffId);
}

// Nhãn chọn nhân viên ưu tiên tên hiển thị để dễ nhận diện, dùng 4 ký tự cuối làm dự phòng.
export function staffRadioLabel(staff: StaffPresence): string {
  if (staff.displayName && staff.displayName.trim().length > 0) {
    return staff.displayName.trim();
  }
  // Dùng 4 ký tự cuối để phân biệt nhanh đồng nghiệp khi thiếu tên hiển thị.
  return `Nhân viên ${staff.staffId.slice(-4)}`;
}

// Áp một thay đổi trạng thái lên danh sách đang sẵn sàng, không sửa mảng đầu vào.
// Sự kiện không mang `lastSeenAt`, nên `changedAt` là giờ nhận sự kiện.
export function applyPresenceChange(
  availableStaff: readonly StaffPresence[],
  change: PresenceChange,
  changedAt: string,
): StaffPresence[] {
  // Tìm thông tin cũ để giữ lại tên hiển thị khi cập nhật thời điểm trực tuyến.
  const existing = availableStaff.find((entry) => entry.staffId === change.staffId);
  // Bỏ dòng cũ của người đó trước. Nhận `available` hai lần không tạo hai dòng.
  const others = availableStaff.filter((entry) => entry.staffId !== change.staffId);
  if (change.status === 'away') return others;
  // Giữ lại tên hiển thị đã có để không bị mất tên khi nhận sự kiện mới.
  return [
    ...others,
    {
      ...existing,
      staffId: change.staffId,
      status: 'available',
      lastSeenAt: changedAt,
    },
  ];
}
