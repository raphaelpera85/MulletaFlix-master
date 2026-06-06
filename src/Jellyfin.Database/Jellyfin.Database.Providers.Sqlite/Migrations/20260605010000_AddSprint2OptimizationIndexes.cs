using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Database.Providers.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSprint2OptimizationIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserData_UserId_Played_LastPlayedDate_ItemId",
                table: "UserData",
                columns: new[] { "UserId", "Played", "LastPlayedDate", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserData_UserId_IsFavorite_LastPlayedDate_ItemId",
                table: "UserData",
                columns: new[] { "UserId", "IsFavorite", "LastPlayedDate", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_BaseItems_Type_TopParentId_IsVirtualItem_DateCreated",
                table: "BaseItems",
                columns: new[] { "Type", "TopParentId", "IsVirtualItem", "DateCreated" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserData_UserId_Played_LastPlayedDate_ItemId",
                table: "UserData");

            migrationBuilder.DropIndex(
                name: "IX_UserData_UserId_IsFavorite_LastPlayedDate_ItemId",
                table: "UserData");

            migrationBuilder.DropIndex(
                name: "IX_BaseItems_Type_TopParentId_IsVirtualItem_DateCreated",
                table: "BaseItems");
        }
    }
}
