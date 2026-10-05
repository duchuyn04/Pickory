import { createHash } from 'node:crypto';
import type { PrismaClient } from '../../generated/prisma/client';
import { redactPII } from '../../shared/privacy';
import { assertWithinBudget } from './budget';
import { AiBudgetExceededError, AiUnavailableError, getAiErrorCode } from './errors';
import type { Reliability } from './reliability';
import type { AiSettings, CallContext, EmbeddingProvider, TokenUsage } from './types';
import { persistUsageLog, type UsageStatus } from './usage-log';

type PendingEmbedding = {
  key: string;
  text: string;
  promise: Promise<number[]>;
  resolve: (vector: number[]) => void;
  reject: (error: unknown) => void;
};
// Mỗi store có một nhóm single-flight riêng, không chia sẻ vector giữa tenant hoặc test DB.
const inFlightPerStore = new WeakMap<PrismaClient, Map<string, Promise<number[]>>>();

export function computeEmbeddingCacheKey(model: string, redactedText: string): string {
  // Ranh giới null ngăn hai cặp model/text khác nhau ghép thành cùng chuỗi trước khi hash.
  return createHash('sha256').update(`${model}\0${redactedText}`).digest('hex');
}

export async function executeEmbedWithCache(params: {
  provider: EmbeddingProvider;
  reliability: Reliability;
  store: PrismaClient;
  settings: AiSettings;
  texts: string[];
  ctx: CallContext;
  now?: () => number;
}): Promise<number[][]> {
  const currentNow = params.now ?? Date.now;
  const startTime = currentNow();
  let inFlight = inFlightPerStore.get(params.store);
  if (!inFlight) {
    inFlight = new Map();
    inFlightPerStore.set(params.store, inFlight);
  }
  const textByKey = new Map<string, string>();
  const cacheKeys = params.texts.map((rawText) => {
    // Che PII trước cả hash và provider để không lưu khóa suy ra từ dữ liệu định danh thô.
    const text = redactPII(rawText);
    const key = computeEmbeddingCacheKey(params.provider.model, text);
    textByKey.set(key, text);
    return key;
  });
  const promisesByKey = new Map<string, Promise<number[]>>();
  const owned: PendingEmbedding[] = [];
  for (const [key, text] of textByKey) {
    const existing = inFlight.get(key);
    if (existing) {
      promisesByKey.set(key, existing);
      continue;
    }
    const pending = Promise.withResolvers<number[]>();
    // Đăng ký trước lần await đầu tiên, tránh snapshot cache cũ tạo request trùng sau khi lời gọi khác vừa hoàn tất.
    inFlight.set(key, pending.promise);
    promisesByKey.set(key, pending.promise);
    pending.promise.catch(() => undefined);
    owned.push({ key, text, ...pending });
  }

  let usage: TokenUsage = { inputTokens: 0, outputTokens: 0, cachedTokens: 0 };
  let status: UsageStatus = 'ok';
  let errorCode: string | undefined;
  try {
    if (owned.length > 0) {
      let cachedRows: { key: string; embedding: number[] }[];
      try {
        cachedRows = await params.store.aiEmbeddingCache.findMany({
          where: { key: { in: owned.map((item) => item.key) } },
          select: { key: true, embedding: true },
        });
      } catch {
        throw new AiUnavailableError('EMBEDDING_CACHE_READ_ERROR');
      }
      const cachedByKey = new Map(cachedRows.map((row) => [row.key, row.embedding]));
      const missing: PendingEmbedding[] = [];
      for (const item of owned) {
        const cached = cachedByKey.get(item.key);
        if (cached) item.resolve(cached);
        else missing.push(item);
      }
      if (missing.length > 0) {
        // Hit cache hoặc chờ request đã có không phát sinh phí nên không bị chặn bởi ngân sách mới.
        await assertWithinBudget({
          feature: params.ctx.feature,
          monthlyBudgetUsd: params.settings.monthlyBudgetUsd,
          store: params.store,
          now: currentNow,
        });
        const result = await params.reliability.run(params.provider.name, 15000, (signal) =>
          params.provider.embed(
            missing.map((item) => item.text),
            signal,
          ),
        );
        usage = result.usage;
        if (result.vectors.length !== missing.length) {
          throw new AiUnavailableError('EMBEDDING_VECTOR_COUNT_ERROR');
        }
        for (let index = 0; index < missing.length; index += 1) {
          const item = missing[index];
          const vector = result.vectors[index];
          if (!item || !vector) throw new AiUnavailableError('EMBEDDING_VECTOR_COUNT_ERROR');
          try {
            await params.store.aiEmbeddingCache.upsert({
              where: { key: item.key },
              create: { key: item.key, model: params.provider.model, embedding: vector },
              update: { embedding: vector },
            });
          } catch {
            throw new AiUnavailableError('EMBEDDING_CACHE_WRITE_ERROR');
          }
          // Chỉ công bố vector sau khi lưu bền vững, caller khác không nhận kết quả từ một lần ghi DB thất bại.
          item.resolve(vector);
        }
      }
    }
    // Mọi key đều đã được đăng ký đồng bộ ở trên, giữ cả thứ tự và số lần trùng của input.
    return await Promise.all(cacheKeys.map((key) => promisesByKey.get(key)!));
  } catch (error) {
    status = error instanceof AiBudgetExceededError ? 'blocked' : 'error';
    errorCode = getAiErrorCode(error);
    for (const item of owned) item.reject(error);
    throw error;
  } finally {
    for (const item of owned) inFlight.delete(item.key);
    // Một log duy nhất bao phủ cache hit, mảng rỗng, single-flight, lỗi DB, lỗi provider và ngân sách.
    await persistUsageLog(params.store, {
      feature: params.ctx.feature,
      provider: params.provider.name,
      model: params.provider.model,
      promptVersion: params.ctx.promptVersion,
      actorKey: params.ctx.actorKey,
      usage,
      status,
      errorCode,
      latencyMs: currentNow() - startTime,
    });
  }
}
