using Microsoft.EntityFrameworkCore.Migrations;
using Pickory.Modules.Commerce.Contracts.Orders;

#nullable disable

namespace Pickory.Modules.Commerce.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderShippingFeeSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:OrderStatus", "pending_payment,pending_confirmation,confirmed,shipping,delivered,returned,cancelled")
                .Annotation("Npgsql:Enum:PaymentMethod", "cod,sepay")
                .Annotation("Npgsql:Enum:PaymentStatus", "unpaid,paid,refund_pending,refunded")
                .Annotation("Npgsql:Enum:ShippingFeeSource", "ghn,cached,fallback")
                .OldAnnotation("Npgsql:Enum:OrderStatus", "pending_payment,pending_confirmation,confirmed,shipping,delivered,returned,cancelled")
                .OldAnnotation("Npgsql:Enum:PaymentMethod", "cod,sepay")
                .OldAnnotation("Npgsql:Enum:PaymentStatus", "unpaid,paid,refund_pending,refunded");

            migrationBuilder.AddColumn<ShippingFeeSource>(
                name: "shippingFeeSource",
                table: "orders",
                type: "\"ShippingFeeSource\"",
                nullable: false,
                defaultValue: ShippingFeeSource.Ghn);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shippingFeeSource",
                table: "orders");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:OrderStatus", "pending_payment,pending_confirmation,confirmed,shipping,delivered,returned,cancelled")
                .Annotation("Npgsql:Enum:PaymentMethod", "cod,sepay")
                .Annotation("Npgsql:Enum:PaymentStatus", "unpaid,paid,refund_pending,refunded")
                .OldAnnotation("Npgsql:Enum:OrderStatus", "pending_payment,pending_confirmation,confirmed,shipping,delivered,returned,cancelled")
                .OldAnnotation("Npgsql:Enum:PaymentMethod", "cod,sepay")
                .OldAnnotation("Npgsql:Enum:PaymentStatus", "unpaid,paid,refund_pending,refunded")
                .OldAnnotation("Npgsql:Enum:ShippingFeeSource", "ghn,cached,fallback");
        }
    }
}
