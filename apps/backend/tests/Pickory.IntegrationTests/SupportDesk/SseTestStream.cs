using System.Text.Json;

namespace Pickory.IntegrationTests.SupportDesk;

// Một sự kiện SSE đã đọc từ luồng: tên sự kiện và dữ liệu JSON.
internal sealed record ReceivedSseEvent(string Name, JsonElement Data);

// Đọc luồng SSE trong test. Mỗi lần đọc một sự kiện có hạn chờ thật riêng, nên thiếu sự kiện làm test đỏ chứ không treo.
internal sealed class SseTestStream : IDisposable
{
    // Hạn chờ cho thao tác không cần đồng hồ giả. Đủ lớn cho máy chậm, đủ nhỏ để test thiếu sự kiện đỏ nhanh.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;

    private SseTestStream(HttpResponseMessage response, StreamReader reader)
    {
        _response = response;
        _reader = reader;
    }

    public HttpResponseMessage Response => _response;

    // Mở luồng bằng `ResponseHeadersRead`. Nếu đọc hết body, request SSE không bao giờ kết thúc và test treo.
    public static async Task<SseTestStream> OpenAsync(HttpClient client, string path, string? cookie)
    {
        // Hạn chờ này chỉ áp cho việc mở luồng. Mỗi lần đọc sự kiện sau đó có hạn chờ riêng.
        using var openTimeout = new CancellationTokenSource(DefaultTimeout);
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, openTimeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return new SseTestStream(response, StreamReader.Null);
        }

        var stream = await response.Content.ReadAsStreamAsync(openTimeout.Token);
        return new SseTestStream(response, new StreamReader(stream));
    }

    // Đọc sự kiện kế tiếp. Dòng bắt đầu bằng `:` là chú thích của SSE nên bỏ qua.
    public async Task<ReceivedSseEvent> ReadEventAsync()
    {
        // Token riêng cho mỗi sự kiện. Chuỗi nhiều lần đọc chậm trên máy bận không dồn vào một hạn chờ chung.
        using var readTimeout = new CancellationTokenSource(DefaultTimeout);
        var parser = new SseEventParser();
        while (true)
        {
            var line = await _reader.ReadLineAsync(readTimeout.Token)
                ?? throw new InvalidOperationException("Luồng SSE đã đóng trước khi đủ một sự kiện.");

            if (parser.TryCompleteEvent(line) is { } received)
            {
                return received;
            }
        }
    }

    // Đọc cho tới khi gặp sự kiện có tên mong muốn. Sự kiện khác (ví dụ nhịp tim) bị bỏ qua.
    public async Task<ReceivedSseEvent> ReadEventNamedAsync(string expectedName)
    {
        while (true)
        {
            var received = await ReadEventAsync();
            if (received.Name == expectedName)
            {
                return received;
            }
        }
    }

    // Đọc tới khi máy chủ đóng luồng, rồi trả các sự kiện đã đọc. Hết hạn chờ trước khi luồng đóng thì ném lỗi.
    public async Task<IReadOnlyList<ReceivedSseEvent>> ReadUntilStreamEndsAsync()
    {
        var received = new List<ReceivedSseEvent>();
        using var readTimeout = new CancellationTokenSource(DefaultTimeout);
        var parser = new SseEventParser();
        while (await _reader.ReadLineAsync(readTimeout.Token) is { } line)
        {
            if (parser.TryCompleteEvent(line) is { } completed)
            {
                received.Add(completed);
            }
        }

        return received;
    }

    public void Dispose()
    {
        _reader.Dispose();
        _response.Dispose();
    }

    // Ghép từng dòng của luồng thành sự kiện. Hai hàm đọc dùng chung, nên định dạng `event:` và `data:` chỉ tách ở một chỗ.
    private sealed class SseEventParser
    {
        private string? _name;
        private readonly List<string> _dataLines = [];

        // Trả sự kiện khi gặp dòng trống kết thúc một sự kiện, còn lại trả `null`.
        public ReceivedSseEvent? TryCompleteEvent(string line)
        {
            if (line.Length == 0)
            {
                // Dòng trống thừa khi chưa có nội dung thì bỏ qua.
                if (_name is null && _dataLines.Count == 0)
                {
                    return null;
                }

                var completed = new ReceivedSseEvent(
                    _name ?? "message", JsonDocument.Parse(string.Join('\n', _dataLines)).RootElement.Clone());
                _name = null;
                _dataLines.Clear();
                return completed;
            }

            // Dòng bắt đầu bằng `:` là chú thích của SSE, không thuộc sự kiện nào.
            if (line.StartsWith(':'))
            {
                return null;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                _name = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                _dataLines.Add(line["data:".Length..].TrimStart());
            }

            return null;
        }
    }
}
