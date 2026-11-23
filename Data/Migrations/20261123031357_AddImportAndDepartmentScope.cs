using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroupBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImportAndDepartmentScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReportLines_FilingFormId_SubjectId",
                table: "ReportLines");

            migrationBuilder.AddColumn<string>(
                name: "ExpenseType",
                table: "ReportLines",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "基本支出");

            migrationBuilder.CreateTable(
                name: "BudgetImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    SheetName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TotalRows = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessRows = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedRows = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ErrorSummary = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubjectDepartmentScopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectDepartmentScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubjectDepartmentScopes_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubjectDepartmentScopes_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubjectMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountingSubjectName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpenseType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubjectMappings_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActualDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BudgetImportBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    BudgetBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountingPeriod = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    VoucherDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VoucherNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AccountingSubjectName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExpenseType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AccountAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AdjustmentAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SourceRowNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActualDetails_BudgetBatches_BudgetBatchId",
                        column: x => x.BudgetBatchId,
                        principalTable: "BudgetBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualDetails_BudgetImportBatches_BudgetImportBatchId",
                        column: x => x.BudgetImportBatchId,
                        principalTable: "BudgetImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualDetails_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportLines_FilingFormId_SubjectId_ExpenseType",
                table: "ReportLines",
                columns: new[] { "FilingFormId", "SubjectId", "ExpenseType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActualDetails_BudgetBatchId_OrganizationId_AccountingPeriod",
                table: "ActualDetails",
                columns: new[] { "BudgetBatchId", "OrganizationId", "AccountingPeriod" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualDetails_BudgetImportBatchId",
                table: "ActualDetails",
                column: "BudgetImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualDetails_OrganizationId",
                table: "ActualDetails",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetImportBatches_ImportedAt",
                table: "BudgetImportBatches",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectDepartmentScopes_OrganizationId",
                table: "SubjectDepartmentScopes",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectDepartmentScopes_SubjectId_OrganizationId",
                table: "SubjectDepartmentScopes",
                columns: new[] { "SubjectId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubjectMappings_AccountingSubjectName",
                table: "SubjectMappings",
                column: "AccountingSubjectName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubjectMappings_SubjectId",
                table: "SubjectMappings",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActualDetails");

            migrationBuilder.DropTable(
                name: "SubjectDepartmentScopes");

            migrationBuilder.DropTable(
                name: "SubjectMappings");

            migrationBuilder.DropTable(
                name: "BudgetImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_ReportLines_FilingFormId_SubjectId_ExpenseType",
                table: "ReportLines");

            migrationBuilder.DropColumn(
                name: "ExpenseType",
                table: "ReportLines");

            migrationBuilder.CreateIndex(
                name: "IX_ReportLines_FilingFormId_SubjectId",
                table: "ReportLines",
                columns: new[] { "FilingFormId", "SubjectId" },
                unique: true);
        }
    }
}
