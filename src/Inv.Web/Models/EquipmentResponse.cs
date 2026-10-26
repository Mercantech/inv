namespace Inv.Web.Models;

public sealed record EquipmentResponse(
    Guid Id,
    string Name,
    string SerialNumber,
    Guid EquipmentCategoryId,
    string CategoryName,
    int Condition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ShortId = 0,
    bool IsLoaned = false);
