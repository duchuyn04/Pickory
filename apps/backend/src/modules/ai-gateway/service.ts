import type { ZodType } from 'zod';
import type { PrismaClient } from '../../generated/prisma/client';
import { loadConfig, type Config } from '../../shared/config';
import { db as defaultDb } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { executeChatJson } from './chat-json';
import { executeEmbedWithCache } from './embedding-cache';
import { AiBudgetExceededError, AiUnavailableError, getAiErrorCode } from './errors';
import { FakeChatProvider, FakeEmbeddingProvider } from './fake-provider';
import { getModelPricing } from './pricing';
import { createDeepSeekProvider, createOpenAIEmbeddingProvider } from './providers';
import { ChatRateLimiter } from './rate-limit';
import { Reliability } from './reliability';
import { getAiSettings } from './settings';
import { executeChatStreamWithToolLoop, executeChatWithToolLoop } from './tool-loop';
import type {
  AiGateway,
  AiSettings,
  CallContext,
  ChatChunk,
  ChatProvider,
  ChatRequest,
  EmbeddingProvider,
} from './types';
import { persistUsageLog } from './usage-log';

function validateCallContext(ctx: CallContext): void {
  // Chat phải có định danh ổn định để không bỏ qua hạn mức ngày.
  if (ctx.feature === 'shopping-assistant.chat' && !ctx.actorKey?.trim()) {
    throw new AppError('INVALID_ACTOR_KEY', 400, 'actorKey là bắt buộc cho tính năng trò chuyện.');
  }
  if (ctx.actorKey !== undefined) {
    const key = ctx.actorKey.trim();
    if (
      (!key.startsWith('user:') && !key.startsWith('anon:')) ||
      !key.slice(key.indexOf(':') + 1).trim()
    ) {
      throw new AppError(
        'INVALID_ACTOR_KEY',
        400,
        'actorKey sai định dạng: cần tiền tố user: hoặc anon: và định danh không rỗng.',
      );
    }
  }
}

function validateChatRequest(req: ChatRequest): void {
  if (req.maxSteps !== undefined && (!Number.isInteger(req.maxSteps) || req.maxSteps <= 0)) {
    throw new AppError('INVALID_MAX_STEPS', 400, 'maxSteps phải là số nguyên dương.');
  }
}

export type CreateAiGatewayOptions = {
  chatProvider?: ChatProvider;
  embeddingProvider?: EmbeddingProvider;
  reliability?: Reliability;
  store?: PrismaClient;
  rateLimiter?: ChatRateLimiter;
  now?: () => number;
  config?: Config;
};

export function createAiGateway(options: CreateAiGatewayOptions = {}): AiGateway {
  const currentNow = options.now ?? Date.now;
  const config = options.config ?? loadConfig();
  const store = options.store ?? defaultDb;
  const reliability = options.reliability ?? new Reliability({ now: currentNow });
  const rateLimiter = options.rateLimiter ?? new ChatRateLimiter(currentNow);

  // Không dựng provider giả trên production, thiếu cấu hình phải được ghi nhận như một sự cố.
  let chatProvider = options.chatProvider;
  if (!chatProvider) {
    if (config.LLM_API_KEY.trim() && config.LLM_MODEL.trim()) {
      chatProvider = createDeepSeekProvider(config);
    } else if (config.NODE_ENV !== 'production') {
      chatProvider = new FakeChatProvider();
    }
  }
  let embeddingProvider = options.embeddingProvider;
  if (!embeddingProvider) {
    if (config.EMBEDDING_API_KEY.trim()) {
      embeddingProvider = createOpenAIEmbeddingProvider(config);
    } else if (config.NODE_ENV !== 'production') {
      embeddingProvider = new FakeEmbeddingProvider();
    }
  }

  async function prepareProvider<P extends ChatProvider | EmbeddingProvider>(
    provider: P | undefined,
    ctx: CallContext,
    request?: ChatRequest,
  ): Promise<P> {
    try {
      validateCallContext(ctx);
      if (request) validateChatRequest(request);
    } catch (error) {
      // Lỗi hợp đồng từ caller vẫn có metadata đối soát, nhưng tuyệt đối không gửi request sai tới provider.
      await persistUsageLog(store, {
        feature: ctx.feature,
        provider: provider?.name ?? 'unavailable',
        model: provider?.model ?? 'unavailable',
        promptVersion: ctx.promptVersion,
        actorKey: ctx.actorKey,
        status: 'error',
        errorCode: getAiErrorCode(error),
        latencyMs: 0,
        costUsd: 0,
      });
      throw error;
    }
    if (!provider) {
      // Thiếu khóa cũng cần một dòng log để dashboard không che giấu thời gian gián đoạn.
      await persistUsageLog(store, {
        feature: ctx.feature,
        provider: 'unavailable',
        model: 'unavailable',
        promptVersion: ctx.promptVersion,
        actorKey: ctx.actorKey,
        status: 'blocked',
        errorCode: 'AI_NOT_CONFIGURED',
        latencyMs: 0,
        costUsd: 0,
      });
      throw new AiUnavailableError();
    }
    if (getModelPricing(provider.model) === null) {
      // Model chưa có giá không được gọi ra ngoài rồi mới phát hiện chi phí không đối soát được.
      await persistUsageLog(store, {
        feature: ctx.feature,
        provider: provider.name,
        model: provider.model,
        promptVersion: ctx.promptVersion,
        actorKey: ctx.actorKey,
        status: 'error',
        errorCode: 'UNKNOWN_MODEL_PRICING',
        latencyMs: 0,
        costUsd: 0,
      });
      throw new Error(
        `Không thể sử dụng model "${provider.model}": chưa được cấu hình bảng giá an toàn.`,
      );
    }
    try {
      // Từ chối mạch mở trước khi truy vấn cấu hình hoặc tính hạn mức người dùng.
      reliability.assertAvailable(provider.name);
    } catch (error) {
      await persistUsageLog(store, {
        feature: ctx.feature,
        provider: provider.name,
        model: provider.model,
        promptVersion: ctx.promptVersion,
        actorKey: ctx.actorKey,
        status: 'blocked',
        errorCode: 'CIRCUIT_OPEN',
        latencyMs: 0,
        costUsd: 0,
      });
      throw error;
    }
    return provider;
  }

  async function prepareChat(
    ctx: CallContext,
  ): Promise<{ settings: AiSettings; turnDay?: string }> {
    const settings = await getAiSettings(store);
    let turnDay: string | undefined;
    if (ctx.feature === 'shopping-assistant.chat') {
      try {
        // Một lời gọi logic chỉ chiếm một lượt, dù JSON sửa lại hoặc tool có nhiều bước.
        turnDay = rateLimiter.checkAndIncrement(
          ctx.actorKey,
          settings.chatDailyLimitUser,
          settings.chatDailyLimitAnon,
        );
      } catch (error) {
        if (error instanceof AiBudgetExceededError) {
          await persistUsageLog(store, {
            feature: ctx.feature,
            provider: chatProvider?.name ?? 'unavailable',
            model: chatProvider?.model ?? 'unavailable',
            promptVersion: ctx.promptVersion,
            actorKey: ctx.actorKey,
            status: 'blocked',
            errorCode: 'RATE_LIMIT_EXCEEDED',
            latencyMs: 0,
            costUsd: 0,
          });
        }
        throw error;
      }
    }
    return { settings, turnDay };
  }

  function refundFailedTurn(ctx: CallContext, turnDay: string | undefined, error: unknown): void {
    // Sự cố provider hoặc chặn ngân sách không được làm mất lượt chat của người dùng.
    if (
      turnDay &&
      ctx.actorKey &&
      (error instanceof AiUnavailableError || error instanceof AiBudgetExceededError)
    ) {
      rateLimiter.refund(ctx.actorKey, turnDay);
    }
  }

  return {
    async chat(req, ctx) {
      const provider = await prepareProvider(chatProvider, ctx, req);
      const { settings, turnDay } = await prepareChat(ctx);
      try {
        return await executeChatWithToolLoop({
          provider,
          reliability,
          store,
          settings,
          req,
          ctx,
          now: currentNow,
        });
      } catch (error) {
        refundFailedTurn(ctx, turnDay, error);
        throw error;
      }
    },
    async *chatStream(req, ctx): AsyncIterable<ChatChunk> {
      const provider = await prepareProvider(chatProvider, ctx, req);
      const { settings, turnDay } = await prepareChat(ctx);
      try {
        yield* executeChatStreamWithToolLoop({
          provider,
          reliability,
          store,
          settings,
          req,
          ctx,
          now: currentNow,
        });
      } catch (error) {
        refundFailedTurn(ctx, turnDay, error);
        throw error;
      }
    },
    async chatJson<T>(req: ChatRequest, schema: ZodType<T>, ctx: CallContext): Promise<T> {
      const provider = await prepareProvider(chatProvider, ctx, req);
      const { settings, turnDay } = await prepareChat(ctx);
      try {
        return await executeChatJson({
          provider,
          reliability,
          store,
          settings,
          req,
          schema,
          ctx,
          now: currentNow,
        });
      } catch (error) {
        refundFailedTurn(ctx, turnDay, error);
        throw error;
      }
    },
    async embed(texts, ctx) {
      const provider = await prepareProvider(embeddingProvider, ctx);
      const settings = await getAiSettings(store);
      return executeEmbedWithCache({
        provider,
        reliability,
        store,
        settings,
        texts,
        ctx,
        now: currentNow,
      });
    },
  };
}

export const aiGateway: AiGateway = createAiGateway();
