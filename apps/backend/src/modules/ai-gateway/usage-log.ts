import { createHash } from 'node:crypto';
import type { PrismaClient } from '../../generated/prisma/client';
import { logger } from '../../shared/logger';
import { calculateTokenCostUsd } from './pricing';
import type { AiFeature, TokenUsage } from './types';

export type UsageStatus = 'ok' | 'error' | 'blocked';

export type PersistUsageLogParams = {
  feature: AiFeature;
  provider: string;
  model: string;
  promptVersion?: string;
  usage?: TokenUsage;
  costUsd?: number;
  latencyMs: number;
  status: UsageStatus;
  errorCode?: string;
  actorKey?: string;
  createdAt?: Date;
};

// Băm actorKey bằng SHA-256 để bảo vệ quyền riêng tư người dùng, không bao giờ lưu ID gốc.
export function hashActorKey(actorKey?: string): string | null {
  // Trả về null nếu không có actorKey để không băm chuỗi rỗng gây nhầm lẫn.
  if (!actorKey || !actorKey.trim()) {
    return null;
  }
  // Dùng SHA-256 một chiều để đối soát hạn mức mà không lộ danh tính userId hoặc anonymousId.
  return createHash('sha256').update(actorKey.trim()).digest('hex');
}

// Ghi một bản ghi nhật ký sử dụng AI vào cơ sở dữ liệu.
export async function persistUsageLog(
  store: PrismaClient,
  params: PersistUsageLogParams,
): Promise<void> {
  // Băm actorKey trước khi lưu xuống bảng nhật ký để ngăn rò rỉ định danh người dùng.
  const actorKeyHash = hashActorKey(params.actorKey);

  // Tính chi phí USD một cách trung thực: bảo toàn chi phí thực tế cho mọi bước đã tiêu tốn token.
  let resolvedCostUsd = params.costUsd;
  if (resolvedCostUsd === undefined) {
    if (params.status === 'blocked') {
      // Lời gọi bị chặn ở gateway trước khi chạm tới provider không phát sinh chi phí.
      resolvedCostUsd = 0;
    } else if (params.usage && (params.usage.inputTokens > 0 || params.usage.outputTokens > 0)) {
      // Bảo toàn chi phí nếu đã nhận token phản hồi, kể cả khi quá trình parse sau đó thất bại.
      resolvedCostUsd = calculateTokenCostUsd(params.model, params.usage);
    } else {
      // Trường hợp lỗi mạng đầu vào chưa nhận được bất kỳ token nào từ provider.
      resolvedCostUsd = 0;
    }
  }

  const inputTokens = params.usage?.inputTokens ?? 0;
  const outputTokens = params.usage?.outputTokens ?? 0;
  const cachedTokens = params.usage?.cachedTokens ?? 0;
  const sanitizedPromptVersion =
    params.promptVersion && params.promptVersion.trim() ? params.promptVersion.trim() : null;

  try {
    // Chờ ghi dữ liệu để bảo đảm tính bền vững (persistence semantics) cho việc tính toán ngân sách.
    await store.aiUsageLog.create({
      data: {
        feature: params.feature,
        provider: params.provider,
        model: params.model,
        promptVersion: sanitizedPromptVersion,
        inputTokens,
        outputTokens,
        cachedTokens,
        costUsd: resolvedCostUsd,
        latencyMs: Math.max(0, Math.round(params.latencyMs)),
        status: params.status,
        errorCode: params.errorCode ?? null,
        actorKeyHash,
        createdAt: params.createdAt ?? new Date(),
      },
    });
  } catch {
    // Chỉ ghi thông điệp lỗi an toàn để tránh lộ chuỗi kết nối DB hoặc tham số nội bộ ra hệ thống log.
    logger.error(
      {
        feature: params.feature,
        provider: params.provider,
        status: params.status,
        errorCode: 'DB_PERSIST_ERROR',
      },
      'Không thể lưu ai_usage_log xuống cơ sở dữ liệu',
    );
    // Lan truyền lỗi để các caller biết rằng việc ghi nhật ký đã thất bại.
    throw new Error('Không thể lưu nhật ký sử dụng AI.');
  }
}
