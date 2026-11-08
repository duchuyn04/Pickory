using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Staff;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Sessions;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.SupportDesk.Conversations;

// Dịch vụ quản lý hội thoại, gửi nhận tin nhắn và cung cấp lịch sử tin.
internal sealed class SupportConversationService(
    SupportDeskDbContext database,
    SupportSessionOpener sessionOpener,
    TimeProvider timeProvider,
    SupportMessageLimiters limiters,
    IEventDispatcher dispatcher,
    IStaffDirectory staffDirectory)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    private const string ConversationNotFoundMessage = "Không tìm thấy hội thoại.";
    private const string ConversationClosedMessage = "Hội thoại này đã đóng. Hãy mở hội thoại mới để tiếp tục.";

    // Trả hội thoại `open` của người gọi và phiên gần nhất của hội thoại, hoặc tạo mới. `Created` cho route biết chọn 201 hay 200.
    public async Task<(SupportConversation Conversation, SupportSession? LatestSession, bool Created)> OpenAsync(
        Actor actor, CancellationToken cancellationToken)
    {
        var existing = await FindOpenAsync(actor, cancellationToken);
        if (existing is not null)
        {
            // Hội thoại đang mở có thể đã có phiên từ lần gửi tin trước. Đọc phiên gần nhất để trả trạng thái.
            return (existing, await FindLatestSessionAsync(existing.Id, cancellationToken), false);
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
            // Hội thoại vừa tạo chưa có phiên nào, nên không cần đọc bảng phiên.
            return (conversation, null, true);
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

            return (winner, await FindLatestSessionAsync(winner.Id, cancellationToken), false);
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

        // Kiểm chủ trước khi đọc tin. Người khác và `id` không tồn tại cùng nhận 404, nên không dò được `id`.
        await EnsureOwnerAsync(actor, conversationId, cancellationToken);

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

        var staffIds = page
            .Where(message => message.Role == SupportMessageRole.Staff && !string.IsNullOrEmpty(message.StaffId))
            .Select(message => message.StaffId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var staffNames = staffIds.Count > 0
            ? await staffDirectory.GetProfileNamesAsync(staffIds, cancellationToken)
            : new Dictionary<string, string?>();

        var items = page.Select(message =>
        {
            string? staffDisplayName = null;
            if (message.Role == SupportMessageRole.Staff && message.StaffId is not null
                && staffNames.TryGetValue(message.StaffId, out var name)
                && !string.IsNullOrWhiteSpace(name))
            {
                staffDisplayName = name;
            }
            return MessageResponse.From(message, staffDisplayName);
        }).ToList();

        return new MessagePageResponse(items, nextCursor);
    }

    public async Task<MessageResponse> SendMessageAsync(
        Actor actor, string conversationId, string trimmedBody, CancellationToken cancellationToken)
    {
        // Nạp bản có theo dõi, vì bước cuối cập nhật `LastMessageAt` trong cùng transaction với tin.
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

        var nowMoment = timeProvider.GetUtcNow();
        var now = nowMoment.UtcDateTime;
        var message = new SupportMessage
        {
            ConversationId = conversation.Id,
            Role = SupportMessageRole.Customer,
            Body = trimmedBody,
            CreatedAt = now,
        };
        // Sự kiện gom trong lúc transaction chạy và chỉ phát sau commit. Transaction hủy không phát phiên mới cho ai.
        var events = new DomainEventCollector(dispatcher);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Phiên chờ mở trong cùng transaction với tin của khách, nên khách không thấy tin mà thiếu người xử lý.
        var openedSession = await sessionOpener.OpenIfMissingAsync(conversation, nowMoment, events, cancellationToken);
        var activeSession = openedSession ?? await database.Sessions
            .SingleOrDefaultAsync(session => session.ConversationId == conversation.Id && session.ClosedAt == null, cancellationToken);

        // Ghi mã phiên vào tin nhắn của khách nếu đang trong một phiên.
        message.SessionId = activeSession?.Id;
        database.Messages.Add(message);
        await database.SaveChangesAsync(cancellationToken);

        // Phát sự kiện tin mới tới đúng nhân viên đang giữ phiên nếu đã có người nhận.
        if (activeSession?.StaffId is not null)
        {
            events.Emit(new SupportCustomerMessageDispatchedEvent(
                activeSession.Id, conversation.Id, activeSession.StaffId));
        }

        // Cập nhật `lastMessageAt` sau bước kiểm phiên. Hai tin gửi cùng lúc không phải chờ khóa dòng hội thoại trước khi kiểm phiên.
        conversation.LastMessageAt = now;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Phát sau commit. Handler đọc DB bằng kết nối khác vẫn thấy phiên và tin hệ thống mới.
        await events.FlushAsync(cancellationToken);

        return MessageResponse.From(message);
    }

    // Đọc lịch sử tin nhắn của hội thoại cho nhân viên theo phân trang con trỏ.
    public async Task<StaffMessagePageResponse> ListStaffMessagesAsync(
        string conversationId, string? cursor, string? limitText, CancellationToken cancellationToken)
    {
        // Kiểm tham số trước khi truy vấn DB.
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

        // Kiểm tra hội thoại tồn tại trước khi lấy lịch sử tin nhắn.
        var conversationExists = await database.Conversations.AsNoTracking()
            .AnyAsync(conversation => conversation.Id == conversationId, cancellationToken);
        if (!conversationExists)
        {
            throw ConversationNotFound();
        }

        var query = database.Messages.AsNoTracking().Where(message => message.ConversationId == conversationId);
        if (cursorCreatedAt is not null)
        {
            var before = cursorCreatedAt.Value;
            query = query.Where(message =>
                message.CreatedAt < before || (message.CreatedAt == before && message.Id.CompareTo(cursorId) < 0));
        }

        var newestFirst = await query
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasOlderPage = newestFirst.Count > limit;
        var page = newestFirst.Take(limit).ToList();
        var nextCursor = hasOlderPage ? SupportMessageCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        // Trong trang, tin xếp tăng dần theo thời gian tạo.
        page.Reverse();

        // Đọc danh sách tên nhân viên để hiển thị trong lịch sử tin.
        var staffIds = page
            .Where(message => message.Role == SupportMessageRole.Staff && !string.IsNullOrEmpty(message.StaffId))
            .Select(message => message.StaffId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var staffNames = staffIds.Count > 0
            ? await staffDirectory.GetProfileNamesAsync(staffIds, cancellationToken)
            : new Dictionary<string, string?>();

        var items = page.Select(message =>
        {
            string? staffDisplayName = null;
            if (message.Role == SupportMessageRole.Staff && message.StaffId is not null
                && staffNames.TryGetValue(message.StaffId, out var name)
                && !string.IsNullOrWhiteSpace(name))
            {
                staffDisplayName = name;
            }

            return StaffViewMessageResponse.From(message, staffDisplayName);
        }).ToList();

        return new StaffMessagePageResponse(items, nextCursor);
    }

    // Kiểm chủ hội thoại cho route đọc. Hội thoại của người khác và `id` không tồn tại cùng nhận 404.
    public async Task EnsureOwnerAsync(Actor actor, string conversationId, CancellationToken cancellationToken)
    {
        // Một truy vấn có điều kiện chủ, nên người khác không dò được hội thoại nào tồn tại.
        var isOwner = await ConversationOwnership.OwnedBy(database.Conversations.AsNoTracking(), actor)
            .AnyAsync(conversation => conversation.Id == conversationId, cancellationToken);
        if (!isOwner)
        {
            throw ConversationNotFound();
        }
    }

    // Hội thoại `open` của người gọi theo luật chủ. Chỉ mục unique bảo đảm tối đa một dòng.
    private Task<SupportConversation?> FindOpenAsync(Actor actor, CancellationToken cancellationToken)
    {
        return ConversationOwnership.OwnedBy(database.Conversations, actor)
            .SingleOrDefaultAsync(conversation => conversation.Status == SupportConversationStatus.Open, cancellationToken);
    }

    // Phiên gần nhất của hội thoại. Hội thoại có nhiều phiên sau nhiều lượt cần nhân viên, nên chọn theo lúc mở phiên.
    private Task<SupportSession?> FindLatestSessionAsync(string conversationId, CancellationToken cancellationToken)
    {
        return database.Sessions.AsNoTracking()
            .Where(session => session.ConversationId == conversationId)
            // Hai phiên mở cùng mili giây vẫn có thứ tự ổn định nhờ mã phiên phá thế hòa.
            .OrderByDescending(session => session.HandoffAt)
            .ThenByDescending(session => session.Id)
            .FirstOrDefaultAsync(cancellationToken);
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
