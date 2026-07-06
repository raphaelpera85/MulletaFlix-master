using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Database.Implementations.Migrations
{
    /// <inheritdoc />
    public partial class AddBaseItemSeriesDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BaseItems_TopParentId_Type_IsVirtualItem_SeriesId_DateCreated",
                table: "BaseItems",
                columns: new[] { "TopParentId", "Type", "IsVirtualItem", "SeriesId", "DateCreated" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BaseItems_TopParentId_Type_IsVirtualItem_SeriesId_DateCreated",
                table: "BaseItems");
        }
    }
}
