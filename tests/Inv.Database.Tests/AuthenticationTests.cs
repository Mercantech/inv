using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Inv.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Inv.Database.Tests;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task Loan_testing_bypass_only_applies_in_development(bool development, bool bypass, bool allowed)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AUTH_ISSUER"] = "https://auth.mercantec.tech",
            ["AUTH_AUDIENCE"] = "mercantec-apps",
            ["JWKS_URI"] = "https://auth.mercantec.tech/.well-known/jwks.json",
            ["ALLOW_LOAN_TESTING_WITHOUT_ROLE"] = bypass.ToString()
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMercantecAuthentication(configuration, development);
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var ordinary = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test"));
        Assert.Equal(allowed, (await authorization.AuthorizeAsync(ordinary, null, "LoanManager")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(ordinary, null, "EquipmentManager")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, "LoanManager")).Succeeded);
        var manager = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "EquipmentManager")], "test"));
        Assert.True((await authorization.AuthorizeAsync(manager, null, "LoanManager")).Succeeded);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("issuer", false)]
    [InlineData("audience", false)]
    [InlineData("expired", false)]
    [InlineData("signature", false)]
    [InlineData("kid", false)]
    [InlineData("subject", false)]
    [InlineData("algorithm", false)]
    public async Task Bearer_validation_rejects_invalid_tokens(string scenario, bool expected)
    {
        using var rsa = RSA.Create(2048);
        using var otherRsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "trusted-key" };
        var signingKey = scenario == "signature" ? new RsaSecurityKey(otherRsa) { KeyId = "trusted-key" } : key;
        if (scenario == "kid") signingKey.KeyId = "unknown-key";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AUTH_ISSUER"] = "https://auth.mercantec.tech",
            ["AUTH_AUDIENCE"] = "mercantec-apps",
            ["JWKS_URI"] = "https://auth.mercantec.tech/.well-known/jwks.json"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMercantecAuthentication(configuration);
        var metadata = new OpenIdConnectConfiguration { Issuer = "https://auth.mercantec.tech" };
        metadata.SigningKeys.Add(new RsaSecurityKey(rsa) { KeyId = "trusted-key" });
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata));
        await using var provider = services.BuildServiceProvider();
        var subject = Guid.NewGuid().ToString();
        var token = new JwtSecurityToken(
            issuer: scenario == "issuer" ? "https://wrong.example" : "https://auth.mercantec.tech",
            audience: scenario == "audience" ? "wrong" : "mercantec-apps",
            claims: [new Claim("sub", scenario == "subject" ? "email@example.com" : subject), new Claim("role", "EquipmentManager")],
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: scenario == "expired" ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(signingKey, scenario == "algorithm" ? SecurityAlgorithms.RsaSha512 : SecurityAlgorithms.RsaSha256));
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers.Authorization = "Bearer " + new JwtSecurityTokenHandler().WriteToken(token);
        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(expected, result.Succeeded);
        if (!expected)
        {
            await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }
        if (expected)
        {
            Assert.Equal(subject, result.Principal!.FindFirst("sub")!.Value);
            var authorization = provider.GetRequiredService<IAuthorizationService>();
            Assert.True((await authorization.AuthorizeAsync(result.Principal, null, "EquipmentManager")).Succeeded);
            var adminUser = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", subject), new Claim("role", "Admin")],
                authenticationType: "Bearer",
                nameType: "name",
                roleType: "role"));
            Assert.True((await authorization.AuthorizeAsync(adminUser, null, "EquipmentManager")).Succeeded);
            var ordinaryUser = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", subject)],
                authenticationType: "Bearer",
                nameType: "name",
                roleType: "role"));
            Assert.False((await authorization.AuthorizeAsync(ordinaryUser, null, "EquipmentManager")).Succeeded);
            await context.ForbidAsync(JwtBearerDefaults.AuthenticationScheme);
            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }
    }
}
