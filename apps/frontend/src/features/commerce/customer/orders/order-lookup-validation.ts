import { getEmailError } from '../../../../shared/identity-validation';

// Ba hằng dưới đây khớp `OrderCodeGenerator.cs` ở backend (`Prefix`, `CodeLength`, `Alphabet`). Đổi một nơi thì đổi cả nơi kia.
const ORDER_CODE_PREFIX = 'PK';
const ORDER_CODE_LENGTH = 10;
const ORDER_CODE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

// Câu lỗi giống `OrderLookupRequests.cs`, để lỗi ở trình duyệt và lỗi từ server đọc như nhau.
const ORDER_CODE_REQUIRED_MESSAGE = 'Cần nhập mã đơn hàng.';
const ORDER_CODE_INVALID_MESSAGE = 'Mã đơn hàng không đúng định dạng.';
const EMAIL_REQUIRED_MESSAGE = 'Cần nhập email.';
const EMAIL_INVALID_MESSAGE = 'Email không đúng định dạng.';

// Backend cắt khoảng trắng và đổi sang chữ hoa trước khi so mã đơn. Client làm giống để mã gõ thường vẫn khớp.
export function normalizeOrderCode(rawOrderCode: string): string {
  return rawOrderCode.trim().toUpperCase();
}

// Mọi ký tự sau tiền tố phải thuộc bảng chữ. Bảng chữ bỏ các ký tự dễ nhầm như `0`, `1`, `I`, `O`.
function hasOnlyAlphabetCharacters(text: string): boolean {
  return [...text].every((character) => ORDER_CODE_ALPHABET.includes(character));
}

export function getOrderCodeError(rawOrderCode: string): string | null {
  const orderCode = normalizeOrderCode(rawOrderCode);
  if (!orderCode) return ORDER_CODE_REQUIRED_MESSAGE;
  if (orderCode.length !== ORDER_CODE_LENGTH) return ORDER_CODE_INVALID_MESSAGE;
  if (!orderCode.startsWith(ORDER_CODE_PREFIX)) return ORDER_CODE_INVALID_MESSAGE;
  const randomPart = orderCode.slice(ORDER_CODE_PREFIX.length);
  if (!hasOnlyAlphabetCharacters(randomPart)) return ORDER_CODE_INVALID_MESSAGE;
  return null;
}

// Cắt khoảng trắng trước khi kiểm, vì backend cắt email trước khi so khớp.
export function getLookupEmailError(rawEmail: string): string | null {
  const email = rawEmail.trim();
  if (!email) return EMAIL_REQUIRED_MESSAGE;
  // Dùng luật chung của form identity. Câu lỗi được đổi sang câu của route tra cứu.
  if (getEmailError(email)) return EMAIL_INVALID_MESSAGE;
  return null;
}
