using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShopLicenseDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShopLicenseKind",
                table: "Sites",
                type: "TEXT",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShopLicenseSeats",
                table: "Sites",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ShopLicenseUntil",
                table: "Sites",
                type: "TEXT",
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
