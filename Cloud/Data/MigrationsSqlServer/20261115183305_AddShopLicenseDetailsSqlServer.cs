using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class AddShopLicenseDetailsSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShopLicenseKind",
                table: "Sites",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShopLicenseSeats",
                table: "Sites",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ShopLicenseUntil",
                table: "Sites",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShopLicenseKind",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "ShopLicenseSeats",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "ShopLicenseUntil",
                table: "Sites");
        }
    }
}
