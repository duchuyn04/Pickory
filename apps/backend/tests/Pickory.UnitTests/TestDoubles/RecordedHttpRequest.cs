namespace Pickory.UnitTests.TestDoubles;

// `Method` và `Headers` có giá trị mặc định để test cũ vẫn dựng được bản ghi theo ba tham số đầu.
public sealed record RecordedHttpRequest(
    string Url,
    string? Authorization,
    string Body,
    string Method = "",
    IReadOnlyDictionary<string, string>? Headers = null);
