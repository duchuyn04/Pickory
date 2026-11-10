// Hàm thuần đọc và đổi danh sách ngày nghỉ. Ngày luôn là chuỗi `yyyy-MM-dd`, không qua `Date`, nên múi giờ máy không làm lệch ngày.

const ISO_DAY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

export const EMPTY_DAY_ERROR = 'Hãy chọn một ngày.';
export const INVALID_DAY_ERROR = 'Ngày không hợp lệ.';
export const DUPLICATE_DAY_ERROR = 'Ngày này đã có trong danh sách.';

function isLeapYear(year: number): boolean {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
}

function daysInMonth(year: number, month: number): number {
  if (month === 2) return isLeapYear(year) ? 29 : 28;
  return [4, 6, 9, 11].includes(month) ? 30 : 31;
}

// Chuỗi đúng dạng nhưng ngày không có thật, ví dụ `2027-02-30`, bị loại để backend không nhận ngày sai.
export function isValidIsoDay(value: string): boolean {
  const match = ISO_DAY_PATTERN.exec(value);
  if (!match) return false;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (month < 1 || month > 12 || day < 1) return false;
  return day <= daysInMonth(year, month);
}

// Đổi `2027-02-06` thành `06/02/2027` để người đọc quen dạng ngày của Việt Nam.
export function formatIsoDay(isoDay: string): string {
  const match = ISO_DAY_PATTERN.exec(isoDay);
  if (!match) return isoDay;
  return `${match[3]}/${match[2]}/${match[1]}`;
}

// Chuỗi `yyyy-MM-dd` cùng độ dài nên so chuỗi cho đúng thứ tự thời gian.
export function sortHolidays(holidays: readonly string[]): string[] {
  return [...holidays].sort((first, second) => first.localeCompare(second));
}

export type AddHolidayResult = { ok: true; holidays: string[] } | { ok: false; error: string };

// Thêm một ngày vào danh sách. Ngày trống, sai hoặc trùng thì trả lỗi và giữ nguyên danh sách.
export function addHoliday(current: readonly string[], isoDay: string): AddHolidayResult {
  if (isoDay === '') return { ok: false, error: EMPTY_DAY_ERROR };
  if (!isValidIsoDay(isoDay)) return { ok: false, error: INVALID_DAY_ERROR };
  if (current.includes(isoDay)) return { ok: false, error: DUPLICATE_DAY_ERROR };
  return { ok: true, holidays: sortHolidays([...current, isoDay]) };
}

export function removeHoliday(current: readonly string[], isoDay: string): string[] {
  return current.filter((holiday) => holiday !== isoDay);
}
