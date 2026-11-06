using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteHeartbeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "Sites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SiteHeartbeats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Hour = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Contacts = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteHeartbeats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteHeartbeats_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteHeartbeats_SiteId_Hour",
                table: "SiteHeartbeats",
                columns: new[] { "SiteId", "Hour" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteHeartbeats");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "Sites");
        }
    }
}
