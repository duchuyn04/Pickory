import type { ErrorRequestHandler, RequestHandler } from 'express';
import { ZodError } from 'zod';
import { logger } from './logger';

/**
 * Lỗi nghiệp vụ có chủ đích. Ném lỗi này trong service/route,
 * middleware `errorHandler` sẽ đổi thành response `{ error: { code, message } }`.
 * `message` viết bằng tiếng Việt vì được hiển thị thẳng cho người dùng.
 */
export class AppError extends Error {
  constructor(
    public readonly code: string, // mã máy đọc được, ví dụ 'PRODUCT_NOT_FOUND'
    public readonly status: number, // HTTP status, ví dụ 404
    message: string,
  ) {
    super(message);
  }
}

// Chạy sau mọi route: request không khớp route nào thì trả 404 theo đúng mẫu lỗi chung.
export const notFoundHandler: RequestHandler = (_req, _res, next) => {
  next(new AppError('NOT_FOUND', 404, 'Không tìm thấy tài nguyên.'));
};

/**
 * Middleware lỗi duy nhất của API (phải đăng ký cuối cùng).
 * Ba nhánh: lỗi nghiệp vụ, lỗi validate Zod, và mọi lỗi còn lại.
 */
export const errorHandler: ErrorRequestHandler = (err, req, res, next) => {
  // Response đã gửi một phần thì không viết lại được, giao cho handler mặc định của Express.
  if (res.headersSent) {
    next(err);
    return;
  }
  // 1. Lỗi nghiệp vụ: trả đúng code, status, message mà code ném ra.
  if (err instanceof AppError) {
    res.status(err.status).json({ error: { code: err.code, message: err.message } });
    return;
  }
  // 2. Input không hợp lệ (body, query, params): luôn là lỗi của client nên trả 400.
  if (err instanceof ZodError) {
    const detail = err.issues.map((i) => `${i.path.join('.') || 'body'}: ${i.message}`).join('; ');
    res
      .status(400)
      .json({ error: { code: 'VALIDATION_ERROR', message: `Dữ liệu không hợp lệ (${detail}).` } });
    return;
  }
  // 3. Lỗi không lường trước: ghi log đầy đủ để debug, nhưng response không lộ stack hay chi tiết nội bộ.
  logger.error({ err, method: req.method, url: req.originalUrl }, 'unhandled error');
  res.status(500).json({
    error: { code: 'INTERNAL_ERROR', message: 'Đã có lỗi xảy ra, vui lòng thử lại sau.' },
  });
};
