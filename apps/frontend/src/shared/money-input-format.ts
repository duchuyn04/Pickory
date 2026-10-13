// Giới hạn khớp số nguyên 32 bit của cột giá ở backend (2.147.483.647, 10 chữ số). 10 chữ số cũng nhỏ hơn `Number.MAX_SAFE_INTEGER`.
export const VND_MAX_DIGITS = 10;

const NON_DIGIT_PATTERN = /\D/g;
const THOUSANDS_BOUNDARY_PATTERN = /\B(?=(\d{3})+(?!\d))/g;

export interface ReformattedMoneyInput {
  value: number | null;
  text: string;
  caret: number;
}

// Giữ lại chữ số ASCII. Dấu chấm, dấu phẩy, khoảng trắng, đơn vị `đ`, dấu trừ đều bị bỏ.
function extractDigits(text: string): string {
  return text.replace(NON_DIGIT_PATTERN, '');
}

// Bỏ số 0 ở đầu nhưng chừa lại một chữ số 0 khi cả chuỗi là số 0.
function stripLeadingZeros(digits: string): { significant: string; strippedCount: number } {
  if (digits === '') return { significant: '', strippedCount: 0 };
  const significant = digits.replace(/^0+(?=\d)/, '');
  return { significant, strippedCount: digits.length - significant.length };
}

function groupThousands(digits: string): string {
  return digits.replace(THOUSANDS_BOUNDARY_PATTERN, '.');
}

// Vị trí con trỏ ngay sau chữ số thứ `digitCount` của chuỗi đã định dạng.
function caretAfterDigits(text: string, digitCount: number): number {
  if (digitCount <= 0) return 0;
  let seenDigits = 0;
  for (let index = 0; index < text.length; index += 1) {
    const character = text.charAt(index);
    if (character >= '0' && character <= '9') seenDigits += 1;
    if (seenDigits === digitCount) return index + 1;
  }
  return text.length;
}

// Đọc chuỗi người dùng gõ hoặc dán thành số đồng. Chuỗi không còn chữ số là chưa nhập, tức `null`.
export function parseVndInput(text: string): number | null {
  const { significant } = stripLeadingZeros(extractDigits(text));
  if (significant === '') return null;
  // Phần chữ số thừa bị cắt, nên giá trị luôn nằm trong giới hạn kể cả với chuỗi rất dài.
  return Number(significant.slice(0, VND_MAX_DIGITS));
}

// Dấu chấm phân cách nghìn theo kiểu Việt Nam. Hàm tự viết, không phụ thuộc dữ liệu ngôn ngữ của trình duyệt.
export function formatVndInput(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return '';
  return groupThousands(String(Math.trunc(value)));
}

// Tính giá trị, chuỗi hiển thị và vị trí con trỏ sau mỗi lần gõ. Trả `null` khi số chữ số vượt giới hạn.
export function reformatVndInput(rawText: string, rawCaret: number): ReformattedMoneyInput | null {
  const digits = extractDigits(rawText);
  if (digits === '') return { value: null, text: '', caret: 0 };

  const { significant, strippedCount } = stripLeadingZeros(digits);
  // Từ chối thay vì cắt: cắt âm thầm sẽ lưu một giá khác với giá người dùng định nhập.
  if (significant.length > VND_MAX_DIGITS) return null;

  // Giữ con trỏ theo số chữ số đứng trước nó. Số ký tự thay đổi vì dấu chấm được thêm hoặc bớt.
  const digitsBeforeCaret = extractDigits(rawText.slice(0, rawCaret)).length;
  // Số 0 ở đầu bị bỏ nên không còn nằm trước con trỏ.
  const significantBeforeCaret = Math.min(
    Math.max(digitsBeforeCaret - strippedCount, 0),
    significant.length,
  );

  const text = groupThousands(significant);
  return {
    value: Number(significant),
    text,
    caret: caretAfterDigits(text, significantBeforeCaret),
  };
}
