using WebApplication2.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// SINGLETON pattern: one shared HospitalManager instance for the whole app,
// holding all in-memory patients, beds and alerts.
builder.Services.AddSingleton<HospitalManager>();

// Hosted service: runs the vitals monitoring loop in the background
// (web equivalent of a Timer-based monitor).
builder.Services.AddHostedService<VitalsMonitorService>();

var app = builder.Build();

// Seed sample data once at startup so the dashboard has content immediately.
app.Services.GetRequiredService<HospitalManager>().SeedSampleData();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
