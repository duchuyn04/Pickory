const vietnamDateTime = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Ho_Chi_Minh',
  hourCycle: 'h23',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
});

const vietnamDay = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Ho_Chi_Minh',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

const vietnamHourMinute = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Ho_Chi_Minh',
  hourCycle: 'h23',
  hour: '2-digit',
  minute: '2-digit',
});

// API trả giờ UTC. Chuỗi thiếu múi giờ sẽ bị `Date` hiểu là giờ máy xem, nên thêm `Z` trước khi đọc.
function readUtcDate(isoTime: string): Date {
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/.test(isoTime);
  return new Date(hasZone ? isoTime : `${isoTime}Z`);
}

// Chuỗi hỏng không được làm sập trang: bộ định dạng giờ ném lỗi khi nhận ngày không hợp lệ.
function isValidDate(date: Date): boolean {
  return !Number.isNaN(date.getTime());
}

function toDayKey(date: Date): string {
  return vietnamDay.format(date);
}

// Hai mốc giờ có cùng ngày theo giờ Việt Nam hay không.
export function isSameVietnamDay(firstIso: string, secondIso: string): boolean {
  const first = readUtcDate(firstIso);
  const second = readUtcDate(secondIso);
  if (!isValidDate(first) || !isValidDate(second)) return false;
  return toDayKey(first) === toDayKey(second);
}

// Hiện giờ Việt Nam dạng `HH:mm dd/MM/yyyy`, không phụ thuộc múi giờ của máy xem.
export function formatVietnamDateTime(isoTime: string): string {
  const date = readUtcDate(isoTime);
  if (!isValidDate(date)) return '';
  const parts = vietnamDateTime.formatToParts(date);
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((part) => part.type === type)?.value ?? '';
  return `${get('hour')}:${get('minute')} ${get('day')}/${get('month')}/${get('year')}`;
}

// Hiện giờ phút theo giờ Việt Nam dạng `HH:mm`, dùng cho dòng phụ của từng tin.
export function formatVietnamTime(isoTime: string): string {
  const date = readUtcDate(isoTime);
  if (!isValidDate(date)) return '';
  return vietnamHourMinute.format(date);
}

// Nhãn ngày của tin: Hôm nay, Hôm qua, hoặc `dd/MM/yyyy` theo giờ Việt Nam.
export function formatVietnamDayLabel(isoTime: string, now: Date = new Date()): string {
  const date = readUtcDate(isoTime);
  if (!isValidDate(date)) return '';
  const dayKey = toDayKey(date);
  if (dayKey === toDayKey(now)) return 'Hôm nay';
  // Việt Nam không đổi giờ theo mùa, nên lùi đúng 24 giờ là ngày hôm qua.
  const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);
  if (dayKey === toDayKey(yesterday)) return 'Hôm qua';
  return dayKey;
}
