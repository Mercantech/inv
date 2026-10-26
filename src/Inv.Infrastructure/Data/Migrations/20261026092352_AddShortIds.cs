using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inv.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShortIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "ShortIds",
                startValue: 100L);

            migrationBuilder.AddColumn<int>(
                name: "ShortId",
                table: "HardwareDevices",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('\"ShortIds\"')");

            migrationBuilder.AddColumn<int>(
                name: "ShortId",
                table: "Equipment",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('\"ShortIds\"')");

            migrationBuilder.CreateIndex(
                name: "IX_HardwareDevices_ShortId",
                table: "HardwareDevices",
                column: "ShortId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_ShortId",
                table: "Equipment",
                column: "ShortId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HardwareDevices_ShortId",
                table: "HardwareDevices");

            migrationBuilder.DropIndex(
                name: "IX_Equipment_ShortId",
                table: "Equipment");

            migrationBuilder.DropColumn(
                name: "ShortId",
                table: "HardwareDevices");

            migrationBuilder.DropColumn(
                name: "ShortId",
                table: "Equipment");

            migrationBuilder.DropSequence(
                name: "ShortIds");
        }
    }
}
