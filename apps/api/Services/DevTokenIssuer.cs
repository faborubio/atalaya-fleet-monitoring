using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Atalaya.Api.Services;

/// <summary>
/// Emisor de tokens de desarrollo (modo <c>Auth:Dev</c>). Firma HS256 con la clave simétrica de
/// configuración y emite los claims mínimos (<c>sub</c>, <c>role</c>, <c>name</c>). En producción
/// este rol lo cumple un IdP OIDC real (Cognito); aquí evita depender de una cuenta AWS para
/// demostrar la cadena de auth de extremo a extremo.
/// </summary>
public static class DevTokenIssuer
{
    public static (string Token, int ExpiresInSeconds) Issue(AuthOptions auth, string subject, string role)
    {
        if (string.IsNullOrWhiteSpace(auth.DevSigningKey))
            throw new InvalidOperationException("Auth:DevSigningKey es obligatorio en modo Dev.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.DevSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var lifetime = TimeSpan.FromMinutes(auth.TokenLifetimeMinutes);
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: auth.Issuer,
            audience: auth.Audience,
            claims:
            [
                new Claim("sub", subject),
                new Claim("role", role),
                new Claim("name", subject),
            ],
            notBefore: now,
            expires: now.Add(lifetime),
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), (int)lifetime.TotalSeconds);
    }
}
