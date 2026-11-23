using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroupBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContactPerson = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ContactPhone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FilingForms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FormNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilingForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilingForms_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilingFormId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewerName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Comments = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewRecords_FilingForms_FilingFormId",
                        column: x => x.FilingFormId,
                        principalTable: "FilingForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubjectDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilingFormId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
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
                name: "IX_FilingForms_OrganizationId",
                table: "FilingForms",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRecords_FilingFormId",
                table: "ReviewRecords",
                column: "FilingFormId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectDetails_FilingFormId",
                table: "SubjectDetails",
                column: "FilingFormId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewRecords");

            migrationBuilder.DropTable(
                name: "SubjectDetails");

            migrationBuilder.DropTable(
                name: "FilingForms");

            migrationBuilder.DropTable(
                name: "Organizations");
        }
    }
}
