using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffNotificationTargeting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionLabel",
                table: "InAppNotifications",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActionType",
                table: "InAppNotifications",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "InAppNotifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecipientUserId",
                table: "InAppNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedEntityId",
                table: "InAppNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedEntityType",
                table: "InAppNotifications",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Severity",
                table: "InAppNotifications",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "NotificationEmailDeliveries",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QueuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEmailDeliveries", x => new { x.NotificationId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "StaffNotificationPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InAppEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ToastEnabled = table.Column<bool>(type: "bit", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    EmailFrequency = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffNotificationPreferences", x => new { x.UserId, x.Category });
                });

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotifications_RecipientUserId_CreatedAt",
                table: "InAppNotifications",
                columns: new[] { "RecipientUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationEmailDeliveries");

            migrationBuilder.DropTable(
                name: "StaffNotificationPreferences");

            migrationBuilder.DropIndex(
                name: "IX_InAppNotifications_RecipientUserId_CreatedAt",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "ActionLabel",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "ActionType",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "RecipientUserId",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "RelatedEntityId",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "RelatedEntityType",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "InAppNotifications");
        }
    }
}
