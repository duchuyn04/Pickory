import type { ZodType } from 'zod';

export type AiFeature =
  | 'smart-search.parse-query'
  | 'smart-search.index'
  | 'recommendations.rerank'
  | 'personalized-content.review-summary'
  | 'personalized-content.description'
  | 'personalized-content.banner'
  | 'shopping-assistant.chat'
  | 'shopping-assistant.summarize'
  | 'knowledge-base.index'
  | 'knowledge-base.query'
  | 'support-desk.draft-reply';

export type CallContext = { feature: AiFeature; actorKey?: string; promptVersion?: string };
export type ToolDefinition<P = unknown> = {
  name: string;
  description: string;
  parameters: ZodType<P>;
  // Kiểu method cho phép gom tool khác schema; gateway bắt buộc parse parameters trước execute.
  execute(params: P): Promise<unknown>;
};
export type ToolCall = { id: string; name: string; input: unknown };
export type ChatMessage =
  | { role: 'system'; content: string }
  | { role: 'user'; content: string }
  | { role: 'assistant'; content: string; toolCalls?: ToolCall[] }
  | { role: 'tool'; toolCallId: string; toolName: string; content: string };
export type ChatRequest = {
  messages: ChatMessage[];
  tools?: ToolDefinition[];
  maxSteps?: number;
  temperature?: number;
  maxTokens?: number;
};
export type TokenUsage = { inputTokens: number; outputTokens: number; cachedTokens: number };
export type ChatResult = { text: string; usage: TokenUsage; toolCalls?: ToolCall[] };
export type ChatChunk =
  { type: 'text'; text: string } | { type: 'finish'; usage: TokenUsage; toolCalls?: ToolCall[] };
export interface AiGateway {
  chat(req: ChatRequest, ctx: CallContext): Promise<ChatResult>;
  chatStream(req: ChatRequest, ctx: CallContext): AsyncIterable<ChatChunk>;
  chatJson<T>(req: ChatRequest, schema: ZodType<T>, ctx: CallContext): Promise<T>;
  embed(texts: string[], ctx: CallContext): Promise<number[][]>;
}
export type ProviderChatRequest = ChatRequest & { json?: boolean };
export interface ChatProvider {
  readonly name: string;
  readonly model: string;
  chat(req: ProviderChatRequest, signal: AbortSignal): Promise<ChatResult>;
  stream(req: ProviderChatRequest, signal: AbortSignal): AsyncIterable<ChatChunk>;
}
export interface EmbeddingProvider {
  readonly name: string;
  readonly model: string;
  embed(texts: string[], signal: AbortSignal): Promise<{ vectors: number[][]; usage: TokenUsage }>;
}
export type AiSettings = {
  monthlyBudgetUsd: number;
  chatDailyLimitUser: number;
  chatDailyLimitAnon: number;
};
export type DateRange = { from: Date; to: Date };
export type UsageGroup = { costUsd: number; calls: number };
export type UsageSummary = UsageGroup & {
  errorRate: number;
  p95LatencyMs: number;
  byDay: (UsageGroup & { day: string })[];
  byFeature: (UsageGroup & { feature: string })[];
  budgetWarning: boolean;
};
