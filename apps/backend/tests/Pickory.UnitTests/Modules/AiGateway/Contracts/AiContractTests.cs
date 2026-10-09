using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Contracts;

public sealed class AiContractTests
{
    [Fact]
    // Mã tính năng nằm trong `ai_usage_logs`. Mã phải giống bản Node để báo cáo cũ và mới gộp được.
    public void AiFeatureCodes_MatchNodeFeatureIds()
    {
        string[] nodeFeatureIds =
        [
            "smart-search.parse-query",
            "smart-search.index",
            "recommendations.rerank",
            "personalized-content.review-summary",
            "personalized-content.description",
            "personalized-content.banner",
            "shopping-assistant.chat",
            "shopping-assistant.summarize",
            "knowledge-base.index",
            "knowledge-base.query",
            "support-desk.draft-reply",
        ];

        var codes = Enum.GetValues<AiFeature>().Select(AiFeatureCodes.ToCode).ToArray();

        Assert.Equal(nodeFeatureIds, codes);
    }

    [Fact]
    // Module gọi chọn phương án dự phòng theo mã lỗi ổn định và mã HTTP, giống bản Node.
    public void AiErrors_UseNodeCodesAndStatuses()
    {
        var unavailable = new AiUnavailableException("CIRCUIT_OPEN");
        var budget = new AiBudgetExceededException();
        var invalidOutput = new AiInvalidOutputException();

        Assert.Equal(("AI_UNAVAILABLE", 503, "CIRCUIT_OPEN"), (unavailable.Code, unavailable.StatusCode, unavailable.ReasonCode));
        Assert.Equal(("AI_BUDGET_EXCEEDED", 429), (budget.Code, budget.StatusCode));
        Assert.Equal(("AI_INVALID_OUTPUT", 502), (invalidOutput.Code, invalidOutput.StatusCode));
    }

    [Fact]
    // Lỗi không truyền mã lý do thì dùng mã chung, để log luôn có mã lỗi.
    public void AiUnavailableException_DefaultsReasonCode()
    {
        Assert.Equal("AI_UNAVAILABLE", new AiUnavailableException().ReasonCode);
    }
}
