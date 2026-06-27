using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KRINT.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeTokenAndConfigManaged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsConfigManaged",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "Nodes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsConfigManaged",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "Nodes");
        }
    }
}
