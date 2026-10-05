// Cổng giao tiếp chính thức (public interface) của module ai-gateway.
// Các module khác trong hệ thống chỉ import từ file này theo quy ước kiến trúc.

export type {
  AiFeature,
  AiSettings,
  CallContext,
  ChatChunk,
  ChatMessage,
  ChatProvider,
  ChatRequest,
  ChatResult,
  DateRange,
  EmbeddingProvider,
  ProviderChatRequest,
  TokenUsage,
  ToolCall,
  ToolDefinition,
  UsageGroup,
  UsageSummary,
  AiGateway,
} from './types';

export {
  AiBudgetExceededError,
  AiInvalidOutputError,
  AiUnavailableError,
  ProviderError,
} from './errors';

export { aiGateway, createAiGateway } from './service';
export type { CreateAiGatewayOptions } from './service';

export { getAiSettings, updateAiSettings } from './settings';
export { aiRouter } from './routes';
export { getUsageSummary } from './usage-summary';
export { FakeChatProvider, FakeEmbeddingProvider } from './fake-provider';

// Danh mục quyền hạn quản trị dành riêng cho module ai-gateway.
export const aiPermissions = ['ai-usage:read', 'ai-settings:manage'] as const;
