// Lỗi theo từng ô nhập. Giá trị null hoặc thiếu khóa nghĩa là ô đó hợp lệ.
export type FieldErrors<FieldName extends string> = Partial<Record<FieldName, string | null>>;

// Id lỗi theo quy ước `<id ô>-error`, để ô và lỗi luôn nối được với nhau.
function toErrorId(inputId: string): string {
  return `${inputId}-error`;
}

// Thuộc tính cho ô sai: trình đọc màn hình đọc ô là không hợp lệ và đọc kèm câu lỗi.
export function fieldErrorProps(inputId: string, message: string | null | undefined) {
  if (!message) return {};
  return { 'aria-invalid': true as const, 'aria-describedby': toErrorId(inputId) };
}

// Ô có câu giải thích riêng: câu lỗi (nếu có) và câu giải thích cùng nối với ô bằng `aria-describedby`.
export function fieldPropsWithHint(
  inputId: string,
  hintId: string,
  message: string | null | undefined,
) {
  const errorProps = fieldErrorProps(inputId, message);
  const errorDescriptionId =
    'aria-describedby' in errorProps ? errorProps['aria-describedby'] : undefined;
  const describedBy = [errorDescriptionId, hintId].filter(Boolean).join(' ');
  return { ...errorProps, 'aria-describedby': describedBy };
}

// Có ít nhất một ô sai thì form không được gửi.
export function hasFieldErrors(errors: FieldErrors<string>): boolean {
  return Object.values(errors).some(Boolean);
}

interface FieldErrorProps {
  inputId: string;
  message: string | null | undefined;
}

// Câu lỗi nằm ngay dưới ô sai, theo nguyên tắc của `docs/design/design-system.md`.
export function FieldError({ inputId, message }: FieldErrorProps) {
  if (!message) return null;
  return (
    <span id={toErrorId(inputId)} className="form-error">
      {message}
    </span>
  );
}
