import { Prisma } from '../../../generated/prisma/client';
import type { PrismaClient } from '../../../generated/prisma/client';

// Tên trường chứa bí mật; khớp một phần tên để bắt cả accessToken, newPassword, passwordHash.
const SENSITIVE_KEY_REGEXP = /(password|token|secret|apikey|cvv|cardnumber|hash)/i;

// Xóa giá trị nhạy cảm khỏi dữ liệu trước khi ghi nhật ký.
export function sanitizeAuditData(data: unknown): unknown {
  // Giá trị rỗng giữ nguyên để nhật ký phân biệt được "không có" với "có giá trị".
  if (data === null || data === undefined) {
    return data;
  }
  // Số, chuỗi, boolean không có tên trường để xét nên giữ nguyên.
  if (typeof data !== 'object') {
    return data;
  }
  // Mảng thì làm sạch từng phần tử, vì bí mật có thể nằm trong object con của mảng.
  if (Array.isArray(data)) {
    return data.map((item) => sanitizeAuditData(item));
  }
  // Date chuyển sang chuỗi ISO vì cột before/after là JSON, không lưu được đối tượng Date.
  if (data instanceof Date) {
    return data.toISOString();
  }

  const record = data as Record<string, unknown>;
  const result: Record<string, unknown> = {};

  for (const [key, val] of Object.entries(record)) {
    // Bỏ gạch nối và gạch dưới trước khi so khớp để bắt cả api_key, card-number.
    if (SENSITIVE_KEY_REGEXP.test(key.replace(/[-_]/g, ''))) {
      // Giữ lại tên trường nhưng giấu giá trị, để người xem biết trường đó từng được đổi.
      result[key] = '[REDACTED]';
    } else if (typeof val === 'object' && val !== null) {
      // Đệ quy vì bí mật có thể nằm sâu trong object lồng nhau.
      result[key] = sanitizeAuditData(val);
    } else {
      result[key] = val;
    }
  }

  return result;
}

export interface RecordAuditParams {
  // Người thực hiện thao tác; lưu id để lịch sử vẫn đúng khi tên người đó đổi.
  actorId: string;
  // Tên hành động theo mẫu 'đối_tượng.hành_động', ví dụ 'role.assign'.
  action: string;
  targetType: string;
  targetId: string;
  // Trạng thái trước và sau thao tác, để xem lại chính xác cái gì đã đổi.
  before?: unknown;
  after?: unknown;
}

// Ghi một dòng nhật ký thao tác nhân viên.
export async function recordAudit(
  // Nhận tx để ghi cùng transaction với thao tác chính: thao tác lỗi rollback thì dòng nhật ký cũng không còn.
  tx: PrismaClient | Prisma.TransactionClient,
  params: RecordAuditParams,
) {
  // Luôn làm sạch ở đây thay vì tin người gọi, để không bao giờ lưu bí mật vào nhật ký.
  const sanitizedBefore =
    params.before !== undefined ? sanitizeAuditData(params.before) : undefined;
  const sanitizedAfter = params.after !== undefined ? sanitizeAuditData(params.after) : undefined;

  return tx.auditLog.create({
    data: {
      actorId: params.actorId,
      action: params.action,
      targetType: params.targetType,
      targetId: params.targetId,
      // Prisma bắt dùng Prisma.JsonNull để lưu JSON null; truyền null thường sẽ bị hiểu là bỏ qua trường.
      before:
        sanitizedBefore !== undefined
          ? (sanitizedBefore as Prisma.InputJsonValue)
          : Prisma.JsonNull,
      after:
        sanitizedAfter !== undefined ? (sanitizedAfter as Prisma.InputJsonValue) : Prisma.JsonNull,
    },
  });
}
