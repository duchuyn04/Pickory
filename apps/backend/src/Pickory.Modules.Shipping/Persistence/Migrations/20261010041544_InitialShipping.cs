using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;

#nullable disable

namespace Pickory.Modules.Shipping.Persistence.Migrations
{
    public partial class InitialShipping : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dùng `AlterDatabase` để Npgsql nạp lại cache kiểu sau khi tạo enum. Giữ thứ tự nhãn như model.
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:ShipmentEventSource", "webhook,poll,manual")
                .Annotation("Npgsql:Enum:ShipmentStatus", "pending_pickup,in_transit,delivering,delivered,delivery_failed,returning,returned,cancelled,exception");

            migrationBuilder.CreateTable(
                name: "address_provinces",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    syncedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("address_provinces_pkey", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    @ref = table.Column<string>(name: "ref", type: "text", nullable: false),
                    carrier = table.Column<string>(type: "text", nullable: false, defaultValue: "ghn"),
                    trackingCode = table.Column<string>(type: "text", nullable: false),
                    fee = table.Column<int>(type: "integer", nullable: false),
                    codAmount = table.Column<int>(type: "integer", nullable: false),
                    parcel = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<ShipmentStatus>(type: "\"ShipmentStatus\"", nullable: false),
                    rawStatus = table.Column<string>(type: "text", nullable: false),
                    createdAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updatedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shipments_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shipping_settings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    senderName = table.Column<string>(type: "text", nullable: false),
                    senderPhone = table.Column<string>(type: "text", nullable: false),
                    provinceCode = table.Column<string>(type: "text", nullable: false),
                    provinceName = table.Column<string>(type: "text", nullable: false),
                    wardCode = table.Column<string>(type: "text", nullable: false),
                    wardName = table.Column<string>(type: "text", nullable: false),
                    line = table.Column<string>(type: "text", nullable: false),
                    defaultServiceTypeId = table.Column<int>(type: "integer", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shipping_settings_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "address_wards",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    provinceCode = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    syncedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("address_wards_pkey", x => x.code);
                    table.ForeignKey(
                        name: "address_wards_provinceCode_fkey",
                        column: x => x.provinceCode,
                        principalTable: "address_provinces",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    shipmentId = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<ShipmentStatus>(type: "\"ShipmentStatus\"", nullable: false),
                    rawStatus = table.Column<string>(type: "text", nullable: false),
                    occurredAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false),
                    source = table.Column<ShipmentEventSource>(type: "\"ShipmentEventSource\"", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shipment_events_pkey", x => x.id);
                    table.ForeignKey(
                        name: "shipment_events_shipmentId_fkey",
                        column: x => x.shipmentId,
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "address_wards_provinceCode_idx",
                table: "address_wards",
                column: "provinceCode");

            migrationBuilder.CreateIndex(
                name: "shipment_events_shipmentId_idx",
                table: "shipment_events",
                column: "shipmentId");

            migrationBuilder.CreateIndex(
                name: "shipments_ref_key",
                table: "shipments",
                column: "ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "shipments_trackingCode_key",
                table: "shipments",
                column: "trackingCode",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "address_wards");

            migrationBuilder.DropTable(
                name: "shipment_events");

            migrationBuilder.DropTable(
                name: "shipping_settings");

            migrationBuilder.DropTable(
                name: "address_provinces");

            migrationBuilder.DropTable(
                name: "shipments");

            // Gỡ enum sau bảng sử dụng để `Down` không thất bại do đối tượng còn phụ thuộc.
            migrationBuilder.Sql("""
                DROP TYPE IF EXISTS public."ShipmentEventSource";
                DROP TYPE IF EXISTS public."ShipmentStatus";
                """);
        }
    }
}
