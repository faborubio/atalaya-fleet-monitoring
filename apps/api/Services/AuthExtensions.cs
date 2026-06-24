using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Atalaya.Api.Services;

/// <summary>
/// Registro de la autenticación/autorización de lecturas (AUD-015 D, SAD §6.1). JWT Bearer con
/// validación según el modo (Dev = HS256 local; Oidc = JWKS del authority). RBAC operador/admin:
/// las lecturas exigen la política <see cref="ReadPolicy"/> (rol operador o admin) y se reserva
/// <see cref="AdminPolicy"/> para acciones futuras. Cuando el modo es <c>Disabled</c> no se
/// registra nada (base/tests sin auth), igual que el token de ingesta vacío.
/// </summary>
public static class AuthExtensions
{
    public const string ReadPolicy = "read";
    public const string AdminPolicy = "admin";
    public const string OperatorRole = "operador";
    public const string AdminRole = "admin";

    public static IServiceCollection AddAtalayaAuth(this IServiceCollection services, AuthOptions auth)
    {
        if (!auth.IsEnabled) return services;

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // No remapear "role"/"sub" a las URIs largas de ClaimTypes: trabajamos con los
                // nombres cortos del JWT, que es como llegan también desde un IdP OIDC.
                options.MapInboundClaims = false;

                if (auth.IsOidc)
                {
                    // Validación contra un IdP real: el authority publica el JWKS (firmas rotables).
                    options.Authority = auth.Authority;
                    options.Audience = auth.Audience;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidAudience = auth.Audience,
                        RoleClaimType = "role",
                        NameClaimType = "sub",
                    };
                }
                else
                {
                    // Modo Dev: la propia API emite y valida tokens HS256 con una clave simétrica.
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = auth.Issuer,
                        ValidateAudience = true,
                        ValidAudience = auth.Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(RequireDevKey(auth))),
                        ValidateLifetime = true,
                        RoleClaimType = "role",
                        NameClaimType = "sub",
                        ClockSkew = TimeSpan.FromSeconds(30),
                    };
                }

                // El WebSocket del hub no puede mandar la cabecera Authorization: SignalR pasa el
                // token por query string (?access_token=). Lo recogemos solo para rutas del hub.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) &&
                            ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ReadPolicy, p => p
                .RequireAuthenticatedUser()
                .RequireRole(OperatorRole, AdminRole));
            options.AddPolicy(AdminPolicy, p => p
                .RequireAuthenticatedUser()
                .RequireRole(AdminRole));
        });

        return services;
    }

    private static string RequireDevKey(AuthOptions auth) =>
        !string.IsNullOrWhiteSpace(auth.DevSigningKey)
            ? auth.DevSigningKey
            : throw new InvalidOperationException("Auth:DevSigningKey es obligatorio en modo Dev.");
}
