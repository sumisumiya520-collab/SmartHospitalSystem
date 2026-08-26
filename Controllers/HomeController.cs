using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using WebApplication2.Models;

namespace WebApplication2.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
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

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // Log the full exception so Render Logs show the real cause.
            var feature = HttpContext.Features.Get<IExceptionHandlerFeature>();
            if (feature?.Error != null)
            {
                _logger.LogError(feature.Error,
                    "Unhandled exception on {Path}. RequestId: {RequestId}",
                    HttpContext.Request.Path,
                    Activity.Current?.Id ?? HttpContext.TraceIdentifier);
            }
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
