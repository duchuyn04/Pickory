// Kiểu chữ từng màn theo token Figma: màu, cỡ, nhãn 600 và chú thích 400.
// Độ đậm 500 của nhãn thẻ cũ không phải token mà đo từ chiều ngang chữ trong ảnh Figma (lệch tối đa 1px).
export interface TextStyleCheck {
  // `link` tìm theo vai trò liên kết, `footerLink` chỉ tìm trong chân trang. `text` tìm theo chữ hiển thị.
  kind: 'text' | 'link' | 'footerLink';
  text: string;
  fontSize: string;
  fontWeight: string;
  color: string;
  lineHeight?: string;
}

const INK = 'rgb(27, 31, 36)';
const MUTED = 'rgb(91, 100, 112)';
const LINK = 'rgb(42, 111, 214)';
const DANGER = 'rgb(180, 35, 24)';

function label(text: string, fontWeight = '600'): TextStyleCheck {
  return { kind: 'text', text, fontSize: '13px', fontWeight, color: INK };
}

function muted(text: string, fontSize = '14px', lineHeight?: string): TextStyleCheck {
  return { kind: 'text', text, fontSize, fontWeight: '400', color: MUTED, lineHeight };
}

function link(text: string, fontSize = '14px', fontWeight = '600'): TextStyleCheck {
  return { kind: 'link', text, fontSize, fontWeight, color: LINK };
}

const footerLinks = ['Liên hệ CSKH', 'Phí giao hàng', 'Quyền riêng tư'].map(
  (text): TextStyleCheck => ({ ...link(text), kind: 'footerLink' }),
);

const loginChecks: TextStyleCheck[] = [
  label('Email'),
  label('Mật khẩu'),
  link('Quên mật khẩu?', '13px'),
  link('Đăng ký ngay'),
  muted('Đăng nhập tài khoản khách hàng Pickory', '14px', '22px'),
  muted('hoặc đăng nhập bằng email', '12px'),
];

const registerChecks: TextStyleCheck[] = [
  label('Email *'),
  label('Họ và tên (tùy chọn)'),
  link('Đăng nhập ngay'),
  muted('Tạo tài khoản để quản lý đơn hàng và nhận ưu đãi', '14px', '22px'),
  muted('hoặc đăng ký bằng email', '12px'),
  muted(
    'Khi tạo tài khoản, bạn đồng ý với chính sách quyền riêng tư và cookie của Pickory.',
    '12px',
  ),
];

const adminLoginChecks: TextStyleCheck[] = [
  label('Email nhân viên'),
  label('Mật khẩu'),
  muted('Đăng nhập dành cho nhân viên quản trị', '14px', '22px'),
  muted('Tài khoản do quản trị viên cấp. Liên hệ quản trị viên nếu bạn quên mật khẩu.', '12px'),
];

export const figmaTextStyles: Record<string, TextStyleCheck[]> = {
  'TK-01': loginChecks,
  'TK-02': loginChecks,
  'TK-03': registerChecks,
  'TK-04': [
    ...registerChecks,
    {
      kind: 'text',
      text: 'Email không được để trống.',
      fontSize: '12px',
      fontWeight: '400',
      color: DANGER,
    },
  ],
  'TK-05': [
    label('Email tài khoản', '500'),
    muted('Nhập email để nhận liên kết đặt lại mật khẩu', '14px', '21px'),
    link('Quay lại đăng nhập'),
    ...footerLinks,
  ],
  'TK-06': [
    label('Mật khẩu mới (tối thiểu 8 ký tự)', '500'),
    muted('Nhập mật khẩu mới cho tài khoản của bạn', '14px', '21px'),
    ...footerLinks,
  ],
  'TK-07': [
    muted('Liên kết không hợp lệ, đã hết hạn hoặc đã được sử dụng.', '14px'),
    ...footerLinks,
  ],
  'TK-08': [
    label('Họ và tên', '500'),
    muted('Bạn chưa lưu địa chỉ nào. Thêm địa chỉ để đặt hàng nhanh hơn.', '14px', '21px'),
    muted('Theo dõi tiến trình vận chuyển và lịch sử mua sắm của bạn.', '13px', '19.5px'),
    ...footerLinks,
  ],
  'TK-09': [
    muted(
      'Duy trì phiên đăng nhập, giỏ hàng và bảo mật hệ thống. Luôn bật và không thể tắt.',
      '12px',
    ),
    ...footerLinks,
  ],
  'QT-01': adminLoginChecks,
  'QT-02': adminLoginChecks,
  'QT-03': [muted('Tổng quan back-office của Pickory.', '14px')],
};
