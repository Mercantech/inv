using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inv.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceEquipmentConditionWithFaultCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Equipment_Condition",
                table: "Equipment");

            migrationBuilder.DropColumn(
                name: "Condition",
                table: "Equipment");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Condition",
                table: "Equipment",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Equipment_Condition",
                table: "Equipment",
                sql: "\"Condition\" IN ('Unknown', 'Good', 'Worn', 'Damaged', 'OutOfService')");
        }
    }
}
