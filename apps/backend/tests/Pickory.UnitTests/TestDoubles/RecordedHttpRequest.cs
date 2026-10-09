namespace Pickory.UnitTests.TestDoubles;

public sealed record RecordedHttpRequest(string Url, string? Authorization, string Body);
