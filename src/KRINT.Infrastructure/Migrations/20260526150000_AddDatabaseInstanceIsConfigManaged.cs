using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KRINT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseInstanceIsConfigManaged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-existing rows were created interactively, never declared via instances.yaml,
            // so they stay mutable.
            migrationBuilder.AddColumn<bool>(
                name: "IsConfigManaged",
                table: "DatabaseInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsConfigManaged",
                table: "DatabaseInstances");
        }
    }
}
