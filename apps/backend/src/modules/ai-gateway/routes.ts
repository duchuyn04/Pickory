import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../../shared/errors';
import { requirePermission, requireStaff } from '../identity';
import { getAiSettings, updateAiSettings } from './settings';
import { getUsageSummary } from './usage-summary';

// Router quản trị cho module ai-gateway, cung cấp các endpoint theo dõi và cấu hình AI.
export const aiRouter = Router();

// Schema kiểm tra tham số truy vấn khoảng thời gian cho báo cáo sử dụng.
// Yêu cầu chuỗi ngày giờ định dạng chuẩn ISO 8601 UTC (kết thúc bằng đuôi Z).
const usageQuerySchema = z
  .object({
    from: z
      .string()
      .datetime({ message: 'Tham số from phải là chuỗi ngày giờ ISO 8601 UTC hợp lệ.' })
      .optional(),
    to: z
      .string()
      .datetime({ message: 'Tham số to phải là chuỗi ngày giờ ISO 8601 UTC hợp lệ.' })
      .optional(),
  })
  .refine(
    (data) => {
      // Khi gửi cả hai mốc thời gian, mốc bắt đầu bắt buộc phải trước mốc kết thúc để đảm bảo logic khoảng thời gian.
      if (data.from && data.to) {
        return new Date(data.from).getTime() < new Date(data.to).getTime();
      }
      return true;
    },
    {
      message: 'Khoảng thời gian không hợp lệ: from phải trước to.',
      path: ['from'],
    },
  );

// Schema kiểm tra dữ liệu cấu hình AI gửi lên khi cập nhật.
// Dùng cú pháp message trực tiếp của Zod thay cho required_error và invalid_type_error.
// Nghiêm ngặt không cho phép trường lạ (strict) để ngăn ngừa dữ liệu cấu hình ngoài ý muốn.
const updateSettingsSchema = z
  .object({
    monthlyBudgetUsd: z
      .number({ message: 'monthlyBudgetUsd phải là số.' })
      .finite({ message: 'monthlyBudgetUsd phải là số hữu hạn.' })
      .min(0, { message: 'Ngân sách tháng không được là số âm.' }),
    chatDailyLimitUser: z
      .number({ message: 'chatDailyLimitUser phải là số.' })
      .int({ message: 'chatDailyLimitUser phải là số nguyên.' })
      .min(1, { message: 'Hạn mức chat ngày của người dùng phải là số nguyên dương.' }),
    chatDailyLimitAnon: z
      .number({ message: 'chatDailyLimitAnon phải là số.' })
      .int({ message: 'chatDailyLimitAnon phải là số nguyên.' })
      .min(1, { message: 'Hạn mức chat ngày của khách vãng lai phải là số nguyên dương.' }),
  })
  .strict();

// GET /api/admin/ai/usage: xem báo cáo tổng hợp sử dụng AI trong khoảng thời gian.
// Chỉ nhân viên có quyền ai-usage:read mới được phép truy cập số liệu.
aiRouter.get(
  '/api/admin/ai/usage',
  requireStaff,
  requirePermission('ai-usage:read'),
  async (req, res, next) => {
    try {
      // Xác thực cấu trúc tham số query gửi lên để tránh lỗi kiểu dữ liệu ở tầng truy vấn DB.
      const query = usageQuerySchema.parse(req.query);

      // Thiết lập khoảng thời gian mặc định 30 ngày gần nhất nếu client không chỉ định mốc thời gian.
      const now = new Date();
      const defaultFrom = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000);
      const fromDate = query.from ? new Date(query.from) : defaultFrom;
      const toDate = query.to ? new Date(query.to) : now;

      // Bảo vệ bất biến thời gian: ngày bắt đầu luôn phải trước ngày kết thúc sau khi điền giá trị mặc định.
      if (fromDate.getTime() >= toDate.getTime()) {
        throw new AppError(
          'INVALID_DATE_RANGE',
          400,
          'Khoảng thời gian không hợp lệ: from phải trước to.',
        );
      }
      // Giới hạn một quý để query từ nhiều năm trước không tải toàn bộ lịch sử vào bộ nhớ API.
      if (toDate.getTime() - fromDate.getTime() > 92 * 24 * 60 * 60 * 1000) {
        throw new AppError(
          'INVALID_DATE_RANGE',
          400,
          'Báo cáo AI chỉ hỗ trợ khoảng thời gian tối đa 92 ngày.',
        );
      }

      // Tổng hợp dữ liệu từ bảng nhật ký theo khoảng thời gian đã xác thực.
      const summary = await getUsageSummary({ from: fromDate, to: toDate });

      res.json({ data: summary });
    } catch (err) {
      next(err);
    }
  },
);

// GET /api/admin/ai/settings: xem cấu hình ngân sách và hạn mức hiện tại.
// Yêu cầu nhân viên có quyền ai-usage:read để xem được cấu hình phục vụ giám sát.
aiRouter.get(
  '/api/admin/ai/settings',
  requireStaff,
  requirePermission('ai-usage:read'),
  async (_req, res, next) => {
    try {
      // Lấy cấu hình AI hiện hành từ cơ sở dữ liệu.
      const settings = await getAiSettings();

      res.json({ data: settings });
    } catch (err) {
      next(err);
    }
  },
);

// PUT /api/admin/ai/settings: cập nhật ngân sách và hạn mức AI.
// Yêu cầu quyền ai-settings:manage và bắt buộc định danh nhân viên hợp lệ để ghi audit log.
aiRouter.put(
  '/api/admin/ai/settings',
  requireStaff,
  requirePermission('ai-settings:manage'),
  async (req, res, next) => {
    try {
      // Xác thực nghiêm ngặt body request để bảo vệ tính toàn vẹn dữ liệu cài đặt.
      const validatedBody = updateSettingsSchema.parse(req.body);

      // Từ chối xử lý nếu không có userId nhân viên, không bao giờ dùng giá trị giả lập để tránh sai lệch audit log.
      if (!req.actor?.userId) {
        throw new AppError(
          'UNAUTHORIZED',
          401,
          'Vui lòng đăng nhập với tài khoản nhân viên để thực hiện thao tác này.',
        );
      }
      const actorId = req.actor.userId;

      // Cập nhật cấu hình nguyên tử kèm ghi nhật ký kiểm toán trong transaction.
      const updated = await updateAiSettings(validatedBody, actorId);

      res.json({ data: updated });
    } catch (err) {
      next(err);
    }
  },
);
