using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class AddMenuListingStyleSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ListingStyle",
                table: "Menus",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "compact");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ListingStyle",
                table: "Menus");
        }
    }
}
