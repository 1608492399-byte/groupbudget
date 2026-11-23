using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroupBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubjectDetails");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "FilingForms");

            migrationBuilder.RenameColumn(
                name: "Remarks",
                table: "FilingForms",
                newName: "ReturnReason");

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "ReviewRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BudgetBatchId",
                table: "FilingForms",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "FilingForms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "FilingForms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "FilingForms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedBy",
                table: "FilingForms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "FilingForms",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BudgetBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PlanYear = table.Column<int>(type: "INTEGER", nullable: false),
                    BaselineYear = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DeviationThreshold = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    CurrentBaselineVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PublishedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilingFormId = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    Comments = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Operator = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormVersions_FilingForms_FilingFormId",
                        column: x => x.FilingFormId,
                        principalTable: "FilingForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OperationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserName = table.Column<string>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsLeaf = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserOrganizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOrganizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserOrganizations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BaselineRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BudgetBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    BudgetBaseline = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ActualBaseline = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaselineRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BaselineRecords_BudgetBatches_BudgetBatchId",
                        column: x => x.BudgetBatchId,
                        principalTable: "BudgetBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BaselineRecords_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BaselineRecords_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BaselineSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BudgetBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    BudgetBaseline = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ActualBaseline = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SnapshotAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operator = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaselineSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BaselineSnapshots_BudgetBatches_BudgetBatchId",
                        column: x => x.BudgetBatchId,
                        principalTable: "BudgetBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BaselineSnapshots_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BaselineSnapshots_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilingFormId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Budget2026 = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Actual2026 = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Budget2027 = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BaselineVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportLines_FilingForms_FilingFormId",
                        column: x => x.FilingFormId,
                        principalTable: "FilingForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportLines_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FilingForms_BudgetBatchId_OrganizationId",
                table: "FilingForms",
                columns: new[] { "BudgetBatchId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaselineRecords_BudgetBatchId_OrganizationId_SubjectId_Version",
                table: "BaselineRecords",
                columns: new[] { "BudgetBatchId", "OrganizationId", "SubjectId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaselineRecords_OrganizationId",
                table: "BaselineRecords",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_BaselineRecords_SubjectId",
                table: "BaselineRecords",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BaselineSnapshots_BudgetBatchId_Version_OrganizationId_SubjectId",
                table: "BaselineSnapshots",
                columns: new[] { "BudgetBatchId", "Version", "OrganizationId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaselineSnapshots_OrganizationId",
                table: "BaselineSnapshots",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_BaselineSnapshots_SubjectId",
                table: "BaselineSnapshots",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_FormVersions_FilingFormId",
                table: "FormVersions",
                column: "FilingFormId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportLines_FilingFormId_SubjectId",
                table: "ReportLines",
                columns: new[] { "FilingFormId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportLines_SubjectId",
                table: "ReportLines",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Code",
                table: "Subjects",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserOrganizations_OrganizationId",
                table: "UserOrganizations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOrganizations_UserId",
                table: "UserOrganizations",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FilingForms_BudgetBatches_BudgetBatchId",
                table: "FilingForms",
                column: "BudgetBatchId",
                principalTable: "BudgetBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FilingForms_BudgetBatches_BudgetBatchId",
                table: "FilingForms");

            migrationBuilder.DropTable(
                name: "BaselineRecords");

            migrationBuilder.DropTable(
                name: "BaselineSnapshots");

            migrationBuilder.DropTable(
                name: "FormVersions");

            migrationBuilder.DropTable(
                name: "OperationLogs");

            migrationBuilder.DropTable(
                name: "ReportLines");

            migrationBuilder.DropTable(
                name: "UserOrganizations");

            migrationBuilder.DropTable(
                name: "BudgetBatches");

            migrationBuilder.DropTable(
                name: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_FilingForms_BudgetBatchId_OrganizationId",
                table: "FilingForms");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "ReviewRecords");

            migrationBuilder.DropColumn(
                name: "BudgetBatchId",
                table: "FilingForms");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "FilingForms");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "FilingForms");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "FilingForms");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "FilingForms");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "FilingForms");

            migrationBuilder.RenameColumn(
                name: "ReturnReason",
                table: "FilingForms",
                newName: "Remarks");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "FilingForms",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "SubjectDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilingFormId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SubjectName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubjectDetails_FilingForms_FilingFormId",
                        column: x => x.FilingFormId,
                        principalTable: "FilingForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubjectDetails_FilingFormId",
                table: "SubjectDetails",
                column: "FilingFormId");
        }
    }
}
