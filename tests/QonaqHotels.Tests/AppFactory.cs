using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QonaqHotels.Tests;

// Приложение целиком поверх базы gosti_test.
// Вход подменяется схемой Test: роль берётся из заголовка X-Test-Role,
// id гостя — из X-Test-Guest. Без заголовков пользователь анонимный:
// нехватка входа → 401, нехватка роли → 403.
public class AppFactory : WebApplicationFactory<Program>
{
    public const string Scheme = "Test";

    static AppFactory()
    {
        // Seeder создаёт admin при пустой users и без пароля падает
        Environment.SetEnvironmentVariable("ADMIN_INITIAL_PASSWORD", "Test12345");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", TestDb.ConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestDb.ConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(Scheme, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme             = Scheme;
                o.DefaultAuthenticateScheme = Scheme;
                o.DefaultChallengeScheme    = Scheme;
                o.DefaultForbidScheme       = Scheme;
                o.DefaultSignInScheme       = Scheme;
            });
        });
    }

    public HttpClient Client(string? role, int? guestId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress       = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (role != null)    client.DefaultRequestHeaders.Add("X-Test-Role", Uri.EscapeDataString(role));
        if (guestId != null) client.DefaultRequestHeaders.Add("X-Test-Guest", guestId.Value.ToString());
        return client;
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
                           ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "Тест"),
            new(ClaimTypes.Role, Uri.UnescapeDataString(role.ToString()))
        };
        if (Request.Headers.TryGetValue("X-Test-Guest", out var guest))
            claims.Add(new Claim(ClaimTypes.NameIdentifier, "guest:" + guest));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AppFactory.Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AppFactory.Scheme)));
    }
}
