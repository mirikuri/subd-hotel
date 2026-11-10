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

public static class FormPosting
{
    public static string Token(string html) =>
        System.Text.RegularExpressions.Regex
            .Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

    // Открывает страницу (как браузер), берёт антифорджери-токен из её формы и отправляет POST
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string url, Dictionary<string, string> fields, string? postUrl = null)
    {
        var html = await client.GetStringAsync(url);
        fields["__RequestVerificationToken"] = Token(html);
        return await client.PostAsync(postUrl ?? url, new FormUrlEncodedContent(fields));
    }

    // POST без токена — для проверки 403: авторизация срабатывает раньше проверки токена
    public static Task<HttpResponseMessage> PostRawAsync(this HttpClient client, string url,
                                                         Dictionary<string, string>? fields = null) =>
        client.PostAsync(url, new FormUrlEncodedContent(fields ?? []));
}

// Короткие запросы к gosti_test из тестов
public static class Sql
{
    public static async Task<object?> ScalarAsync(string sql, params (string, object)[] ps)
    {
        await using var conn = await TestDb.Create().OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return await cmd.ExecuteScalarAsync();
    }

    public static async Task ExecAsync(string sql, params (string, object)[] ps)
    {
        await using var conn = await TestDb.Create().OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }
}
