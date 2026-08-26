using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;
using WebApplication2.Models;

namespace WebApplication2.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly HospitalDbContext _db;

        public HomeController(ILogger<HomeController> logger, HospitalDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        public IActionResult Index()
        {
            // The dashboard is the landing page of the hospital system.
            return RedirectToAction("Index", "Dashboard");
        }

        public IActionResult Privacy()
        {
            return View();
        }

        /// <summary>
        /// TEMPORARY DEBUG ENDPOINT: tests DB connectivity + schema and returns
        /// the raw result/exception as plain text. Remove after fix.
        /// </summary>
        [HttpGet]
        public async Task<string> DbCheck()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Environment: {_db.Database.ProviderName}");
            sb.AppendLine($"CanConnect: {await _db.Database.CanConnectAsync()}");
            try
            {
                var conn = _db.Database.GetDbConnection();
                sb.AppendLine($"Server/Host: {conn.DataSource}");
                sb.AppendLine($"Database: {conn.Database}");
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema='public' ORDER BY table_name";
                using var reader = await cmd.ExecuteReaderAsync();
                sb.AppendLine("Tables in DB:");
                while (await reader.ReadAsync()) sb.AppendLine("  - " + reader.GetString(0));
                await reader.CloseAsync();

                foreach (var t in new[] { "Patients", "Beds", "Alerts" })
                {
                    cmd.CommandText = $"SELECT column_name, data_type FROM information_schema.columns WHERE table_name='{t}' ORDER BY ordinal_position";
                    using var r2 = await cmd.ExecuteReaderAsync();
                    sb.AppendLine($"Columns of {t}:");
                    while (await r2.ReadAsync()) sb.AppendLine($"    {r2.GetString(0)} ({r2.GetString(1)})");
                    await r2.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("DB ERROR: " + ex.Message);
                if (ex.InnerException != null) sb.AppendLine("INNER: " + ex.InnerException.Message);
            }
            return sb.ToString();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // Log the full exception so Render Logs show the real cause.
            var feature = HttpContext.Features.Get<IExceptionHandlerFeature>();
            if (feature?.Error != null)
            {
                _logger.LogError(feature.Error,
                    "Unhandled exception on {Path}. RequestId: {RequestId}. Type: {ExType}. Msg: {ExMsg}",
                    HttpContext.Request.Path,
                    Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    feature.Error.GetType().Name,
                    feature.Error.Message);
            }
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
