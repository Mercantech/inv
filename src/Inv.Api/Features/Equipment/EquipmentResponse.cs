using Inv.Domain.Entities;

namespace Inv.Api.Features.Equipment;

public sealed record EquipmentResponse(
    Guid Id,
    string Name,
    string SerialNumber,
    Guid EquipmentCategoryId,
    string CategoryName,
    EquipmentCondition Condition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ShortId = 0);