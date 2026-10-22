using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pickory.Modules.SupportDesk.Entities;

#nullable disable

namespace Pickory.Modules.SupportDesk.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSupportDesk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:CallbackRequestStatus", "pending,done,unreachable")
                .Annotation("Npgsql:Enum:StaffPresenceStatus", "available,away")
                .Annotation("Npgsql:Enum:SupportConversationStatus", "open,closed")
                .Annotation("Npgsql:Enum:SupportMessageRole", "customer,staff,system");

            migrationBuilder.CreateTable(
                name: "canned_responses",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    ownerId = table.Column<string>(type: "text", nullable: true),
                    shortcut = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    shared = table.Column<bool>(type: "boolean", nullable: false),
                    createdAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false),
                    updatedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("canned_responses_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff_presence",
                columns: table => new
                {
                    staffId = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<StaffPresenceStatus>(type: "\"StaffPresenceStatus\"", nullable: false),
                    lastSeenAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("staff_presence_pkey", x => x.staffId);
                });

            migrationBuilder.CreateTable(
                name: "support_conversations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    customerUserId = table.Column<string>(type: "text", nullable: true),
                    anonymousId = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<SupportConversationStatus>(type: "\"SupportConversationStatus\"", nullable: false),
                    createdAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false),
                    lastMessageAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false),
                    closedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("support_conversations_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_settings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    opensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    closesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    holidays = table.Column<DateOnly[]>(type: "date[]", nullable: false),
                    waitNudgeMinutes = table.Column<int>(type: "integer", nullable: false),
                    autoCloseMinutes = table.Column<int>(type: "integer", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("support_settings_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "callback_requests",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    conversationId = table.Column<string>(type: "text", nullable: true),
                    anonymousId = table.Column<string>(type: "text", nullable: false),
                    customerUserId = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<CallbackRequestStatus>(type: "\"CallbackRequestStatus\"", nullable: false),
                    handledBy = table.Column<string>(type: "text", nullable: true),
                    createdAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false),
                    handledAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("callback_requests_pkey", x => x.id);
                    table.ForeignKey(
                        name: "callback_requests_conversationId_fkey",
                        column: x => x.conversationId,
                        principalTable: "support_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "support_messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    conversationId = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<SupportMessageRole>(type: "\"SupportMessageRole\"", nullable: false),
                    staffId = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: true),
                    createdAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("support_messages_pkey", x => x.id);
                    table.ForeignKey(
                        name: "support_messages_conversationId_fkey",
                        column: x => x.conversationId,
                        principalTable: "support_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    conversationId = table.Column<string>(type: "text", nullable: false),
                    handoffAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false),
                    handoffInHours = table.Column<bool>(type: "boolean", nullable: false),
                    customerUserId = table.Column<string>(type: "text", nullable: true),
                    anonymousId = table.Column<string>(type: "text", nullable: false),
                    staffId = table.Column<string>(type: "text", nullable: true),
                    claimedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: true),
                    firstResponseAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: true),
                    closedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: true),
                    closedBy = table.Column<string>(type: "text", nullable: true),
                    csat = table.Column<int>(type: "integer", nullable: true),
                    transferNote = table.Column<string>(type: "text", nullable: true),
                    nudgedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("support_sessions_pkey", x => x.id);
                    table.ForeignKey(
                        name: "support_sessions_conversationId_fkey",
                        column: x => x.conversationId,
                        principalTable: "support_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "support_settings",
                columns: new[] { "id", "autoCloseMinutes", "closesAt", "holidays", "opensAt", "updatedAt", "waitNudgeMinutes" },
                values: new object[] { "singleton", 30, new TimeOnly(21, 0, 0), new DateOnly[0], new TimeOnly(8, 0, 0), new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), 10 });

            migrationBuilder.CreateIndex(
                name: "callback_requests_status_createdAt_idx",
                table: "callback_requests",
                columns: new[] { "status", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "support_conversations_anonymousId_open_key",
                table: "support_conversations",
                column: "anonymousId",
                unique: true,
                filter: "\"status\" = 'open'::\"SupportConversationStatus\"");

            migrationBuilder.CreateIndex(
                name: "support_messages_conversationId_createdAt_idx",
                table: "support_messages",
                columns: new[] { "conversationId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "support_sessions_closedAt_handoffAt_idx",
                table: "support_sessions",
                columns: new[] { "closedAt", "handoffAt" });

            migrationBuilder.CreateIndex(
                name: "support_sessions_conversationId_open_key",
                table: "support_sessions",
                column: "conversationId",
                unique: true,
                filter: "\"closedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "callback_requests");

            migrationBuilder.DropTable(
                name: "canned_responses");

            migrationBuilder.DropTable(
                name: "staff_presence");

            migrationBuilder.DropTable(
                name: "support_messages");

            migrationBuilder.DropTable(
                name: "support_sessions");

            migrationBuilder.DropTable(
                name: "support_settings");

            migrationBuilder.DropTable(
                name: "support_conversations");

            // Gỡ enum sau bảng sử dụng để `Down` không thất bại do đối tượng còn phụ thuộc.
            migrationBuilder.Sql("""
                DROP TYPE IF EXISTS public."CallbackRequestStatus";
                DROP TYPE IF EXISTS public."StaffPresenceStatus";
                DROP TYPE IF EXISTS public."SupportConversationStatus";
                DROP TYPE IF EXISTS public."SupportMessageRole";
                """);
        }
    }
}
