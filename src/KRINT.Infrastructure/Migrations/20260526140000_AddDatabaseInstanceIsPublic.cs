using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KRINT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseInstanceIsPublic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every container provisioned before this migration was bound to 0.0.0.0 (Docker's
            // default when HostIP is left unset on the port binding), so existing rows seed to
            // TRUE. New provisions default to FALSE (localhost-only) in code.
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "DatabaseInstances",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "DatabaseInstances");
        }
    }
}
