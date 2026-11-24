using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLicensesAndPosOnlyClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasOnline",
                table: "Clients",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "Licenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClientId = table.Column<int>(type: "INTEGER", nullable: true),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MachineId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Plan = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Seats = table.Column<int>(type: "INTEGER", nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Serial = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Customer = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    KeyText = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    PaymentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Licenses_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Licenses_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Licenses_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ClientId",
                table: "Licenses",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_MachineId",
                table: "Licenses",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_PaymentId",
                table: "Licenses",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_Serial_MachineId",
                table: "Licenses",
                columns: new[] { "Serial", "MachineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_SiteId",
                table: "Licenses",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ValidUntil",
                table: "Licenses",
                column: "ValidUntil");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Licenses");

            migrationBuilder.DropColumn(
                name: "HasOnline",
                table: "Clients");
        }
    }
}
