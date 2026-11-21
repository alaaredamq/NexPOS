using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderMode",
                table: "Menus",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "show");

            migrationBuilder.AddColumn<bool>(
                name: "ShowWhatsApp",
                table: "Menus",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "MenuOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MenuId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Token = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Route = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OrderType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    GuestName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Place = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuOrders_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MenuOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    MenuItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    PosProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuOrderLines_MenuOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "MenuOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MenuOrderLines_OrderId",
                table: "MenuOrderLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuOrders_MenuId_Number",
                table: "MenuOrders",
                columns: new[] { "MenuId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuOrders_MenuId_Status_CreatedAt",
                table: "MenuOrders",
                columns: new[] { "MenuId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuOrders_SiteId_Status",
                table: "MenuOrders",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuOrders_Token",
                table: "MenuOrders",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuOrderLines");

            migrationBuilder.DropTable(
                name: "MenuOrders");

            migrationBuilder.DropColumn(
                name: "OrderMode",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "ShowWhatsApp",
                table: "Menus");
        }
    }
}
