using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KRINT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNodesAndDatabaseInstanceNodeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NodeId",
                table: "DatabaseInstances",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Nodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    MachineName = table.Column<string>(type: "text", nullable: false),
                    Os = table.Column<string>(type: "text", nullable: false),
                    DockerVersion = table.Column<string>(type: "text", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nodes", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Nodes");

            migrationBuilder.DropColumn(
                name: "NodeId",
                table: "DatabaseInstances");
        }
    }
}
