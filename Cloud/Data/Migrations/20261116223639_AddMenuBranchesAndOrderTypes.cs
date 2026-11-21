using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuBranchesAndOrderTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MenuBranches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MenuId = table.Column<int>(type: "INTEGER", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CatalogHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CatalogAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PosArranged = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuBranches_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuBranches_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuItemBranches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MenuItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PosProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItemBranches_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MenuOrderTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MenuId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Adjust = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "none"),
                    AdjustValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuOrderTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuOrderTypes_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MenuItemTypePrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MenuItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemTypePrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItemTypePrices_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuItemTypePrices_MenuOrderTypes_OrderTypeId",
                        column: x => x.OrderTypeId,
                        principalTable: "MenuOrderTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MenuBranches_MenuId_Slug",
                table: "MenuBranches",
                columns: new[] { "MenuId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuBranches_SiteId",
                table: "MenuBranches",
                column: "SiteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemBranches_MenuItemId_SiteId",
                table: "MenuItemBranches",
                columns: new[] { "MenuItemId", "SiteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemBranches_SiteId_PosProductId",
                table: "MenuItemBranches",
                columns: new[] { "SiteId", "PosProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemTypePrices_MenuItemId_OrderTypeId",
                table: "MenuItemTypePrices",
                columns: new[] { "MenuItemId", "OrderTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemTypePrices_OrderTypeId",
                table: "MenuItemTypePrices",
                column: "OrderTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuOrderTypes_MenuId_SortOrder",
                table: "MenuOrderTypes",
                columns: new[] { "MenuId", "SortOrder" });

            // Today's single link becomes the first branch, and each POS dish keeps its product and price at that branch,
            // before the old columns go. (Plain SQL that SQLite and SQL Server both read.)
            migrationBuilder.Sql(MenuBranchesCopy.Branches);
            migrationBuilder.Sql(MenuBranchesCopy.Prices);

            migrationBuilder.DropIndex(
                name: "IX_Menus_LinkedSiteId",
                table: "Menus");

            migrationBuilder.DropIndex(
                name: "IX_MenuItems_MenuId_PosProductId",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "CatalogAt",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "CatalogHash",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "LinkedSiteId",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "PosArranged",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "PosProductId",
                table: "MenuItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuBranches");

            migrationBuilder.DropTable(
                name: "MenuItemBranches");

            migrationBuilder.DropTable(
                name: "MenuItemTypePrices");

            migrationBuilder.DropTable(
                name: "MenuOrderTypes");

            migrationBuilder.AddColumn<DateTime>(
                name: "CatalogAt",
                table: "Menus",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CatalogHash",
                table: "Menus",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedSiteId",
                table: "Menus",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PosArranged",
                table: "Menus",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PosProductId",
                table: "MenuItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Menus_LinkedSiteId",
                table: "Menus",
                column: "LinkedSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_MenuId_PosProductId",
                table: "MenuItems",
                columns: new[] { "MenuId", "PosProductId" });
        }
    }
}
