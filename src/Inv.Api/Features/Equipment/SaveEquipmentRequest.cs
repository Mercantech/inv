using Inv.Domain.Entities;

namespace Inv.Api.Features.Equipment;

public sealed record SaveEquipmentRequest(
    string? Name,
    string? SerialNumber,
    Guid? EquipmentCategoryId,
    EquipmentCondition? Condition);