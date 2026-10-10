using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Pickory.IntegrationTests.Shipping;

// Handler HTTP giả chọn phản hồi theo đường dẫn, vì báo phí và ngày giao chạy song song.
// Hàng đợi chung sẽ phát nhầm phản hồi, nên mỗi đường dẫn có hàng đợi riêng.
internal sealed class RoutedHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<HttpResponseMessage>>> _queues = new();
    private readonly ConcurrentDictionary<string, int> _requestCounts = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _bodies = new();

    public void EnqueueJson(string path, HttpStatusCode status, string json)
    {
        Enqueue(path, () => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    public void EnqueueException(string path, Exception exception)
    {
        Enqueue(path, () => throw exception);
    }

    // Phản hồi không bao giờ về. Chỉ hủy token mới dừng được lời gọi, nên test không phụ thuộc đồng hồ.
    public void EnqueueHang(string path)
    {
        Enqueue(path, () => throw new HangMarkerException());
    }

    // Số request đã tới một đường dẫn. Đường dẫn so khớp theo phần cuối, ví dụ `/fee`.
    public int RequestCount(string path) => _requestCounts.GetValueOrDefault(path);

    // Thân JSON của các request đã tới một đường dẫn, theo thứ tự tới.
    public IReadOnlyList<string> Bodies(string path) =>
        _bodies.TryGetValue(path, out var bodies) ? bodies.ToList() : [];

    private void Enqueue(string path, Func<HttpResponseMessage> response)
    {
        _queues.GetOrAdd(path, _ => new ConcurrentQueue<Func<HttpResponseMessage>>()).Enqueue(response);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = FindRegisteredPath(request.RequestUri!.AbsolutePath);
        _requestCounts.AddOrUpdate(path, 1, (_, current) => current + 1);
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            _bodies.GetOrAdd(path, _ => new ConcurrentQueue<string>()).Enqueue(body);
        }

        if (!_queues.TryGetValue(path, out var queue) || !queue.TryDequeue(out var response))
        {
            throw new InvalidOperationException($"Test chưa xếp phản hồi HTTP cho đường dẫn {path}.");
        }

        try
        {
            return response();
        }
        catch (HangMarkerException)
        {
            // Chờ vô hạn tới khi bên gọi hủy. Adapter phải tự hết giờ, nên token luôn bị hủy.
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw;
        }
    }

    // Trả về đường dẫn đã đăng ký mà `absolutePath` kết thúc bằng nó. Đường lạ giữ nguyên để lỗi nêu rõ.
    private string FindRegisteredPath(string absolutePath)
    {
        var registered = _queues.Keys.FirstOrDefault(path => absolutePath.EndsWith(path, StringComparison.Ordinal));
        return registered ?? absolutePath;
    }

    private sealed class HangMarkerException : Exception;
}
