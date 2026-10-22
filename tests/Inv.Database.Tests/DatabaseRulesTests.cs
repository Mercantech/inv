using Inv.Domain.Entities;
using Inv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Inv.Database.Tests;

public sealed class DatabaseRulesTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Equipment_category_names_are_unique_ignoring_case_and_surrounding_spaces()
    {
        await using var database = postgres.CreateContext();
        var name = $"Skærme-{Guid.NewGuid():N}";
        database.EquipmentCategories.Add(new EquipmentCategory { Name = name });
        await database.SaveChangesAsync();

        database.EquipmentCategories.Add(new EquipmentCategory { Name = $" {name.ToUpperInvariant()} " });
        await AssertPostgresErrorAsync(PostgresErrorCodes.UniqueViolation, () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task Empty_category_name_is_rejected()
    {
        await using var database = postgres.CreateContext();
        database.EquipmentCategories.Add(new EquipmentCategory { Name = "   " });
        await AssertPostgresErrorAsync(PostgresErrorCodes.CheckViolation, () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task An_equipment_item_cannot_have_two_active_loans_from_separate_contexts()
    {
        await using var firstContext = postgres.CreateContext();
        var equipment = await CreateEquipmentAsync(firstContext);
        firstContext.Loans.Add(CreateLoan(equipment.Id));
        await firstContext.SaveChangesAsync();

        await using var secondContext = postgres.CreateContext();
        secondContext.Loans.Add(CreateLoan(equipment.Id));
        await AssertPostgresErrorAsync(PostgresErrorCodes.UniqueViolation, () => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task A_returned_item_can_be_loaned_again_without_losing_history_or_original_notes()
    {
        await using var database = postgres.CreateContext();
        var equipment = await CreateEquipmentAsync(database);
        var firstLoan = CreateLoan(equipment.Id);
        firstLoan.Note = "Udlånes med oplader";
        database.Loans.Add(firstLoan);
        await database.SaveChangesAsync();

        firstLoan.ReturnedAt = firstLoan.LoanedAt.AddHours(1);
        firstLoan.ReturnedBySubject = Guid.NewGuid();
        firstLoan.ReturnNote = "Oplader returneret";
        await database.SaveChangesAsync();

        var nextLoan = CreateLoan(equipment.Id);
        database.Loans.Add(nextLoan);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var history = await database.Loans.Where(loan => loan.EquipmentId == equipment.Id).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Single(history, loan => loan.ReturnedAt is null);
        var returnedLoan = Assert.Single(history, loan => loan.ReturnedAt is not null);
        Assert.Equal("Udlånes med oplader", returnedLoan.Note);
        Assert.Equal("Oplader returneret", returnedLoan.ReturnNote);
    }

    [Fact]
    public async Task Return_before_loan_time_is_rejected()
    {
        await using var database = postgres.CreateContext();
        var equipment = await CreateEquipmentAsync(database);
        var loan = CreateLoan(equipment.Id);
        loan.ReturnedAt = loan.LoanedAt.AddMinutes(-1);
        loan.ReturnedBySubject = Guid.NewGuid();
        database.Loans.Add(loan);
        await AssertPostgresErrorAsync(PostgresErrorCodes.CheckViolation, () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task Return_without_an_approver_is_rejected()
    {
        await using var database = postgres.CreateContext();
        var equipment = await CreateEquipmentAsync(database);
        var loan = CreateLoan(equipment.Id);
        loan.ReturnedAt = loan.LoanedAt.AddMinutes(1);
        database.Loans.Add(loan);
        await AssertPostgresErrorAsync(PostgresErrorCodes.CheckViolation, () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task Equipment_with_loan_history_cannot_be_deleted()
    {
        Guid equipmentId;
        await using (var database = postgres.CreateContext())
        {
            var equipment = await CreateEquipmentAsync(database);
            equipmentId = equipment.Id;
            database.Loans.Add(CreateLoan(equipmentId));
            await database.SaveChangesAsync();
        }

        await using var deletingContext = postgres.CreateContext();
        var storedEquipment = await deletingContext.Equipment.SingleAsync(equipment => equipment.Id == equipmentId);
        deletingContext.Remove(storedEquipment);
        await AssertPostgresErrorAsync(PostgresErrorCodes.ForeignKeyViolation, () => deletingContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Global_issue_category_names_are_unique_ignoring_case()
    {
        await using var database = postgres.CreateContext();
        var name = $"Defekt-{Guid.NewGuid():N}";
        database.IssueCategories.Add(new IssueCategory { Name = name });
        await database.SaveChangesAsync();
        database.IssueCategories.Add(new IssueCategory { Name = name.ToUpperInvariant() });
        await AssertPostgresErrorAsync(PostgresErrorCodes.UniqueViolation, () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task Issue_category_name_can_be_reused_in_a_different_scope_but_not_in_the_same_scope()
    {
        await using var database = postgres.CreateContext();
        var firstCategory = new EquipmentCategory { Name = $"Skærme-{Guid.NewGuid():N}" };
        var secondCategory = new EquipmentCategory { Name = $"Computere-{Guid.NewGuid():N}" };
        var name = $"Tænder ikke-{Guid.NewGuid():N}";
        database.AddRange(firstCategory, secondCategory);
        database.IssueCategories.AddRange(
            new IssueCategory { Name = name },
            new IssueCategory { Name = name, EquipmentCategoryId = firstCategory.Id },
            new IssueCategory { Name = name, EquipmentCategoryId = secondCategory.Id });
        await database.SaveChangesAsync();

        database.IssueCategories.Add(new IssueCategory { Name = name.ToUpperInvariant(), EquipmentCategoryId = firstCategory.Id });
        await AssertPostgresErrorAsync(PostgresErrorCodes.UniqueViolation, () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task Resolving_one_report_does_not_close_other_reports_on_the_same_equipment()
    {
        await using var database = postgres.CreateContext();
        var equipment = await CreateEquipmentAsync(database);
        var category = new IssueCategory { Name = $"Defekt-{Guid.NewGuid():N}" };
        database.Add(category);
        var firstReport = CreateIssue(equipment.Id, category.Id);
        var secondReport = CreateIssue(equipment.Id, category.Id);
        database.EquipmentIssues.AddRange(firstReport, secondReport);
        await database.SaveChangesAsync();

        firstReport.Status = EquipmentIssueStatus.Resolved;
        firstReport.ResolvedAt = firstReport.ReportedAt.AddMinutes(1);
        firstReport.ResolvedBySubject = Guid.NewGuid();
        firstReport.ResolutionNote = "Repareret";
        await database.SaveChangesAsync();

        Assert.Equal(1, await database.EquipmentIssues.CountAsync(issue =>
            issue.EquipmentId == equipment.Id && issue.Status != EquipmentIssueStatus.Resolved));
        Assert.Equal(2, await database.EquipmentIssues.CountAsync(issue => issue.EquipmentId == equipment.Id));
    }

    [Fact]
    public async Task Resolved_report_without_resolution_details_is_rejected()
    {
        await using var database = postgres.CreateContext();
        var equipment = await CreateEquipmentAsync(database);
        var category = new IssueCategory { Name = $"Defekt-{Guid.NewGuid():N}" };
        database.Add(category);
        var report = CreateIssue(equipment.Id, category.Id);
        report.Status = EquipmentIssueStatus.Resolved;
        database.EquipmentIssues.Add(report);
        await AssertPostgresErrorAsync(PostgresErrorCodes.CheckViolation, () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task Saving_changes_preserves_creation_time_and_updates_modification_time()
    {
        await using var database = postgres.CreateContext();
        var category = new EquipmentCategory { Name = $"Oprindelig-{Guid.NewGuid():N}" };
        database.Add(category);
        await database.SaveChangesAsync();
        var createdAt = category.CreatedAt;

        category.Name = $"Opdateret-{Guid.NewGuid():N}";
        category.CreatedAt = createdAt.AddYears(-1);
        await database.SaveChangesAsync();
        await database.Entry(category).ReloadAsync();

        // PostgreSQL stores microseconds; .NET timestamps have 100 ns precision.
        Assert.True((category.CreatedAt - createdAt).Duration() < TimeSpan.FromMicroseconds(1));
        Assert.True(category.UpdatedAt >= category.CreatedAt);
    }

    private static async Task<Equipment> CreateEquipmentAsync(InventoryDbContext database)
    {
        var category = new EquipmentCategory { Name = $"Udstyr-{Guid.NewGuid():N}" };
        var equipment = new Equipment
        {
            Name = "Testcomputer",
            SerialNumber = Guid.NewGuid().ToString("N"),
            Category = category,
            Condition = EquipmentCondition.Good
        };
        database.Add(equipment);
        await database.SaveChangesAsync();
        return equipment;
    }

    private static Loan CreateLoan(Guid equipmentId) => new()
    {
        EquipmentId = equipmentId,
        BorrowerSubject = Guid.NewGuid(),
        ApprovedBySubject = Guid.NewGuid()
    };

    private static EquipmentIssue CreateIssue(Guid equipmentId, Guid categoryId) => new()
    {
        EquipmentId = equipmentId,
        IssueCategoryId = categoryId,
        Description = "Skærmen virker ikke",
        ReportedBySubject = Guid.NewGuid()
    };

    private static async Task AssertPostgresErrorAsync(string sqlState, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgresException.SqlState);
    }
}
