namespace Inv.Api.Features.EquipmentCategories;
// De oplysninger, API'et sender tilbage til klienten.
// Vi sender kun ID og navn, så databaseentitetens relationer ikke medtages.
public sealed record EquipmentCategoryResponse(Guid Id, string Name);