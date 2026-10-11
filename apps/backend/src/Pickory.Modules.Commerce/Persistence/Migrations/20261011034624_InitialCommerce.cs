using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Pickory.Modules.Commerce.Contracts.Orders;

#nullable disable

namespace Pickory.Modules.Commerce.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:OrderStatus", "pending_payment,pending_confirmation,confirmed,shipping,delivered,returned,cancelled")
                .Annotation("Npgsql:Enum:PaymentMethod", "cod,sepay")
                .Annotation("Npgsql:Enum:PaymentStatus", "unpaid,paid,refund_pending,refunded");

            migrationBuilder.CreateTable(
                name: "carts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    anonymousId = table.Column<string>(type: "text", nullable: true),
                    userId = table.Column<string>(type: "text", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("carts_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "commerce_settings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    freeShippingThreshold = table.Column<int>(type: "integer", nullable: false),
                    codMaxAmount = table.Column<int>(type: "integer", nullable: false),
                    maxQuantityPerSku = table.Column<int>(type: "integer", nullable: false),
                    maxCartLines = table.Column<int>(type: "integer", nullable: false),
                    sepayAccountNumber = table.Column<string>(type: "text", nullable: true),
                    sepayBankCode = table.Column<string>(type: "text", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("commerce_settings_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    userId = table.Column<string>(type: "text", nullable: true),
                    anonymousId = table.Column<string>(type: "text", nullable: false),
                    contact = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    shippingAddress = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    invoiceInfo = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    paymentMethod = table.Column<PaymentMethod>(type: "\"PaymentMethod\"", nullable: false),
                    status = table.Column<OrderStatus>(type: "\"OrderStatus\"", nullable: false),
                    paymentStatus = table.Column<PaymentStatus>(type: "\"PaymentStatus\"", nullable: false),
                    flags = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]"),
                    subtotal = table.Column<int>(type: "integer", nullable: false),
                    shippingFee = table.Column<int>(type: "integer", nullable: false),
                    shippingDiscount = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    paymentDeadline = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: true),
                    accessTokenHash = table.Column<string>(type: "text", nullable: false),
                    idempotencyKey = table.Column<string>(type: "text", nullable: false),
                    createdAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("orders_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_addresses",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    userId = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    isDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("saved_addresses_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cart_items",
                columns: table => new
                {
                    cartId = table.Column<string>(type: "text", nullable: false),
                    skuId = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("cart_items_pkey", x => new { x.cartId, x.skuId });
                    table.ForeignKey(
                        name: "cart_items_cartId_fkey",
                        column: x => x.cartId,
                        principalTable: "carts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    orderId = table.Column<string>(type: "text", nullable: false),
                    skuId = table.Column<string>(type: "text", nullable: false),
                    productId = table.Column<string>(type: "text", nullable: false),
                    nameSnapshot = table.Column<string>(type: "text", nullable: false),
                    skuCodeSnapshot = table.Column<string>(type: "text", nullable: false),
                    optionValues = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    imagePublicId = table.Column<string>(type: "text", nullable: true),
                    unitPrice = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("order_items_pkey", x => x.id);
                    table.ForeignKey(
                        name: "order_items_orderId_fkey",
                        column: x => x.orderId,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_status_history",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    orderId = table.Column<string>(type: "text", nullable: false),
                    fromStatus = table.Column<OrderStatus>(type: "\"OrderStatus\"", nullable: false),
                    toStatus = table.Column<OrderStatus>(type: "\"OrderStatus\"", nullable: false),
                    actorId = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    createdAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("order_status_history_pkey", x => x.id);
                    table.ForeignKey(
                        name: "order_status_history_orderId_fkey",
                        column: x => x.orderId,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    providerTxId = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    orderId = table.Column<string>(type: "text", nullable: true),
                    matchStatus = table.Column<string>(type: "text", nullable: false),
                    receivedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("payment_transactions_pkey", x => x.id);
                    table.ForeignKey(
                        name: "payment_transactions_orderId_fkey",
                        column: x => x.orderId,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    productId = table.Column<string>(type: "text", nullable: false),
                    userId = table.Column<string>(type: "text", nullable: true),
                    orderId = table.Column<string>(type: "text", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "text", nullable: true),
                    hiddenAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: true),
                    hiddenReason = table.Column<string>(type: "text", nullable: true),
                    reportCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    createdAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updatedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("reviews_pkey", x => x.id);
                    table.ForeignKey(
                        name: "reviews_orderId_fkey",
                        column: x => x.orderId,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "carts_userId_key",
                table: "carts",
                column: "userId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "orders_code_key",
                table: "orders",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "orders_idempotencyKey_key",
                table: "orders",
                column: "idempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "orders_status_createdAt_idx",
                table: "orders",
                columns: new[] { "status", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "payment_transactions_providerTxId_key",
                table: "payment_transactions",
                column: "providerTxId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "reviews_productId_userId_key",
                table: "reviews",
                columns: new[] { "productId", "userId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_items");

            migrationBuilder.DropTable(
                name: "commerce_settings");

            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "order_status_history");

            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropTable(
                name: "saved_addresses");

            migrationBuilder.DropTable(
                name: "carts");

            migrationBuilder.DropTable(
                name: "orders");

            // Gỡ enum sau bảng sử dụng để `Down` không thất bại do đối tượng còn phụ thuộc.
            migrationBuilder.Sql("""
                DROP TYPE IF EXISTS public."PaymentMethod";
                DROP TYPE IF EXISTS public."PaymentStatus";
                DROP TYPE IF EXISTS public."OrderStatus";
                """);
        }
    }
}
