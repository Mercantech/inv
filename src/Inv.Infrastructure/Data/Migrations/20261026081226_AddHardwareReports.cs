using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inv.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHardwareReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HardwareDevices",
                columns: table => new
                {
                    SamdId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mac = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EccSerial = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HardwareDevices", x => x.SamdId);
                    table.ForeignKey(
                        name: "FK_HardwareDevices_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HardwareTestReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SamdId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    PassedCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    TestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReportJson = table.Column<string>(type: "jsonb", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    ImportSource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LegacyReportId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HardwareTestReports", x => x.Id);
                    table.CheckConstraint("CK_HardwareTestReports_Counts", "\"PassedCount\" >= 0 AND \"FailedCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_HardwareTestReports_HardwareDevices_SamdId",
                        column: x => x.SamdId,
                        principalTable: "HardwareDevices",
                        principalColumn: "SamdId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HardwareDevices_EquipmentId",
                table: "HardwareDevices",
                column: "EquipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HardwareTestReports_ImportSource_LegacyReportId",
                table: "HardwareTestReports",
                columns: new[] { "ImportSource", "LegacyReportId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HardwareTestReports_SamdId_TestedAt",
                table: "HardwareTestReports",
                columns: new[] { "SamdId", "TestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HardwareTestReports");

            migrationBuilder.DropTable(
                name: "HardwareDevices");
        }
    }
}
