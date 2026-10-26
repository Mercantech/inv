using System.ComponentModel.DataAnnotations;

namespace Inv.Web.Models;

public sealed class EquipmentFormModel
{
    [Required(ErrorMessage = "Navnet må ikke være tomt.")]
    [StringLength(200, ErrorMessage = "Navnet må højst indeholde 200 tegn.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Serienummeret må ikke være tomt.")]
    [StringLength(200, ErrorMessage = "Serienummeret må højst indeholde 200 tegn.")]
    public string SerialNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vælg en kategori.")]
    public Guid? EquipmentCategoryId { get; set; }

    [Required(ErrorMessage = "Vælg en tilstand.")]
    [Range(0, 4, ErrorMessage = "Vælg en gyldig tilstand.")]
    public int? Condition { get; set; }
}