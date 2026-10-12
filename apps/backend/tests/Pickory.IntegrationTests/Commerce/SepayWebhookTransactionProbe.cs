using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Shared.Events;

namespace Pickory.IntegrationTests.Commerce;

// Kết nối riêng chứng minh bên nhận sự kiện thấy dữ liệu đã commit, không thấy trạng thái đang theo dõi.
internal sealed class SepayPaidEventObserver(IntegrationDatabaseFixture database) : IDomainEventHandler<OrderPaidEvent>
{
    private readonly ConcurrentQueue<PaidObservation> _observations = new();
    public IReadOnlyList<PaidObservation> Observations => _observations.ToArray();

    public async Task HandleAsync(OrderPaidEvent @event, CancellationToken cancellationToken = default)
    {
        // Context mới không dùng transaction của webhook nên phát trước commit sẽ đọc trạng thái cũ.
        await using var commerce = database.CreateCommerceContext();
        var order = await commerce.Orders.AsNoTracking().SingleAsync(row => row.Id == @event.OrderId, cancellationToken);
        var historyCount = await commerce.OrderStatusHistory.CountAsync(row => row.OrderId == @event.OrderId
            && row.FromStatus == OrderStatus.PendingPayment && row.ToStatus == OrderStatus.PendingConfirmation, cancellationToken);
        var matchedCount = await commerce.PaymentTransactions.CountAsync(row => row.OrderId == @event.OrderId
            && row.MatchStatus == "matched", cancellationToken);
        _observations.Enqueue(new PaidObservation(@event, order.Status, order.PaymentStatus, historyCount, matchedCount));
    }

    internal sealed record PaidObservation(
        OrderPaidEvent Event, OrderStatus Status, PaymentStatus PaymentStatus, int HistoryCount, int MatchedCount);
}

// Chỉ chặn commit sau khi tạo đơn thật, để lỗi thử nghiệm không tác động bước đặt đơn.
internal sealed class SepayWebhookTransactionProbe(
    IntegrationDatabaseFixture database, SepayPaidEventObserver events) : DbTransactionInterceptor
{
    private readonly ConcurrentDictionary<Guid, int> _commitsByContext = new();
    private bool _armed;
    private int _matchingCommitAttempts;
    private int _eventsBeforeCommit;
    private int _matchingStarts;
    private readonly ConcurrentDictionary<Guid, int> _startsByContext = new();
    private readonly TaskCompletionSource _matchingStartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _rawStarts;
    private readonly TaskCompletionSource _rawStartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool FailMatchingCommit { get; private set; }
    public int MatchingParticipants { get; private set; }
    public int RawParticipants { get; private set; }
    public int MatchingCommitAttempts => Volatile.Read(ref _matchingCommitAttempts);
    public int EventsBeforeCommit => Volatile.Read(ref _eventsBeforeCommit);
    public ConcurrentQueue<RawObservation> RawBeforeMatchingCommit { get; } = new();
    public const string FailureMessage = "COM-11 injected matching commit failure";

    public void Arm(bool failMatchingCommit = false, int matchingParticipants = 0, int rawParticipants = 0)
    {
        FailMatchingCommit = failMatchingCommit;
        MatchingParticipants = matchingParticipants;
        RawParticipants = rawParticipants;
        _armed = true;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        if (!_armed || eventData.Context is null)
        {
            return result;
        }

        // Hai giao dịch khác nhau phải lưu thô xong trước khi cùng tranh khóa một đơn.
        var starts = _startsByContext.AddOrUpdate(eventData.Context.ContextId.InstanceId, 1, (_, count) => count + 1);
        // Ba request trùng phải cùng vượt bước tra ID trước khi tranh ràng buộc unique.
        if (starts == 1 && RawParticipants > 0)
        {
            if (Interlocked.Increment(ref _rawStarts) == RawParticipants)
            {
                _rawStartGate.TrySetResult();
            }
            await _rawStartGate.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        if (starts == 2 && MatchingParticipants > 0)
        {
            if (Interlocked.Increment(ref _matchingStarts) == MatchingParticipants)
            {
                _matchingStartGate.TrySetResult();
            }
            await _matchingStartGate.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        return result;
    }

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (!_armed || eventData.Context is null)
        {
            return result;
        }

        var commits = _commitsByContext.AddOrUpdate(eventData.Context.ContextId.InstanceId, 1, (_, count) => count + 1);
        if (commits != 2)
        {
            return result;
        }

        // Trước commit khớp, kết nối khác phải thấy dòng thô nhưng chưa thấy thay đổi của đơn.
        Interlocked.Increment(ref _matchingCommitAttempts);
        Interlocked.Add(ref _eventsBeforeCommit, events.Observations.Count);
        await using var commerce = database.CreateCommerceContext();
        var providerIds = eventData.Context.ChangeTracker.Entries<Pickory.Modules.Commerce.Entities.PaymentTransaction>()
            .Select(entry => entry.Entity.ProviderTxId).ToArray();
        var rows = await commerce.PaymentTransactions.AsNoTracking()
            .Where(row => providerIds.Contains(row.ProviderTxId)).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            RawBeforeMatchingCommit.Enqueue(new RawObservation(row.ProviderTxId, row.MatchStatus, row.OrderId));
            row.Raw.Dispose();
        }

        // Lỗi tại ranh giới DB buộc rollback khớp, còn transaction lưu thô đã commit phải tồn tại.
        if (FailMatchingCommit)
        {
            throw new InvalidOperationException(FailureMessage);
        }
        return result;
    }

    internal sealed record RawObservation(string ProviderTxId, string MatchStatus, string? OrderId);
}
