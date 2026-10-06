namespace Pickory.Modules.AiGateway.Contracts.Calls;

// Mã chuỗi của từng tính năng, giống bản Node. Cột `feature` của `ai_usage_logs` lưu mã này.
public static class AiFeatureCodes
{
    public static string ToCode(AiFeature feature)
    {
        return feature switch
        {
            AiFeature.SmartSearchParseQuery => "smart-search.parse-query",
            AiFeature.SmartSearchIndex => "smart-search.index",
            AiFeature.RecommendationsRerank => "recommendations.rerank",
            AiFeature.PersonalizedContentReviewSummary => "personalized-content.review-summary",
            AiFeature.PersonalizedContentDescription => "personalized-content.description",
            AiFeature.PersonalizedContentBanner => "personalized-content.banner",
            AiFeature.ShoppingAssistantChat => "shopping-assistant.chat",
            AiFeature.ShoppingAssistantSummarize => "shopping-assistant.summarize",
            AiFeature.KnowledgeBaseIndex => "knowledge-base.index",
            AiFeature.KnowledgeBaseQuery => "knowledge-base.query",
            AiFeature.SupportDeskDraftReply => "support-desk.draft-reply",
            // Giá trị ép kiểu từ số lạ không có mã. Ghi log với mã sai sẽ làm báo cáo chi phí sai.
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Tính năng AI không có mã."),
        };
    }
}
