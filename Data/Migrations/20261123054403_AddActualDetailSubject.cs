using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroupBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActualDetailSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubjectId",
                table: "ActualDetails",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActualDetails_BudgetBatchId_SubjectId_OrganizationId",
                table: "ActualDetails",
                columns: new[] { "BudgetBatchId", "SubjectId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualDetails_SubjectId",
                table: "ActualDetails",
                column: "SubjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_ActualDetails_Subjects_SubjectId",
                table: "ActualDetails",
                column: "SubjectId",
                principalTable: "Subjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActualDetails_Subjects_SubjectId",
                table: "ActualDetails");

            migrationBuilder.DropIndex(
                name: "IX_ActualDetails_BudgetBatchId_SubjectId_OrganizationId",
                table: "ActualDetails");

            migrationBuilder.DropIndex(
                name: "IX_ActualDetails_SubjectId",
                table: "ActualDetails");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "ActualDetails");
        }
    }
}
