namespace Inv.Api.Features.Equipment;

public sealed record EquipmentResponse(
    Guid Id,
    string Name,
    string SerialNumber,
    Guid EquipmentCategoryId,
    string CategoryName,
    int FaultCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ShortId = 0,
    bool IsLoaned = false);
