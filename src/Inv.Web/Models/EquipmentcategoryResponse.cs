namespace Inv.Web.Models;


// Modtager kategoriens ID og navn fra API'et.
// Bruges både ved oprettelse og ved hentning af kategorilisten.
public sealed record EquipmentcategoryResponse(Guid Id, string Name);

