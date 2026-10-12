using System.Collections.Concurrent;
using Pickory.Shared.Email;

namespace Pickory.IntegrationTests.Infrastructure;

// Giữ thư trong bộ nhớ. Test đọc lại token trong thư mà không ghi file.
public sealed class InMemoryEmailSender : IEmailSender
{
    // Dùng hàng đợi an toàn luồng. Nhiều request song song có thể gửi thư cùng lúc.
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyList<EmailMessage> Messages => _messages.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    // Lọc thư theo người nhận. Các test dùng chung một hộp thư nhưng mỗi test có email riêng.
    public IReadOnlyList<EmailMessage> MessagesTo(string email)
    {
        return Messages.Where(message => message.To == email).ToList();
    }
}
