#pragma warning disable CS1591, SA1601

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MulletaFlix.Server.Implementations.Migrations
{
    public partial class AddIndexActivityLogsDateCreated : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_DateCreated",
                schema: "MulletaFlix",
                table: "ActivityLogs",
                column: "DateCreated");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityLogs_DateCreated",
                schema: "MulletaFlix",
                table: "ActivityLogs");
        }
    }
}

