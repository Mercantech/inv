using Microsoft.EntityFrameworkCore.Migrations;

namespace Inv.Infrastructure.Data.Migrations;

public partial class InitialInventory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EquipmentCategories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                NormalizedName = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(\"Name\"))", stored: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EquipmentCategories", entity => entity.Id);
                table.CheckConstraint("CK_EquipmentCategories_Name", "length(btrim(\"Name\")) > 0");
            });

        migrationBuilder.CreateTable(
            name: "Equipment",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SerialNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                EquipmentCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                Condition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Equipment", entity => entity.Id);
                table.CheckConstraint("CK_Equipment_Name", "length(btrim(\"Name\")) > 0");
                table.CheckConstraint("CK_Equipment_SerialNumber", "length(btrim(\"SerialNumber\")) > 0");
                table.CheckConstraint("CK_Equipment_Condition", "\"Condition\" IN ('Unknown', 'Good', 'Worn', 'Damaged', 'OutOfService')");
                table.ForeignKey("FK_Equipment_EquipmentCategories_EquipmentCategoryId", entity => entity.EquipmentCategoryId,
                    "EquipmentCategories", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "IssueCategories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                NormalizedName = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(\"Name\"))", stored: true),
                EquipmentCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IssueCategories", entity => entity.Id);
                table.CheckConstraint("CK_IssueCategories_Name", "length(btrim(\"Name\")) > 0");
                table.ForeignKey("FK_IssueCategories_EquipmentCategories_EquipmentCategoryId", entity => entity.EquipmentCategoryId,
                    "EquipmentCategories", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Loans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                BorrowerSubject = table.Column<Guid>(type: "uuid", nullable: false),
                ApprovedBySubject = table.Column<Guid>(type: "uuid", nullable: false),
                LoanedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                ReturnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ReturnedBySubject = table.Column<Guid>(type: "uuid", nullable: true),
                ReturnNote = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Loans", entity => entity.Id);
                table.CheckConstraint("CK_Loans_ReturnedAt", "\"ReturnedAt\" IS NULL OR \"ReturnedAt\" >= \"LoanedAt\"");
                table.CheckConstraint("CK_Loans_ReturnApproval", "(\"ReturnedAt\" IS NULL) = (\"ReturnedBySubject\" IS NULL)");
                table.CheckConstraint("CK_Loans_BorrowerSubject", "\"BorrowerSubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("CK_Loans_ApprovedBySubject", "\"ApprovedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("CK_Loans_ReturnedBySubject", "\"ReturnedBySubject\" IS NULL OR \"ReturnedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.ForeignKey("FK_Loans_Equipment_EquipmentId", entity => entity.EquipmentId,
                    "Equipment", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "EquipmentIssues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                IssueCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                ReportedBySubject = table.Column<Guid>(type: "uuid", nullable: false),
                ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                ResolutionNote = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ResolvedBySubject = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EquipmentIssues", entity => entity.Id);
                table.CheckConstraint("CK_EquipmentIssues_Description", "length(btrim(\"Description\")) > 0");
                table.CheckConstraint("CK_EquipmentIssues_Status", "\"Status\" IN ('Open', 'InProgress', 'Resolved')");
                table.CheckConstraint("CK_EquipmentIssues_Resolution", "(\"Status\" = 'Resolved' AND \"ResolvedAt\" IS NOT NULL AND \"ResolvedBySubject\" IS NOT NULL) OR (\"Status\" <> 'Resolved' AND \"ResolvedAt\" IS NULL AND \"ResolvedBySubject\" IS NULL)");
                table.CheckConstraint("CK_EquipmentIssues_ResolvedAt", "\"ResolvedAt\" IS NULL OR \"ResolvedAt\" >= \"ReportedAt\"");
                table.CheckConstraint("CK_EquipmentIssues_ReportedBySubject", "\"ReportedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("CK_EquipmentIssues_ResolvedBySubject", "\"ResolvedBySubject\" IS NULL OR \"ResolvedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.ForeignKey("FK_EquipmentIssues_Equipment_EquipmentId", entity => entity.EquipmentId,
                    "Equipment", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EquipmentIssues_IssueCategories_IssueCategoryId", entity => entity.IssueCategoryId,
                    "IssueCategories", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_EquipmentCategories_NormalizedName", "EquipmentCategories", "NormalizedName", unique: true);
        migrationBuilder.CreateIndex("IX_Equipment_EquipmentCategoryId", "Equipment", "EquipmentCategoryId");
        migrationBuilder.CreateIndex("IX_Equipment_Name", "Equipment", "Name");
        migrationBuilder.CreateIndex("IX_Equipment_SerialNumber", "Equipment", "SerialNumber");
        migrationBuilder.CreateIndex("IX_IssueCategories_EquipmentCategoryId_NormalizedName", "IssueCategories",
            ["EquipmentCategoryId", "NormalizedName"], unique: true, filter: "\"EquipmentCategoryId\" IS NOT NULL");
        migrationBuilder.CreateIndex("UX_IssueCategories_GlobalName", "IssueCategories", "NormalizedName",
            unique: true, filter: "\"EquipmentCategoryId\" IS NULL");
        migrationBuilder.CreateIndex("IX_Loans_BorrowerSubject", "Loans", "BorrowerSubject");
        migrationBuilder.CreateIndex("UX_Loans_ActiveEquipment", "Loans", "EquipmentId",
            unique: true, filter: "\"ReturnedAt\" IS NULL");
        migrationBuilder.CreateIndex("IX_Loans_EquipmentId_LoanedAt", "Loans", ["EquipmentId", "LoanedAt"]);
        migrationBuilder.CreateIndex("IX_EquipmentIssues_EquipmentId_Status", "EquipmentIssues", ["EquipmentId", "Status"]);
        migrationBuilder.CreateIndex("IX_EquipmentIssues_IssueCategoryId", "EquipmentIssues", "IssueCategoryId");
        migrationBuilder.CreateIndex("IX_EquipmentIssues_Status", "EquipmentIssues", "Status");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("EquipmentIssues");
        migrationBuilder.DropTable("Loans");
        migrationBuilder.DropTable("IssueCategories");
        migrationBuilder.DropTable("Equipment");
        migrationBuilder.DropTable("EquipmentCategories");
    }
}
