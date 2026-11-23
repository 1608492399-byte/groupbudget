using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroupBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBaselineProjectColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualBaselineProject",
                table: "BaselineSnapshots",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BudgetBaselineProject",
                table: "BaselineSnapshots",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualBaselineProject",
                table: "BaselineRecords",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BudgetBaselineProject",
                table: "BaselineRecords",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualBaselineProject",
                table: "BaselineSnapshots");

            migrationBuilder.DropColumn(
                name: "BudgetBaselineProject",
                table: "BaselineSnapshots");

            migrationBuilder.DropColumn(
                name: "ActualBaselineProject",
                table: "BaselineRecords");

            migrationBuilder.DropColumn(
                name: "BudgetBaselineProject",
                table: "BaselineRecords");
        }
    }
}
