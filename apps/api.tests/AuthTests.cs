using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atalaya.Api.Services;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Auth de lecturas (AUD-015 D, SAD §6.1) sin Docker (WebApplicationFactory, modo <c>Auth:Dev</c>):
/// las lecturas REST y el hub exigen un JWT con rol operador/admin; la ingesta sigue gobernada por
/// el token de dispositivo, no por la auth de usuario. Los tokens se firman con el mismo emisor que
/// usa la API en dev (<see cref="DevTokenIssuer"/>), lo que permite además forjar un rol insuficiente.
/// </summary>
public sealed class AuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "test-only-hs256-signing-key-0123456789-abcdefgh";
    private readonly WebApplicationFactory<Program> _factory;

    public AuthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Telemetry:Transport", "InMemory");
            b.UseSetting("Ingest:Token", "");
            b.UseSetting("Auth:Mode", "Dev");
            b.UseSetting("Auth:Issuer", "atalaya-test");
            b.UseSetting("Auth:Audience", "atalaya");
            b.UseSetting("Auth:DevSigningKey", SigningKey);
        });
    }

    private static AuthOptions Options() => new()
    {
        Mode = "Dev",
        Issuer = "atalaya-test",
        Audience = "atalaya",
        DevSigningKey = SigningKey,
        TokenLifetimeMinutes = 60,
    };

    [Fact]
    public async Task Sin_token_las_lecturas_responden_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Con_token_operador_las_lecturas_responden_200()
    {
        var client = _factory.CreateClient();
        var (token, _) = DevTokenIssuer.Issue(Options(), "op-1", AuthExtensions.OperatorRole);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/devices");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Con_rol_insuficiente_las_lecturas_responden_403()
    {
        var client = _factory.CreateClient();
        // Token válido (firma/issuer/audience correctos) pero con un rol fuera de la read policy.
        var (token, _) = DevTokenIssuer.Issue(Options(), "viewer-1", "viewer");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/devices");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task El_endpoint_dev_token_emite_un_token_usable()
    {
        var client = _factory.CreateClient();
        var minted = await client.GetFromJsonAsync<DevTokenResponse>("/auth/dev-token?role=admin");
        Assert.NotNull(minted);
        Assert.Equal("admin", minted!.Role);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", minted.Token);
        var resp = await client.GetAsync("/api/alerts");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task La_ingesta_sigue_abierta_sin_token_de_usuario()
    {
        // /ingest se gobierna por el token de dispositivo (vacío aquí), no por la auth de usuario:
        // no debe exigir Bearer, o romperíamos el borde de ingesta.
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/ingest", Array.Empty<object>());
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
    }

    [Fact]
    public async Task El_hub_rechaza_la_negociacion_sin_token()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsync("/hubs/telemetry/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task El_hub_acepta_la_conexion_con_token_por_query_string()
    {
        var (token, _) = DevTokenIssuer.Issue(Options(), "op-1", AuthExtensions.OperatorRole);
        var handler = _factory.Server.CreateHandler();

        await using var hub = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/telemetry", o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => handler;
                o.AccessTokenProvider = () => Task.FromResult<string?>(token); // SignalR lo manda como ?access_token=
            })
            .Build();

        await hub.StartAsync();
        Assert.Equal(HubConnectionState.Connected, hub.State);
        await hub.StopAsync();
    }

    private sealed record DevTokenResponse(string Token, string Role, int ExpiresIn);
}
