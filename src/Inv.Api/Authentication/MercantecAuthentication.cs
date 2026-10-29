using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using System.IdentityModel.Tokens.Jwt;

namespace Inv.Api.Authentication;

internal static class MercantecAuthentication
{
    public static IServiceCollection AddMercantecAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var issuer = RequiredHttpsAddress(configuration, "AUTH_ISSUER");
        var jwksUri = RequiredHttpsAddress(configuration, "JWKS_URI");
        var audience = configuration["AUTH_AUDIENCE"];

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("AUTH_AUDIENCE skal være konfigureret.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = true;
            options.IncludeErrorDetails = false;
            options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                jwksUri,
                new JwksConfigurationRetriever(issuer),
                new HttpDocumentRetriever { RequireHttps = true });
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                TryAllIssuerSigningKeys = false,
                NameClaimType = "name",
                RoleClaimType = "role",
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var subject) || subject == Guid.Empty)
                    {
                        context.Fail("Token skal indeholde en gyldig brugeridentifikator i sub.");
                    }

                    var keyId = context.SecurityToken switch
                    {
                        JsonWebToken token => token.Kid,
                        JwtSecurityToken token => token.Header.Kid,
                        _ => null
                    };

                    if (string.IsNullOrWhiteSpace(keyId))
                    {
                        context.Fail("Token skal angive kid.");
                    }

                    return Task.CompletedTask;
                }
            };
        });

        var managerRoles = SplitList(configuration["EQUIPMENT_MANAGER_ROLE"] ?? "EquipmentManager,Admin");
        var managerSubjects = SplitList(configuration["EQUIPMENT_MANAGER_SUBJECTS"]);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy("EquipmentManager", policy => policy.RequireAssertion(context =>
            {
                if (managerRoles.Any(role => context.User.IsInRole(role)))
                {
                    return true;
                }

                if (managerSubjects.Count == 0)
                {
                    return false;
                }

                var subject = context.User.FindFirst("sub")?.Value;
                return !string.IsNullOrWhiteSpace(subject)
                    && managerSubjects.Contains(subject, StringComparer.OrdinalIgnoreCase);
            }));

        services.AddAuthorizationBuilder().AddPolicy("LoanManager", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.Combine(new AuthorizationPolicyBuilder().RequireAssertion(context =>
                managerRoles.Any(role => context.User.IsInRole(role))
                || managerSubjects.Contains(context.User.FindFirst("sub")?.Value ?? "", StringComparer.OrdinalIgnoreCase)).Build());
        });

        return services;
    }

    private static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string RequiredHttpsAddress(IConfiguration configuration, string key)
    {
        var value = configuration[key];

        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{key} skal være en absolut HTTPS-adresse.");
        }

        return value!;
    }
}
