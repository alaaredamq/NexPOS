using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cloud.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SpentOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Payee = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinanceBudgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IncomeTarget = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    SpendingLimit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceBudgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClientId = table.Column<int>(type: "INTEGER", nullable: true),
                    Payer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SpentOn",
                table: "Expenses",
                column: "SpentOn");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBudgets_Month",
                table: "FinanceBudgets",
                column: "Month",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ClientId_ReceivedOn",
                table: "Payments",
                columns: new[] { "ClientId", "ReceivedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReceivedOn",
                table: "Payments",
                column: "ReceivedOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "FinanceBudgets");

            migrationBuilder.DropTable(
                name: "Payments");
        }
    }
}
