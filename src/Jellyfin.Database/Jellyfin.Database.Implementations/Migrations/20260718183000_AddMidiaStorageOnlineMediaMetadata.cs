using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Database.Implementations.Migrations;

public partial class AddMidiaStorageOnlineMediaMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MidiaStorageOnlineMediaMetadata",
            columns: table => new
            {
                RelativePath = table.Column<string>(maxLength: 512, nullable: false),
                ContentType = table.Column<string>(maxLength: 16, nullable: false),
                Title = table.Column<string>(maxLength: 255, nullable: false),
                Year = table.Column<int>(nullable: true),
                SeasonNumber = table.Column<int>(nullable: true),
                EpisodeNumber = table.Column<int>(nullable: true),
                Mode = table.Column<string>(maxLength: 16, nullable: false),
                SourceUrl = table.Column<string>(maxLength: 2048, nullable: false),
                SourceId = table.Column<string>(maxLength: 255, nullable: false),
                OriginalFileName = table.Column<string>(maxLength: 255, nullable: false),
                RecognizedAtUtc = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MidiaStorageOnlineMediaMetadata", x => x.RelativePath);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MidiaStorageOnlineMediaMetadata_SourceId",
            table: "MidiaStorageOnlineMediaMetadata",
            column: "SourceId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MidiaStorageOnlineMediaMetadata");
    }
}
