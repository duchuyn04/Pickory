const vietnamTime = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Ho_Chi_Minh',
  hourCycle: 'h23',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
});

// Hiện giờ Việt Nam dạng `HH:mm dd/MM/yyyy`, không phụ thuộc múi giờ của máy xem.
export function formatVietnamDateTime(isoTime: string): string {
  // API trả giờ UTC. Chuỗi thiếu múi giờ sẽ bị `Date` hiểu là giờ máy, nên thêm `Z` trước.
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/.test(isoTime);
  const parts = vietnamTime.formatToParts(new Date(hasZone ? isoTime : `${isoTime}Z`));
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((part) => part.type === type)?.value ?? '';
  return `${get('hour')}:${get('minute')} ${get('day')}/${get('month')}/${get('year')}`;
}
