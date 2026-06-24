namespace Atalaya.Api.Services;

/// <summary>
/// Configuración de la autenticación de lecturas (AUD-015 D, SAD §6.1). Tres modos, en el mismo
/// espíritu que el flag de transporte (ADR-011): <c>Disabled</c> (base/tests, sin auth),
/// <c>Dev</c> (emisor local HS256 vía <c>/auth/dev-token</c>, sin depender de una cuenta AWS) y
/// <c>Oidc</c> (valida contra un authority/JWKS real, p.ej. Cognito) cuando la cuenta esté lista.
/// El swap Dev→Oidc no toca el código de los endpoints: solo cambia cómo se valida la firma.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>Modo activo: <c>Disabled</c> | <c>Dev</c> | <c>Oidc</c>.</summary>
    public string Mode { get; set; } = "Disabled";

    /// <summary>Authority OIDC (modo Oidc): de aquí se descubre el JWKS para validar firmas.</summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Proyecto de Identity Platform / Firebase (modo Oidc). Si se fija, deriva el Authority
    /// (<c>https://securetoken.google.com/{projectId}</c>) y la Audience (<c>projectId</c>) sin
    /// configurarlos a mano. Swap a otro IdP = poner Authority/Audience explícitos en su lugar.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>Audience esperada en el token (claim <c>aud</c>).</summary>
    public string Audience { get; set; } = "atalaya";

    /// <summary>Issuer esperado (claim <c>iss</c>). En modo Dev lo emite la propia API.</summary>
    public string Issuer { get; set; } = "atalaya-dev";

    /// <summary>Clave simétrica HS256 del emisor dev. Solo modo Dev; nunca en producción.</summary>
    public string DevSigningKey { get; set; } = string.Empty;

    /// <summary>Vida del token dev, en minutos.</summary>
    public int TokenLifetimeMinutes { get; set; } = 480;

    public bool IsEnabled => !Mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
    public bool IsDev => Mode.Equals("Dev", StringComparison.OrdinalIgnoreCase);
    public bool IsOidc => Mode.Equals("Oidc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Authority efectivo: el explícito, o el de Identity Platform derivado del ProjectId.</summary>
    public string? EffectiveAuthority =>
        !string.IsNullOrWhiteSpace(Authority) ? Authority
        : !string.IsNullOrWhiteSpace(ProjectId) ? $"https://securetoken.google.com/{ProjectId}"
        : null;

    /// <summary>Audience efectiva: en Identity Platform es el ProjectId; si no, la Audience explícita.</summary>
    public string EffectiveAudience =>
        !string.IsNullOrWhiteSpace(ProjectId) ? ProjectId! : Audience;
}
