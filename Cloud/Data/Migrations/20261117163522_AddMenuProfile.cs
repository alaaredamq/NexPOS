using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "MenuSections",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageFile",
                table: "MenuSections",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Menus",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverFile",
                table: "Menus",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Facebook",
                table: "Menus",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hours",
                table: "Menus",
                type: "TEXT",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instagram",
                table: "Menus",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MapUrl",
                table: "Menus",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Menus",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "Menus",
                type: "TEXT",
                maxLength: 12,
                nullable: false,
                defaultValue: "basil");

            migrationBuilder.AddColumn<string>(
                name: "TikTok",
                table: "Menus",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Menus",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp",
                table: "Menus",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "MenuBranches",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hours",
                table: "MenuBranches",
                type: "TEXT",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MapUrl",
                table: "MenuBranches",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "MenuBranches",
                type: "TEXT",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "MenuSections");

            migrationBuilder.DropColumn(
                name: "ImageFile",
                table: "MenuSections");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "CoverFile",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "Facebook",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "Hours",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "Instagram",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "MapUrl",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "TikTok",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "WhatsApp",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "MenuBranches");

            migrationBuilder.DropColumn(
                name: "Hours",
                table: "MenuBranches");

            migrationBuilder.DropColumn(
                name: "MapUrl",
                table: "MenuBranches");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "MenuBranches");
        }
    }
}
