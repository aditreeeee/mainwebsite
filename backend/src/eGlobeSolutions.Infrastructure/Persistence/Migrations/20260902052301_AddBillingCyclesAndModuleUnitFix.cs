using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eGlobeSolutions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingCyclesAndModuleUnitFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalculatorBillingCycles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Months = table.Column<int>(type: "int", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
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
                    table.PrimaryKey("PK_CalculatorBillingCycles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalculatorBillingCycles_Months",
                table: "CalculatorBillingCycles",
                column: "Months",
                unique: true);

            // Data fix: Housekeeping and Reviews Manager are property-based, not
            // room-based (ModuleChargeType.PerPropertyMonthly = 10), for rows
            // already seeded before this change. Reviews Manager's per-room rate
            // (₹9) is bumped to a per-property rate (₹299) to stay sane at scale.
            migrationBuilder.Sql("UPDATE [CalculatorPricingModules] SET [ChargeType] = 10 WHERE [Code] = 'housekeeping' AND [IsDeleted] = 0;");
            migrationBuilder.Sql("UPDATE [CalculatorPricingModules] SET [ChargeType] = 10, [MonthlyRate] = 299 WHERE [Code] = 'reviews-manager' AND [IsDeleted] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalculatorBillingCycles");
        }
    }
}
