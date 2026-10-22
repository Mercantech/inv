using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inv.Infrastructure.Data.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20261022090000_InitialInventory")]
public partial class InitialInventory
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => InitialInventoryModel.Build(modelBuilder);
}

// Frozen schema for this migration. Do not reuse live entity configurations here:
// later migrations must compare the new model against this historical definition.
internal static class InitialInventoryModel
{
    public static void Build(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("Inv.Domain.Entities.EquipmentCategory", entity =>
        {
            Common(entity);
            entity.Property<string>("Name").IsRequired().HasMaxLength(150).HasColumnType("character varying(150)");
            entity.Property<string>("NormalizedName").IsRequired().ValueGeneratedOnAddOrUpdate()
                .HasColumnType("text").HasComputedColumnSql("lower(btrim(\"Name\"))", true);
            entity.HasIndex("NormalizedName").IsUnique();
            entity.ToTable("EquipmentCategories", table =>
                table.HasCheckConstraint("CK_EquipmentCategories_Name", "length(btrim(\"Name\")) > 0"));
        });

        modelBuilder.Entity("Inv.Domain.Entities.Equipment", entity =>
        {
            Common(entity);
            entity.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            entity.Property<string>("SerialNumber").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            entity.Property<Guid>("EquipmentCategoryId").HasColumnType("uuid");
            entity.Property<string>("Condition").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            entity.HasIndex("EquipmentCategoryId");
            entity.HasIndex("Name");
            entity.HasIndex("SerialNumber");
            entity.ToTable("Equipment", table =>
            {
                table.HasCheckConstraint("CK_Equipment_Name", "length(btrim(\"Name\")) > 0");
                table.HasCheckConstraint("CK_Equipment_SerialNumber", "length(btrim(\"SerialNumber\")) > 0");
                table.HasCheckConstraint("CK_Equipment_Condition", "\"Condition\" IN ('Unknown', 'Good', 'Worn', 'Damaged', 'OutOfService')");
            });
        });

        modelBuilder.Entity("Inv.Domain.Entities.Loan", entity =>
        {
            Common(entity);
            entity.Property<Guid>("EquipmentId").HasColumnType("uuid");
            entity.Property<Guid>("BorrowerSubject").HasColumnType("uuid");
            entity.Property<Guid>("ApprovedBySubject").HasColumnType("uuid");
            entity.Property<DateTimeOffset>("LoanedAt").HasColumnType("timestamp with time zone");
            entity.Property<string>("Note").HasMaxLength(4000).HasColumnType("character varying(4000)");
            entity.Property<DateTimeOffset?>("ReturnedAt").HasColumnType("timestamp with time zone");
            entity.Property<Guid?>("ReturnedBySubject").HasColumnType("uuid");
            entity.Property<string>("ReturnNote").HasMaxLength(4000).HasColumnType("character varying(4000)");
            entity.HasIndex("BorrowerSubject");
            entity.HasIndex("EquipmentId").IsUnique().HasDatabaseName("UX_Loans_ActiveEquipment").HasFilter("\"ReturnedAt\" IS NULL");
            entity.HasIndex("EquipmentId", "LoanedAt");
            entity.ToTable("Loans", table =>
            {
                table.HasCheckConstraint("CK_Loans_ReturnedAt", "\"ReturnedAt\" IS NULL OR \"ReturnedAt\" >= \"LoanedAt\"");
                table.HasCheckConstraint("CK_Loans_ReturnApproval", "(\"ReturnedAt\" IS NULL) = (\"ReturnedBySubject\" IS NULL)");
                table.HasCheckConstraint("CK_Loans_BorrowerSubject", "\"BorrowerSubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint("CK_Loans_ApprovedBySubject", "\"ApprovedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint("CK_Loans_ReturnedBySubject", "\"ReturnedBySubject\" IS NULL OR \"ReturnedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            });
        });

        modelBuilder.Entity("Inv.Domain.Entities.IssueCategory", entity =>
        {
            Common(entity);
            entity.Property<string>("Name").IsRequired().HasMaxLength(150).HasColumnType("character varying(150)");
            entity.Property<string>("NormalizedName").IsRequired().ValueGeneratedOnAddOrUpdate()
                .HasColumnType("text").HasComputedColumnSql("lower(btrim(\"Name\"))", true);
            entity.Property<Guid?>("EquipmentCategoryId").HasColumnType("uuid");
            entity.HasIndex("EquipmentCategoryId", "NormalizedName").IsUnique().HasFilter("\"EquipmentCategoryId\" IS NOT NULL");
            entity.HasIndex("NormalizedName").IsUnique().HasDatabaseName("UX_IssueCategories_GlobalName").HasFilter("\"EquipmentCategoryId\" IS NULL");
            entity.ToTable("IssueCategories", table =>
                table.HasCheckConstraint("CK_IssueCategories_Name", "length(btrim(\"Name\")) > 0"));
        });

        modelBuilder.Entity("Inv.Domain.Entities.EquipmentIssue", entity =>
        {
            Common(entity);
            entity.Property<Guid>("EquipmentId").HasColumnType("uuid");
            entity.Property<Guid>("IssueCategoryId").HasColumnType("uuid");
            entity.Property<string>("Description").IsRequired().HasMaxLength(4000).HasColumnType("character varying(4000)");
            entity.Property<Guid>("ReportedBySubject").HasColumnType("uuid");
            entity.Property<DateTimeOffset>("ReportedAt").HasColumnType("timestamp with time zone");
            entity.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            entity.Property<string>("ResolutionNote").HasMaxLength(4000).HasColumnType("character varying(4000)");
            entity.Property<DateTimeOffset?>("ResolvedAt").HasColumnType("timestamp with time zone");
            entity.Property<Guid?>("ResolvedBySubject").HasColumnType("uuid");
            entity.HasIndex("EquipmentId", "Status");
            entity.HasIndex("IssueCategoryId");
            entity.HasIndex("Status");
            entity.ToTable("EquipmentIssues", table =>
            {
                table.HasCheckConstraint("CK_EquipmentIssues_Description", "length(btrim(\"Description\")) > 0");
                table.HasCheckConstraint("CK_EquipmentIssues_Status", "\"Status\" IN ('Open', 'InProgress', 'Resolved')");
                table.HasCheckConstraint("CK_EquipmentIssues_Resolution", "(\"Status\" = 'Resolved' AND \"ResolvedAt\" IS NOT NULL AND \"ResolvedBySubject\" IS NOT NULL) OR (\"Status\" <> 'Resolved' AND \"ResolvedAt\" IS NULL AND \"ResolvedBySubject\" IS NULL)");
                table.HasCheckConstraint("CK_EquipmentIssues_ResolvedAt", "\"ResolvedAt\" IS NULL OR \"ResolvedAt\" >= \"ReportedAt\"");
                table.HasCheckConstraint("CK_EquipmentIssues_ReportedBySubject", "\"ReportedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint("CK_EquipmentIssues_ResolvedBySubject", "\"ResolvedBySubject\" IS NULL OR \"ResolvedBySubject\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            });
        });

        modelBuilder.Entity("Inv.Domain.Entities.Equipment", entity =>
        {
            entity.HasOne("Inv.Domain.Entities.EquipmentCategory", "Category").WithMany("Equipment")
                .HasForeignKey("EquipmentCategoryId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.Navigation("Category");
        });
        modelBuilder.Entity("Inv.Domain.Entities.Loan", entity =>
        {
            entity.HasOne("Inv.Domain.Entities.Equipment", "Equipment").WithMany("Loans")
                .HasForeignKey("EquipmentId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.Navigation("Equipment");
        });
        modelBuilder.Entity("Inv.Domain.Entities.IssueCategory", entity =>
        {
            entity.HasOne("Inv.Domain.Entities.EquipmentCategory", "EquipmentCategory").WithMany("IssueCategories")
                .HasForeignKey("EquipmentCategoryId").OnDelete(DeleteBehavior.Restrict);
            entity.Navigation("EquipmentCategory");
        });
        modelBuilder.Entity("Inv.Domain.Entities.EquipmentIssue", entity =>
        {
            entity.HasOne("Inv.Domain.Entities.Equipment", "Equipment").WithMany("Issues")
                .HasForeignKey("EquipmentId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne("Inv.Domain.Entities.IssueCategory", "Category").WithMany("Issues")
                .HasForeignKey("IssueCategoryId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.Navigation("Equipment");
            entity.Navigation("Category");
        });
        modelBuilder.Entity("Inv.Domain.Entities.EquipmentCategory", entity =>
        {
            entity.Navigation("Equipment");
            entity.Navigation("IssueCategories");
        });
        modelBuilder.Entity("Inv.Domain.Entities.Equipment", entity =>
        {
            entity.Navigation("Loans");
            entity.Navigation("Issues");
        });
        modelBuilder.Entity("Inv.Domain.Entities.IssueCategory", entity => entity.Navigation("Issues"));
    }

    private static void Common(EntityTypeBuilder entity)
    {
        entity.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
        entity.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
        entity.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
        entity.HasKey("Id");
    }
}
