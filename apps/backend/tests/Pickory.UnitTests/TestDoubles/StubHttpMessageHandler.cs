using System.Net;
using System.Text;

namespace Pickory.UnitTests.TestDoubles;

// Handler HTTP giả. Test xếp sẵn phản hồi và đọc lại request mà adapter đã gửi.
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<RecordedHttpRequest> Requests { get; } = [];

    public void EnqueueJson(HttpStatusCode status, string json)
    {
        _responses.Enqueue(() => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    // Mỗi dòng là một sự kiện SSE. Handler thêm dòng trống giữa các sự kiện, giống server thật.
    public void EnqueueEventStream(params string[] dataLines)
    {
        var body = string.Concat(dataLines.Select(line => $"data: {line}\n\n"));
        _responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        });
    }

    public void EnqueueException(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedHttpRequest(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("Test chưa xếp phản hồi HTTP.");
        }

        return _responses.Dequeue()();
    }
}
