using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;
using QonaqHotels.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Razor Pages: protect all /Admin pages
builder.Services.AddRazorPages(options => {
    options.Conventions.AuthorizeFolder("/Admin");
});

// EF Core + PostgreSQL
builder.Services.AddDbContext<HotelDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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
    options.AddPolicy("RequireReceptionist", p => p.RequireRole("ресепшен", "админ"));
    options.AddPolicy("RequireAdmin",        p => p.RequireRole("админ"));
});

// Rate limiter: max 10 login POSTs per minute per IP
builder.Services.AddRateLimiter(o =>
    o.AddFixedWindowLimiter("login", opts => {
        opts.Window               = TimeSpan.FromMinutes(1);
        opts.PermitLimit          = 10;
        opts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opts.QueueLimit           = 0;
    }));

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var db     = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await Seeder.SeedAsync(db, logger);
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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();

public partial class Program { }
