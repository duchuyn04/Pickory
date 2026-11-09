using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.CannedResponses;

// Một mẫu trong response của route mẫu trả lời nhanh. `Editable` cho frontend biết có hiện nút sửa hay không.
internal sealed record CannedResponseItem(string Id, string Shortcut, string Body, bool Shared, bool Editable)
{
    public static CannedResponseItem From(CannedResponse row, bool editable) =>
        new(row.Id, row.Shortcut, row.Body, row.Shared, editable);
}
