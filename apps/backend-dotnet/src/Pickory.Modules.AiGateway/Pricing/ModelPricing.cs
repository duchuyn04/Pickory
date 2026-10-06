namespace Pickory.Modules.AiGateway.Pricing;

// Đơn giá USD cho một triệu token. Token đọc từ cache prompt có giá riêng, thấp hơn token thường.
public sealed record ModelPricing(double InputMissPerMillionUsd, double InputHitPerMillionUsd, double OutputPerMillionUsd);
