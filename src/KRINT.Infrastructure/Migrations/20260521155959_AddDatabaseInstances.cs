using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KRINT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatabaseInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Engine = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    ContainerName = table.Column<string>(type: "text", nullable: false),
                    ContainerId = table.Column<string>(type: "text", nullable: false),
                    Host = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    DatabaseName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseInstances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseInstances_ContainerName",
                table: "DatabaseInstances",
                column: "ContainerName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatabaseInstances");
        }
    }
}
