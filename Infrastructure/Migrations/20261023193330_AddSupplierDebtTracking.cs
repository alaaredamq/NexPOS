using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierDebtTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Suppliers used to carry two free-form numbers (DebtToSupplier: what I owe them,
            // DebtFromSupplier: what they owe me). They are replaced by one signed Balance
            // (positive = I owe them) backed by a ledger, so convert before dropping them.
            migrationBuilder.AddColumn<decimal>(
                name: "Balance",
                table: "Suppliers",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(@"
                UPDATE Suppliers
                SET Balance = ROUND(CAST(DebtToSupplier AS REAL) - CAST(DebtFromSupplier AS REAL), 2);");

            migrationBuilder.CreateTable(
                name: "SupplierDebtTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SupplierId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierDebtTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierDebtTransactions_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupplierDebtTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierDebtTransactions_SupplierId",
                table: "SupplierDebtTransactions",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierDebtTransactions_UserId",
                table: "SupplierDebtTransactions",
                column: "UserId");

            // One opening-balance entry per supplier that already had a non-zero balance, so the
            // ledger sums to Balance from day one. Attributed to the lowest-Id user (a ledger row
            // needs a user); Type 2 = ManualAdjustment.
            migrationBuilder.Sql(@"
                INSERT INTO SupplierDebtTransactions (SupplierId, UserId, Amount, Type, Note, CreatedAt)
                SELECT s.Id, (SELECT MIN(Id) FROM Users), s.Balance, 2, 'Opening balance (migrated)', datetime('now')
                FROM Suppliers s
                WHERE CAST(s.Balance AS REAL) <> 0 AND EXISTS (SELECT 1 FROM Users);");

            migrationBuilder.DropColumn(
                name: "DebtFromSupplier",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DebtToSupplier",
                table: "Suppliers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DebtFromSupplier",
                table: "Suppliers",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DebtToSupplier",
                table: "Suppliers",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            // Split the signed balance back into the two columns.
            migrationBuilder.Sql(@"
                UPDATE Suppliers
                SET DebtToSupplier = CASE WHEN CAST(Balance AS REAL) > 0 THEN Balance ELSE 0 END,
                    DebtFromSupplier = CASE WHEN CAST(Balance AS REAL) < 0 THEN -CAST(Balance AS REAL) ELSE 0 END;");

            migrationBuilder.DropTable(
                name: "SupplierDebtTransactions");

            migrationBuilder.DropColumn(
                name: "Balance",
                table: "Suppliers");
        }
    }
}
