using Microsoft.JSInterop;

namespace Inv.Web.Services;

/// <summary>Cacher om den aktuelle session har EquipmentManager/Admin-adgang.</summary>
public sealed class AdminAccess(MercantecAuthClient auth)
{
    private bool? isAdmin;

    public async Task<bool> IsAdminAsync()
    {
        if (isAdmin.HasValue) return isAdmin.Value;
        try
        {
            var response = await auth.GetAsync("manager-access");
            isAdmin = response.Status == 200;
        }
        catch (JSException)
        {
            isAdmin = false;
        }

        return isAdmin.Value;
    }

    public void Invalidate() => isAdmin = null;
}
