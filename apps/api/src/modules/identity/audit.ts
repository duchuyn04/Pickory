import { Prisma } from '../../generated/prisma/client';
import type { PrismaClient } from '../../generated/prisma/client';

const SENSITIVE_KEY_REGEXP = /(password|token|secret|apikey|cvv|cardnumber|hash)/i;

/**
 * Loại bỏ đệ quy các trường nhạy cảm khỏi dữ liệu trước khi ghi audit log.
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
    if (SENSITIVE_KEY_REGEXP.test(key.replace(/[-_]/g, ''))) {
      result[key] = '[REDACTED]';
    } else if (typeof val === 'object' && val !== null) {
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
  const sanitizedBefore =
    params.before !== undefined ? sanitizeAuditData(params.before) : undefined;
  const sanitizedAfter = params.after !== undefined ? sanitizeAuditData(params.after) : undefined;

  return tx.auditLog.create({
    data: {
      actorId: params.actorId,
      action: params.action,
      targetType: params.targetType,
      targetId: params.targetId,
      before:
        sanitizedBefore !== undefined
          ? (sanitizedBefore as Prisma.InputJsonValue)
          : Prisma.JsonNull,
      after:
        sanitizedAfter !== undefined ? (sanitizedAfter as Prisma.InputJsonValue) : Prisma.JsonNull,
    },
  });
}
