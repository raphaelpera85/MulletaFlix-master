using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Database.Implementations.Migrations
{
    /// <inheritdoc />
    public partial class FixBaseItemRedundantRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "OriginalTitle",
                table: "BaseItems",
                type: "varchar(255)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_FullTextSearch ON BaseItems;");

            migrationBuilder.CreateIndex(
                name: "IX_BaseItems_FullTextSearch",
                table: "BaseItems",
                columns: new[] { "CleanName", "OriginalTitle" })
                .Annotation("MySql:FullTextIndex", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BaseItems_FullTextSearch",
                table: "BaseItems");

            migrationBuilder.AlterColumn<string>(
                name: "OriginalTitle",
                table: "BaseItems",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
