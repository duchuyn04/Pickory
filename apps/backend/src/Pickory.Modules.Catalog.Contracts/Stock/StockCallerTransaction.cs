using System.Data.Common;
using Pickory.Shared.Events;

namespace Pickory.Modules.Catalog.Contracts.Stock;

// Transaction của module gọi. Catalog ghi trong transaction này và gom `StockChangedEvent` vào `Events`.
public sealed record StockCallerTransaction(DbTransaction Transaction, DomainEventCollector Events);
