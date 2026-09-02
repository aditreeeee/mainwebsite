using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eGlobeSolutions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyConverterAndFlatBasePrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalculatorCurrencyRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RatePerInr = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalculatorCurrencyRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalculatorCurrencyRates_Code",
                table: "CalculatorCurrencyRates",
                column: "Code",
                unique: true);

            // Data fix: base subscription is now a flat ₹1,200/month regardless
            // of room/property count, for every plan already seeded before this change.
            migrationBuilder.Sql("UPDATE [CalculatorPlanBaseRates] SET [MonthlyRatePerUnit] = 1200 WHERE [IsDeleted] = 0;");
            migrationBuilder.Sql("UPDATE [CalculatorPlanBaseRates] SET [UnitDescription] = N'Flat monthly base fee, the same no matter how many rooms you run. Includes PMS, Channel Manager, Housekeeping and OTA Listing & Management.' WHERE [PlanType] = 0 AND [IsDeleted] = 0;");
            migrationBuilder.Sql("UPDATE [CalculatorPlanBaseRates] SET [UnitDescription] = N'Flat monthly base fee, the same no matter how many properties you run. Includes every core module except B2B Stay.' WHERE [PlanType] = 10 AND [IsDeleted] = 0;");
            migrationBuilder.Sql("UPDATE [CalculatorPlanBaseRates] SET [UnitDescription] = N'Flat monthly base fee to start; final portfolio pricing is customised. Includes every module plus Portfolio Dashboards and a Dedicated Account Manager.' WHERE [PlanType] = 20 AND [IsDeleted] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalculatorCurrencyRates");
        }
    }
}
