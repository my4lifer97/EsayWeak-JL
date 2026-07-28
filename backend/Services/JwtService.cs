using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BarberSaas.Api.Services;

public class JwtService(IConfiguration config)
{
    public string Generate(string id, string email, string name, string slug)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, id),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Name, name),
            new Claim("slug", slug),
        };
        return BuildToken(claims, DateTime.UtcNow.AddDays(30));
    }

    // Same claim shape as Generate (so every existing BarberOnly endpoint keeps working
    // unmodified against the target barber's real id) plus an impersonatedBy marker and a much
    // shorter expiry, since this is minted by a platform admin rather than a real login.
    public string GenerateImpersonation(string id, string email, string name, string slug, string impersonatedByAdminId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, id),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Name, name),
            new Claim("slug", slug),
            new Claim("impersonatedBy", impersonatedByAdminId),
        };
        return BuildToken(claims, DateTime.UtcNow.AddMinutes(60));
    }

    private string BuildToken(IEnumerable<Claim> claims, DateTime expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
