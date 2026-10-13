using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KRINT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstanceLifecycleColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plugins",
                table: "DatabaseInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageBind",
                table: "DatabaseInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KeepLast",
                table: "BackupSchedules",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupEntries_InstanceId",
                table: "BackupEntries",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_CreatedAt",
                table: "ActivityEntries",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BackupEntries_InstanceId",
                table: "BackupEntries");

            migrationBuilder.DropIndex(
                name: "IX_ActivityEntries_CreatedAt",
                table: "ActivityEntries");

            migrationBuilder.DropColumn(
                name: "Plugins",
                table: "DatabaseInstances");

            migrationBuilder.DropColumn(
                name: "StorageBind",
                table: "DatabaseInstances");

            migrationBuilder.DropColumn(
                name: "KeepLast",
                table: "BackupSchedules");
        }
    }
}
