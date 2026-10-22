namespace Inv.Api.Features.EquipmentCategories;

// De oplysninger, klienten sender for at oprette en udstyrskategori.
// Nullable gør det muligt at håndtere manglende navn med vores egen validering.
public sealed record CreateEquipmentCategoryRequest(string? Name);