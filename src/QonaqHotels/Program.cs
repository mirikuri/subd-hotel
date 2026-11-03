using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using QonaqHotels.Data;
using QonaqHotels.Models;
using QonaqHotels.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor Pages: protect all /Admin pages
builder.Services.AddRazorPages(options => {
    options.Conventions.AuthorizeFolder("/Admin");
});

// Database connection factory
builder.Services.AddScoped<Db>();

// Login services
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<GuestAuthService>();
builder.Services.AddScoped<PasswordHasher<User>>();
builder.Services.AddScoped<PasswordHasher<Guest>>();

// Cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.Cookie.Name         = "qonaq.auth";
        options.Cookie.HttpOnly     = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite     = SameSiteMode.Lax;
        options.SlidingExpiration   = true;
        options.ExpireTimeSpan      = TimeSpan.FromHours(8);
        options.LoginPath           = "/Login";
        options.AccessDeniedPath    = "/AccessDenied";
    });

// Role policies
builder.Services.AddAuthorization(options => {
    options.AddPolicy("RequireReceptionist", p => p.RequireRole(Roles.Reception, Roles.Admin));
    options.AddPolicy("RequireAdmin",        p => p.RequireRole(Roles.Admin));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var db     = scope.ServiceProvider.GetRequiredService<Db>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<User>>();
    await Seeder.SeedAsync(db, hasher, logger);
}

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error/500");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error/{0}");
app.UseHttpsRedirection();

// Security headers
app.Use(async (ctx, next) => {
    ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    ctx.Response.Headers.Append("X-Frame-Options", "DENY");
    ctx.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    if (ctx.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase))
        ctx.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();

public partial class Program { }
