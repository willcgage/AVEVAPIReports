using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiReports.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reports");

            migrationBuilder.CreateTable(
                name: "CaptureDefinitions",
                schema: "reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    DisplayUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    StartTime = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EndTime = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Asset = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HideToolbar = table.Column<bool>(type: "bit", nullable: false),
                    HideTimebar = table.Column<bool>(type: "bit", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ViewportWidth = table.Column<int>(type: "int", nullable: false),
                    ViewportHeight = table.Column<int>(type: "int", nullable: false),
                    Landscape = table.Column<bool>(type: "bit", nullable: false),
                    SettleSeconds = table.Column<int>(type: "int", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    Cron = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EmailRecipients = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EmailSubject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AttachMaxMb = table.Column<int>(type: "int", nullable: false),
                    SharePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FileNamePattern = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaptureDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmtpSettings",
                schema: "reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    Security = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProtectedPassword = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmtpSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportRuns",
                schema: "reports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefinitionId = table.Column<int>(type: "int", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OutputPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OutputBytes = table.Column<long>(type: "bigint", nullable: true),
                    Warnings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportRuns_CaptureDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "reports",
                        principalTable: "CaptureDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaptureDefinitions_Name",
                schema: "reports",
                table: "CaptureDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportRuns_DefinitionId_StartedAt",
                schema: "reports",
                table: "ReportRuns",
                columns: new[] { "DefinitionId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportRuns",
                schema: "reports");

            migrationBuilder.DropTable(
                name: "SmtpSettings",
                schema: "reports");

            migrationBuilder.DropTable(
                name: "CaptureDefinitions",
                schema: "reports");
        }
    }
}
