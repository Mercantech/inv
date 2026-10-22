using System.ComponentModel.DataAnnotations;

namespace Inv.Web.Models;

public sealed class CreateEquipmentCategoryModel
{
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage = "Navnet må ikke være tomt.")]
    [StringLength(
        150,
        ErrorMessage = "Navnet må højst indeholde 150 tegn.")]
    public string Name { get; set; } = string.Empty;
}