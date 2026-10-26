using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.SupportDesk.Conversations;

// Mở hội thoại, đọc lịch sử và nhận tin của khách. Mọi hàm giả định route đã chặn nhân viên.
internal sealed class SupportConversationService(
    SupportDeskDbContext database,
    TimeProvider timeProvider,
    SupportMessageLimiters limiters)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    private const string ConversationNotFoundMessage = "Không tìm thấy hội thoại.";
    private const string ConversationClosedMessage = "Hội thoại này đã đóng. Hãy mở hội thoại mới để tiếp tục.";

    // Trả hội thoại `open` của người gọi, hoặc tạo mới. `Created` cho route biết chọn 201 hay 200.
    public async Task<(SupportConversation Conversation, bool Created)> OpenAsync(
        Actor actor, CancellationToken cancellationToken)
    {
        var existing = await FindOpenAsync(actor, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var conversation = new SupportConversation
        {
            AnonymousId = actor.AnonymousId,
            // Khách vãng lai không có tài khoản nên giá trị này là null. Handler đăng nhập gắn tài khoản sau.
            CustomerUserId = actor.UserId,
            Status = SupportConversationStatus.Open,
            CreatedAt = now,
            LastMessageAt = now,
        };
        database.Conversations.Add(conversation);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return (conversation, true);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Request khác vừa tạo hội thoại `open` cho cùng chủ. Bỏ bản chưa lưu rồi trả hội thoại đã có.
            database.Entry(conversation).State = EntityState.Detached;
            var winner = await FindOpenAsync(actor, cancellationToken);
            if (winner is null)
            {
                // Hội thoại thắng cuộc có thể đã đóng ngay sau đó. Khi đó không còn gì để trả, nên giữ lỗi gốc.
                throw;
            }

            return (winner, false);
        }
    }

    public async Task<MessagePageResponse> ListMessagesAsync(
        Actor actor, string conversationId, string? cursor, string? limitText, CancellationToken cancellationToken)
    {
        // Kiểm tham số trước khi chạm DB. Tham số sai trả 400 dù hội thoại có tồn tại hay không.
        var limit = ReadLimit(limitText);
        DateTime? cursorCreatedAt = null;
        string? cursorId = null;
        if (cursor is not null)
        {
            if (!SupportMessageCursor.TryDecode(cursor, out var decodedAt, out var decodedId))
            {
                throw new ValidationException([new ValidationFailure("cursor", "Cursor không hợp lệ.")]);
            }

            cursorCreatedAt = decodedAt;
            cursorId = decodedId;
        }

        // Một truy vấn có điều kiện chủ. Người khác và `id` không tồn tại cùng nhận 404, nên không dò được `id`.
        var isOwner = await ConversationOwnership.OwnedBy(database.Conversations.AsNoTracking(), actor)
            .AnyAsync(conversation => conversation.Id == conversationId, cancellationToken);
        if (!isOwner)
        {
            throw ConversationNotFound();
        }

        // Hội thoại đã đóng vẫn đọc được lịch sử, nên không lọc theo trạng thái.
        var query = database.Messages.AsNoTracking().Where(message => message.ConversationId == conversationId);
        if (cursorCreatedAt is not null)
        {
            var before = cursorCreatedAt.Value;
            // Lấy tin cũ hơn con trỏ. So sánh chặt `<` để tin cuối trang trước không lặp lại ở trang sau.
            query = query.Where(message =>
                message.CreatedAt < before || (message.CreatedAt == before && message.Id.CompareTo(cursorId) < 0));
        }

        // Lấy từ mới nhất về cũ nhất, thêm một dòng để biết còn trang cũ hơn hay không. `Id` phá thế hòa khi hai tin cùng giờ.
        var newestFirst = await query
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasOlderPage = newestFirst.Count > limit;
        var page = newestFirst.Take(limit).ToList();
        // Con trỏ trỏ vào tin cũ nhất của trang này. Không còn tin cũ hơn thì không có con trỏ.
        var nextCursor = hasOlderPage ? SupportMessageCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        // Trong trang, tin xếp tăng dần để khung chat hiện tin mới nhất ở cuối.
        page.Reverse();
        return new MessagePageResponse(page.Select(MessageResponse.From).ToList(), nextCursor);
    }

    public async Task<MessageResponse> SendMessageAsync(
        Actor actor, string conversationId, string trimmedBody, CancellationToken cancellationToken)
    {
        // Nạp bản có theo dõi, vì bước cuối cập nhật `LastMessageAt` cùng lần lưu với tin.
        var conversation = await ConversationOwnership.OwnedBy(database.Conversations, actor)
            .SingleOrDefaultAsync(value => value.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            throw ConversationNotFound();
        }

        // Hội thoại đã đóng không nhận tin. Khách phải mở hội thoại mới.
        if (conversation.Status == SupportConversationStatus.Closed)
        {
            throw new AppException(SupportDeskErrorCodes.ConversationClosed, 409, ConversationClosedMessage);
        }

        // Tin bị 400, 404 hoặc 409 ở trên không tính lượt. Chỉ tin sắp được lưu mới ghi lượt vào giới hạn.
        if (!limiters.TryAcquire(actor.AnonymousId))
        {
            throw new AppException(SupportDeskErrorCodes.MessageRateLimited, 429, SupportMessageLimiters.RateLimitedMessage);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var message = new SupportMessage
        {
            ConversationId = conversation.Id,
            Role = SupportMessageRole.Customer,
            Body = trimmedBody,
            CreatedAt = now,
        };
        database.Messages.Add(message);
        // Tin và `LastMessageAt` lưu trong một lần. Job đóng hội thoại im lặng không thấy trạng thái nửa vời.
        conversation.LastMessageAt = now;
        await database.SaveChangesAsync(cancellationToken);

        return MessageResponse.From(message);
    }

    // Hội thoại `open` của người gọi theo luật chủ. Chỉ mục unique bảo đảm tối đa một dòng.
    private Task<SupportConversation?> FindOpenAsync(Actor actor, CancellationToken cancellationToken)
    {
        return ConversationOwnership.OwnedBy(database.Conversations, actor)
            .SingleOrDefaultAsync(conversation => conversation.Status == SupportConversationStatus.Open, cancellationToken);
    }

    private static int ReadLimit(string? limitText)
    {
        // Không gửi `limit` thì dùng mặc định.
        if (string.IsNullOrEmpty(limitText))
        {
            return DefaultPageSize;
        }

        // Giá trị không phải số nguyên, hoặc ngoài khoảng 1 đến 100, là lỗi của client.
        if (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
            || limit < 1 || limit > MaxPageSize)
        {
            throw new ValidationException([new ValidationFailure("limit", $"`limit` phải là số nguyên từ 1 đến {MaxPageSize}.")]);
        }

        return limit;
    }

    private static AppException ConversationNotFound() =>
        new(SupportDeskErrorCodes.ConversationNotFound, 404, ConversationNotFoundMessage);
}
