using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Menus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClientId = table.Column<int>(type: "INTEGER", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Tagline = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    LogoFile = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreviewKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LinkedSiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CatalogHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CatalogAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PosArranged = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Menus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Menus_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MenuItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MenuId = table.Column<int>(type: "INTEGER", nullable: false),
                    SectionId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PosDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    ImageFile = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSoldOut = table.Column<bool>(type: "INTEGER", nullable: false),
                    PosProductId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItems_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MenuSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MenuId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    PosKey = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuSections_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_MenuId_PosProductId",
                table: "MenuItems",
                columns: new[] { "MenuId", "PosProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_MenuId_SortOrder",
                table: "MenuItems",
                columns: new[] { "MenuId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Menus_ClientId",
                table: "Menus",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Menus_LinkedSiteId",
                table: "Menus",
                column: "LinkedSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Menus_Slug",
                table: "Menus",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuSections_MenuId_SortOrder",
                table: "MenuSections",
                columns: new[] { "MenuId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuItems");

            migrationBuilder.DropTable(
                name: "MenuSections");

            migrationBuilder.DropTable(
                name: "Menus");
        }
    }
}
