using System.Text.Json.Nodes;

namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Yêu cầu gọi tool do model sinh ra. `Input` là JSON thô, gateway kiểm tra trước khi chạy tool.
public sealed record ToolCall(string Id, string Name, JsonNode? Input);
