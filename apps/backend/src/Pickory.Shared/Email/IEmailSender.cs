namespace Pickory.Shared.Email;

// Code nghiệp vụ chỉ biết giao diện này. Đổi sang dịch vụ gửi thư thật không phải sửa chỗ gọi.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
