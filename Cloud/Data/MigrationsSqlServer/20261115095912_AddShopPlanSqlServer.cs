using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class AddShopPlanSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShopPlan",
                table: "Sites",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShopPlan",
                table: "Sites");
        }
    }
}
