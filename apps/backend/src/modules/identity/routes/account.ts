import { Router } from 'express';
import { z } from 'zod';
import { db } from '../../../shared/db';
import { requireAuth } from '../security/middleware';
import { changePassword, deleteCustomerAccount, updateProfile } from '../services/account';
import { saveConsent } from '../services/consent';

// Tài khoản của chính người đang đăng nhập: xem, sửa hồ sơ, đổi mật khẩu, lựa chọn đồng ý, xóa tài khoản.
export const accountRouter = Router();

// GET /api/me: cho giao diện biết người đang dùng là ai, có quyền gì (công khai, khách vãng lai cũng gọi được).
accountRouter.get('/api/me', async (req, res, next) => {
  try {
    let email: string | null = null;
    let name: string | null = null;
    let phone: string | null = null;

    // Chỉ người đã đăng nhập mới có hồ sơ để đọc; khách vãng lai giữ các giá trị null.
    if (req.actor.userId) {
      const u = await db.user.findUnique({
        where: { id: req.actor.userId },
        // Chỉ chọn ba trường cần hiển thị, không lấy cả đối tượng user.
        select: { email: true, name: true, phone: true },
      });
      if (u) {
        email = u.email;
        name = u.name;
        phone = u.phone;
      }
    }

    res.json({
      data: {
        anonymousId: req.actor.anonymousId,
        userId: req.actor.userId,
        email,
        name,
        phone,
        kind: req.actor.kind,
        roles: req.actor.roles,
        // Set không chuyển thành JSON được nên đổi sang mảng.
        permissions: Array.from(req.actor.permissions),
        consent: req.actor.consent,
      },
    });
  } catch (err) {
    next(err);
  }
});

const consentInputSchema = z.object({
  // Nhóm cần thiết luôn bật, không tắt được; chỉ chấp nhận true.
  necessary: z.literal(true).default(true),
  personalization: z.boolean(),
  // Marketing mặc định tắt nếu client không gửi.
  marketing: z.boolean().default(false),
});

// PUT /api/me/consent: lưu lựa chọn đồng ý (công khai, vì khách vãng lai cũng cần chọn).
accountRouter.put('/api/me/consent', async (req, res, next) => {
  try {
    const { personalization, marketing } = consentInputSchema.parse(req.body);
    const record = await saveConsent({
      anonymousId: req.actor.anonymousId,
      userId: req.actor.userId,
      personalization,
      marketing,
    });

    res.json({
      data: {
        consent: {
          necessary: true,
          personalization: record.personalization,
          marketing: record.marketing,
        },
      },
    });
  } catch (err) {
    next(err);
  }
});

const updateProfileSchema = z.object({
  // Cho phép null để người dùng xóa trống trường; không gửi (undefined) nghĩa là không đổi.
  name: z.string().nullable().optional(),
  phone: z.string().nullable().optional(),
});

// PATCH /api/me: sửa tên và số điện thoại của chính mình.
accountRouter.patch('/api/me', requireAuth, async (req, res, next) => {
  try {
    const { name, phone } = updateProfileSchema.parse(req.body);
    // Dấu ! an toàn vì requireAuth đã chặn người chưa đăng nhập; luôn dùng userId của actor, không nhận id từ client.
    const updated = await updateProfile({
      userId: req.actor.userId!,
      name,
      phone,
    });

    res.json({ data: { user: updated } });
  } catch (err) {
    next(err);
  }
});

const changePasswordSchema = z.object({
  // Tùy chọn vì tài khoản chỉ dùng Google chưa có mật khẩu cũ để nhập.
  currentPassword: z.string().optional(),
  newPassword: z
    .string()
    .min(8, 'Mật khẩu mới phải chứa tối thiểu 8 ký tự.')
    .max(128, 'Mật khẩu không được vượt quá 128 ký tự.'),
});

// POST /api/me/password: đổi mật khẩu.
accountRouter.post('/api/me/password', requireAuth, async (req, res, next) => {
  try {
    const { currentPassword, newPassword } = changePasswordSchema.parse(req.body);
    await changePassword({
      userId: req.actor.userId!,
      currentPassword,
      newPassword,
    });

    // Mọi phiên đã bị xóa trong DB, kể cả phiên hiện tại, nên xóa cookie để trình duyệt về trạng thái đăng xuất.
    res.clearCookie('pk_session', { httpOnly: true, sameSite: 'lax' });
    res.json({
      data: {
        success: true,
        message: 'Đổi mật khẩu thành công. Mọi phiên đăng nhập cũ đã được xóa.',
      },
    });
  } catch (err) {
    next(err);
  }
});

const deleteAccountSchema = z.object({
  // Tùy chọn vì tài khoản chỉ dùng Google xác nhận bằng cách đăng nhập lại, không có mật khẩu.
  password: z.string().optional(),
});

// DELETE /api/me: khách tự xóa tài khoản sau khi xác nhận lại.
accountRouter.delete('/api/me', requireAuth, async (req, res, next) => {
  try {
    const { password } = deleteAccountSchema.parse(req.body);
    await deleteCustomerAccount({
      userId: req.actor.userId!,
      password,
      // Dùng để kiểm tra "vừa đăng nhập lại Google trong 5 phút" với tài khoản không có mật khẩu.
      sessionCreatedAt: req.actor.sessionCreatedAt,
    });
    res.clearCookie('pk_session', { httpOnly: true, sameSite: 'lax' });
    res.json({
      data: {
        success: true,
        message: 'Tài khoản của bạn đã được xóa thành công.',
      },
    });
  } catch (err) {
    next(err);
  }
});
