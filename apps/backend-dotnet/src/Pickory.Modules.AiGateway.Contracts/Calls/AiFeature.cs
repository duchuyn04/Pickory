namespace Pickory.Modules.AiGateway.Contracts.Calls;

// Tính năng gọi AI. Gateway dùng giá trị này để ghi chi phí và áp hạn mức.
public enum AiFeature
{
    SmartSearchParseQuery,
    SmartSearchIndex,
    RecommendationsRerank,
    PersonalizedContentReviewSummary,
    PersonalizedContentDescription,
    PersonalizedContentBanner,
    ShoppingAssistantChat,
    ShoppingAssistantSummarize,
    KnowledgeBaseIndex,
    KnowledgeBaseQuery,
    SupportDeskDraftReply,
}
