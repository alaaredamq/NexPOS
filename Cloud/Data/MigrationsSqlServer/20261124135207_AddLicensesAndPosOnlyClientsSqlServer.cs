using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class AddLicensesAndPosOnlyClientsSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasOnline",
                table: "Clients",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "Licenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientId = table.Column<int>(type: "int", nullable: true),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MachineId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Seats = table.Column<int>(type: "int", nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    Serial = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Customer = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    KeyText = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    PaymentId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
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
