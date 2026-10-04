import { db } from '../../../shared/db';
import { AppError } from '../../../shared/errors';
import { getEmailSender } from '../data/email-sender';
import { emitUserLinked } from '../data/events';
import { loginThrottle } from '../security/login-throttle';
import { hashPassword, verifyPassword } from '../security/password';
import { generateToken, hashToken } from '../security/tokens';

// Hash Argon2id hợp lệ nhưng không thuộc ai, dùng để tốn thời gian băm như khi có user thật.
const DUMMY_HASH =
  '$argon2id$v=19$m=19456,t=2,p=1$Q29uc3RhbnRUaW1lU2FsdA$uF7hN93Qx2Tq89jFp1m5A0/X84f/7Dk06u6jJ7uV6U4';

export interface StaffLoginParams {
  email: string;
  password: string;
  ip: string;
}

export interface StaffLoginResult {
  user: {
    id: string;
    email: string;
    name: string | null;
    kind: 'staff';
  };
  token: string;
  expiresAt: Date;
}

// Đăng nhập nhân viên bằng email và mật khẩu, trả về phiên 12 giờ.
export async function staffLogin(params: StaffLoginParams): Promise<StaffLoginResult> {
  const { email, password, ip } = params;
  // Chuẩn hóa email để Abc@x.com và abc@x.com là cùng một tài khoản.
  const normalizedEmail = email.trim().toLowerCase();

  // Kiểm tra bộ đếm trước mọi thứ khác, để người đang bị khóa không tốn CPU băm mật khẩu của máy chủ.
  loginThrottle.assertNotLocked(normalizedEmail, ip);

  const user = await db.user.findUnique({
    where: { email: normalizedEmail },
  });

  // Gộp mọi lý do không đăng nhập được vào một nhánh để thông báo lỗi không tiết lộ tài khoản có tồn tại hay không.
  if (
    !user ||
    user.kind !== 'staff' ||
    user.lockedAt !== null ||
    user.deletedAt !== null ||
    !user.passwordHash
  ) {
    // Vẫn băm một lần với hash giả, để thời gian phản hồi gần bằng trường hợp user thật; nếu không, đo thời gian là đoán được email có tồn tại.
    await verifyPassword(DUMMY_HASH, password).catch(() => false);
    // Tính vào bộ đếm cả khi email không tồn tại, để kẻ dò email cũng bị giới hạn.
    loginThrottle.recordFailure(normalizedEmail, ip);
    throw new AppError('INVALID_CREDENTIALS', 401, 'Email hoặc mật khẩu không chính xác.');
  }

  const isValidPassword = await verifyPassword(user.passwordHash, password);
  if (!isValidPassword) {
    loginThrottle.recordFailure(normalizedEmail, ip);
    // Cùng mã lỗi và cùng thông báo với nhánh trên, để không phân biệt được "sai email" với "sai mật khẩu".
    throw new AppError('INVALID_CREDENTIALS', 401, 'Email hoặc mật khẩu không chính xác.');
  }

  // Đăng nhập đúng thì xóa bộ đếm sai, để lần gõ nhầm trước đó không cộng dồn.
  loginThrottle.recordSuccess(normalizedEmail, ip);

  // Token gốc chỉ trả về một lần để đặt vào cookie; DB chỉ giữ hash.
  const token = generateToken();
  const tokenHash = hashToken(token);
  // 12 giờ và không gia hạn, vì tài khoản nhân viên có quyền lớn nên cần giới hạn thời gian một phiên bị đánh cắp có thể dùng.
  const expiresAt = new Date(Date.now() + 12 * 60 * 60 * 1000);

  await db.session.create({
    data: {
      tokenHash,
      userId: user.id,
      expiresAt,
    },
  });

  // Nhân viên đăng nhập không phát UserLinkedEvent vì họ không có giỏ hàng khách cần gộp.
  return {
    user: {
      id: user.id,
      email: user.email,
      name: user.name,
      kind: 'staff',
    },
    token,
    expiresAt,
  };
}

export interface CustomerRegisterParams {
  email: string;
  password: string;
  name?: string;
  phone?: string;
  anonymousId: string;
  ip: string;
}

export interface AuthResult {
  user: {
    id: string;
    email: string;
    name: string | null;
    phone: string | null;
    kind: 'customer';
  };
  token: string;
  expiresAt: Date;
}

// Đăng ký khách bằng email và mật khẩu, đăng nhập luôn và gửi email xác thực.
export async function customerRegister(params: CustomerRegisterParams): Promise<AuthResult> {
  const { email, password, name, phone, anonymousId } = params;
  const normalizedEmail = email.trim().toLowerCase();

  // Kiểm tra lại độ dài dù route đã kiểm, vì hàm này có thể được gọi từ nơi khác.
  if (password.length < 8) {
    throw new AppError('WEAK_PASSWORD', 400, 'Mật khẩu phải chứa tối thiểu 8 ký tự.');
  }

  const existingUser = await db.user.findUnique({
    where: { email: normalizedEmail },
  });

  // Dùng một thông báo cho cả email khách và email nhân viên, để không ai dò được email nào thuộc nhân viên.
  if (existingUser) {
    throw new AppError(
      'EMAIL_ALREADY_EXISTS',
      400,
      'Email này đã được sử dụng. Vui lòng đăng nhập hoặc chọn email khác.',
    );
  }

  const customerRole = await db.role.findUnique({
    where: { code: 'customer' },
  });
  // Thiếu role customer là lỗi cấu hình (chưa chạy seed), không phải lỗi người dùng, nên ném lỗi thường để ra 500.
  if (!customerRole) {
    throw new Error('Role customer chưa được seed trong database');
  }

  const passwordHash = await hashPassword(password);

  const newUser = await db.user.create({
    data: {
      email: normalizedEmail,
      // Chưa xác thực cho tới khi người dùng bấm link trong email.
      emailVerified: false,
      passwordHash,
      // Chuỗi rỗng đổi thành null để DB không lưu tên trống.
      name: name?.trim() || null,
      phone: phone?.trim() || null,
      kind: 'customer',
      roles: {
        create: {
          roleId: customerRole.id,
        },
      },
    },
  });

  // Link xác thực hạn 24 giờ theo spec; hash lưu DB, token gốc chỉ nằm trong email.
  const verifyToken = generateToken();
  const verifyTokenHash = hashToken(verifyToken);
  const verifyExpiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000);

  await db.emailToken.create({
    data: {
      tokenHash: verifyTokenHash,
      userId: newUser.id,
      purpose: 'verify',
      expiresAt: verifyExpiresAt,
    },
  });

  await getEmailSender().send({
    to: newUser.email,
    subject: 'Xác thực tài khoản Pickory',
    text: `Chào bạn, vui lòng truy cập link sau để xác thực tài khoản: /verify-email?token=${verifyToken}`,
    // metadata cho test và dev lấy token mà không phải cắt chuỗi từ nội dung thư.
    metadata: { token: verifyToken, purpose: 'verify' },
  });

  // Đăng nhập luôn sau khi đăng ký để khách không phải nhập lại thông tin vừa gõ; phiên khách 30 ngày.
  const sessionToken = generateToken();
  const sessionTokenHash = hashToken(sessionToken);
  const sessionExpiresAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000);

  await db.session.create({
    data: {
      tokenHash: sessionTokenHash,
      userId: newUser.id,
      expiresAt: sessionExpiresAt,
    },
  });

  // Phát sự kiện ở cuối, khi mọi thứ đã lưu xong, để module giỏ hàng gộp dữ liệu khách vãng lai vào tài khoản mới.
  await emitUserLinked(anonymousId, newUser.id);

  return {
    user: {
      id: newUser.id,
      email: newUser.email,
      name: newUser.name,
      phone: newUser.phone,
      kind: 'customer',
    },
    token: sessionToken,
    expiresAt: sessionExpiresAt,
  };
}

export interface CustomerLoginParams {
  email: string;
  password: string;
  anonymousId: string;
  ip: string;
}

// Đăng nhập khách bằng email và mật khẩu, trả về phiên 30 ngày.
export async function customerLogin(params: CustomerLoginParams): Promise<AuthResult> {
  const { email, password, anonymousId, ip } = params;
  const normalizedEmail = email.trim().toLowerCase();

  // Chặn sớm người đang bị khóa, trước khi tốn CPU băm mật khẩu.
  loginThrottle.assertNotLocked(normalizedEmail, ip);

  const user = await db.user.findUnique({
    where: { email: normalizedEmail },
  });

  // Gộp mọi lý do không đăng nhập được, gồm cả tài khoản nhân viên (kind khác customer), để không lộ thông tin.
  if (
    !user ||
    user.kind !== 'customer' ||
    user.lockedAt !== null ||
    user.deletedAt !== null ||
    !user.passwordHash
  ) {
    // Băm với hash giả để thời gian phản hồi không tiết lộ email có tồn tại hay không.
    await verifyPassword(DUMMY_HASH, password).catch(() => false);
    loginThrottle.recordFailure(normalizedEmail, ip);
    throw new AppError('INVALID_CREDENTIALS', 401, 'Email hoặc mật khẩu không chính xác.');
  }

  const isValidPassword = await verifyPassword(user.passwordHash, password);
  if (!isValidPassword) {
    loginThrottle.recordFailure(normalizedEmail, ip);
    throw new AppError('INVALID_CREDENTIALS', 401, 'Email hoặc mật khẩu không chính xác.');
  }

  loginThrottle.recordSuccess(normalizedEmail, ip);

  // Phiên khách 30 ngày (và tự gia hạn trong middleware) để khách mua sắm không phải đăng nhập liên tục.
  const token = generateToken();
  const tokenHash = hashToken(token);
  const expiresAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000);

  await db.session.create({
    data: {
      tokenHash,
      userId: user.id,
      expiresAt,
    },
  });

  // Gộp giỏ hàng và lịch sử xem của khách vãng lai vào tài khoản; handler lỗi chỉ bị log nên không làm đăng nhập thất bại.
  await emitUserLinked(anonymousId, user.id);

  return {
    user: {
      id: user.id,
      email: user.email,
      name: user.name,
      phone: user.phone,
      kind: 'customer',
    },
    token,
    expiresAt,
  };
}

// Đăng xuất: xóa phiên khỏi DB để token cũ không dùng lại được.
export async function logout(sessionToken?: string): Promise<void> {
  // Không có cookie thì không có gì để xóa, và đăng xuất lúc chưa đăng nhập không phải lỗi.
  if (!sessionToken) return;
  // DB chỉ lưu hash nên phải băm token trước khi tìm.
  const tokenHash = hashToken(sessionToken);
  // deleteMany thay vì delete để không ném lỗi khi phiên đã bị xóa sẵn.
  await db.session.deleteMany({
    where: { tokenHash },
  });
}
