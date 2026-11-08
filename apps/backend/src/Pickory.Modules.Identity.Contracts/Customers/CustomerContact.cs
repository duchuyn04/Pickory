namespace Pickory.Modules.Identity.Contracts.Customers;

// Thông tin liên hệ cơ bản của khách hàng gồm tên và email.
public sealed record CustomerContact(string? Name, string Email);
