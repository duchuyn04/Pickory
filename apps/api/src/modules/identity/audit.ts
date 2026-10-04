import { Prisma } from '../../generated/prisma/client';
import type { PrismaClient } from '../../generated/prisma/client';

const SENSITIVE_KEY_REGEXP = /(password|token|secret|apikey|cvv|cardnumber|hash)/i;

/**
 * Loại bỏ đệ quy các trường nhạy cảm khỏi dữ liệu trước khi ghi nhật ký kiểm toán.
 */
export function sanitizeAuditData(data: unknown): unknown {
  if (data === null || data === undefined) {
    return data;
  }
  if (typeof data !== 'object') {
    return data;
  }
  if (Array.isArray(data)) {
    return data.map((item) => sanitizeAuditData(item));
  }
  if (data instanceof Date) {
    return data.toISOString();
  }

  const record = data as Record<string, unknown>;
  const result: Record<string, unknown> = {};

  for (const [key, val] of Object.entries(record)) {
    // Xóa bỏ dấu gạch nối và gạch dưới trước khi kiểm tra để bắt được cả snake_case và kebab-case như api-key, card_number.
    if (SENSITIVE_KEY_REGEXP.test(key.replace(/[-_]/g, ''))) {
      result[key] = '[REDACTED]';
    } else if (typeof val === 'object' && val !== null) {
      // Đệ quy để lọc sạch dữ liệu nhạy cảm lồng trong object hoặc mảng con.
      result[key] = sanitizeAuditData(val);
    } else {
      result[key] = val;
    }
  }

  return result;
}

export interface RecordAuditParams {
  actorId: string;
  action: string;
  targetType: string;
  targetId: string;
  before?: unknown;
  after?: unknown;
}

/**
 * Ghi nhật ký thao tác (audit log) vào database.
 * Nhận transaction client (`tx`) để có thể ghi trong cùng transaction với thao tác chính.
 */
export async function recordAudit(
  tx: PrismaClient | Prisma.TransactionClient,
  params: RecordAuditParams,
) {
  // Luôn làm sạch dữ liệu trước khi lưu để phòng ngừa rò rỉ bí mật vào nhật ký kiểm toán.
  const sanitizedBefore =
    params.before !== undefined ? sanitizeAuditData(params.before) : undefined;
  const sanitizedAfter = params.after !== undefined ? sanitizeAuditData(params.after) : undefined;

  return tx.auditLog.create({
    data: {
      actorId: params.actorId,
      action: params.action,
      targetType: params.targetType,
      targetId: params.targetId,
      // Prisma yêu cầu dùng JsonNull thay vì null thông thường để phân biệt rõ giá trị JSON null với việc bỏ qua trường.
      before:
        sanitizedBefore !== undefined
          ? (sanitizedBefore as Prisma.InputJsonValue)
          : Prisma.JsonNull,
      after:
        sanitizedAfter !== undefined ? (sanitizedAfter as Prisma.InputJsonValue) : Prisma.JsonNull,
    },
  });
}
