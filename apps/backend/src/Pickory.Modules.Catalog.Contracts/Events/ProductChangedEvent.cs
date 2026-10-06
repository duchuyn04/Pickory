using Pickory.Shared.Events;

namespace Pickory.Modules.Catalog.Contracts.Events;

// Phát sau khi thay đổi của sản phẩm đã commit. Ví dụ, module tìm kiếm cập nhật chỉ mục khi nhận sự kiện này.
public sealed record ProductChangedEvent(string ProductId, ProductChange Change) : IDomainEvent;
