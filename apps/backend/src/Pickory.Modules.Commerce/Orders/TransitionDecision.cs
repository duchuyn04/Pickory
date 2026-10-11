namespace Pickory.Modules.Commerce.Orders;

// Lý do từ chối một lần chuyển trạng thái. API trả 409 `INVALID_ORDER_TRANSITION` cho mọi lý do khác `None`.
public enum TransitionRejection
{
    None,
    NotInTransitionTable,
    ActorNotAllowed,
    ShipmentStillActive,
}

// Kết quả kiểm một lần chuyển trạng thái.
public readonly record struct TransitionDecision(bool IsAllowed, TransitionRejection Rejection)
{
    public static TransitionDecision Allowed { get; } = new(true, TransitionRejection.None);

    public static TransitionDecision Rejected(TransitionRejection rejection) => new(false, rejection);
}
