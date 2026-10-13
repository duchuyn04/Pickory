using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Commerce.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderRefundTransactionRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "refundTransactionRef",
                table: "orders",
                type: "text",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refundTransactionRef",
                table: "orders");
        }
    }
}
