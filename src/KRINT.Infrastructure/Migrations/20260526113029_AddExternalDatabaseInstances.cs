using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KRINT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalDatabaseInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DatabaseInstances_ContainerName",
                table: "DatabaseInstances");

            migrationBuilder.AlterColumn<string>(
                name: "ContainerName",
                table: "DatabaseInstances",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ContainerId",
                table: "DatabaseInstances",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<bool>(
                name: "IsManaged",
                table: "DatabaseInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every row that existed before this migration was provisioned by KRINT.
            migrationBuilder.Sql("UPDATE \"DatabaseInstances\" SET \"IsManaged\" = TRUE;");

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseInstances_ContainerName",
                table: "DatabaseInstances",
                column: "ContainerName",
                unique: true,
                filter: "\"ContainerName\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DatabaseInstances_ContainerName",
                table: "DatabaseInstances");

            migrationBuilder.DropColumn(
                name: "IsManaged",
                table: "DatabaseInstances");

            migrationBuilder.AlterColumn<string>(
                name: "ContainerName",
                table: "DatabaseInstances",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContainerId",
                table: "DatabaseInstances",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseInstances_ContainerName",
                table: "DatabaseInstances",
                column: "ContainerName",
                unique: true);
        }
    }
}
