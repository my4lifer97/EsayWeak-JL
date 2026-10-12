using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BarberSaas.Api.Services;

public class JwtService(IConfiguration config)
{
    // mustChangePassword should reflect the business's real Business.MustChangePassword value --
    // RequirePasswordChangeFilter reads this claim to block every BusinessOnly action except
    // AuthController.ChangePassword until it's cleared.
    // passwordHash is the business's current Business.PasswordHash -- the token carries a stamp of
    // it (pwd), and BlockDisabledBusinessFilter rejects the token once the password changes, so a
    // reset or change ends every other login.
    public string Generate(string id, string email, string name, string slug, string passwordHash, bool mustChangePassword = false)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, id),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Name, name),
            new("slug", slug),
            new(PasswordStampClaim, PasswordStamp(passwordHash)),
        };
        if (mustChangePassword) claims.Add(new Claim("mustChangePassword", "true"));
        return BuildToken(claims, DateTime.UtcNow.AddDays(30));
    }

    public const string PasswordStampClaim = "pwd";

    // Short fingerprint of the bcrypt hash (which has its own salt, so every new password gives a
    // new stamp). Not reversible and says nothing about the password itself.
    public static string PasswordStamp(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..16];

    // Same claim shape as Generate (so every existing BusinessOnly endpoint keeps working
    // unmodified against the target business's real id) plus an impersonatedBy marker and a much
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
