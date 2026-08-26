using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Add services to the container.
builder.Services.AddControllersWithViews();

// Database: Neon pooled PostgreSQL (Render) via DATABASE_URL, fallback to LocalDB for local dev.
var dbUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(dbUrl))
{
    // Neon pooled URL format: postgres://user:pass@ep-xxx-pooler.neon.tech:5432/db?sslmode=require
    // Handle both postgres:// and postgresql:// schemes, and URL-encoded credentials.
    var uri = new Uri(dbUrl);
    var userInfo = uri.UserInfo.Split(':', 2);
    var dbPort = uri.Port > 0 ? uri.Port : 5432;
    var username = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
    var database = uri.AbsolutePath.TrimStart('/');
    // Handle query string (e.g., ?sslmode=require) - strip it from database name
    var queryIndex = database.IndexOf('?');
    if (queryIndex >= 0) database = database.Substring(0, queryIndex);
    var connStr = $"Host={uri.Host};Port={dbPort};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    builder.Services.AddDbContext<HospitalDbContext>(options => options.UseNpgsql(connStr));
}
else
{
    builder.Services.AddDbContext<HospitalDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("HospitalDb")));
}

// HospitalManager is now scoped (per-request) because it depends on scoped DbContext.
// VitalsMonitorService (singleton) will create scope per tick via IServiceScopeFactory.
builder.Services.AddScoped<HospitalManager>();
builder.Services.AddHostedService<VitalsMonitorService>();

var app = builder.Build();

// Create database and seed sample data on first run (supports both LocalDB and Neon pooled).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
    db.Database.EnsureCreated();
    var manager = scope.ServiceProvider.GetRequiredService<HospitalManager>();
    manager.SeedSampleData();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
