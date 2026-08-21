using Microsoft.AspNetCore.Mvc;
using WebApplication2.Models;

namespace WebApplication2.Controllers
{
    /// <summary>
    /// Monitoring dashboard: live patient vitals, bed occupancy, filters and
    /// bed allocation. Also exposes a JSON alerts endpoint that the page polls
    /// every few seconds to display the real-time alert feed.
    /// </summary>
    public class DashboardController : Controller
    {
        private readonly HospitalManager _hospital;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(HospitalManager hospital, ILogger<DashboardController> logger)
        {
            _hospital = hospital;
            _logger = logger;

            // OBSERVER pattern: subscribe to hospital events and log them.
            // (The alert feed itself reads the shared alert log.)
            _hospital.CriticalVitalAlert += (_, e) =>
                _logger.LogWarning("[{Type}] {Message}", e.Type, e.Message);
            _hospital.AllocationFailed += (_, e) =>
                _logger.LogWarning("[{Type}] {Message}", e.Type, e.Message);
            _hospital.SanitationOverdue += (_, e) =>
                _logger.LogInformation("[{Type}] {Message}", e.Type, e.Message);
            _hospital.BedAllocated += (_, e) =>
                _logger.LogInformation("[{Type}] {Message}", e.Type, e.Message);
        }

        // GET: /Dashboard  (optionally /Dashboard?filter=Critical)
        public IActionResult Index(string? filter)
        {
            ViewBag.Filter = filter ?? "All";
            return View(_hospital);
        }

        // GET: /Dashboard/Alerts - polled by JavaScript for the live feed.
        public JsonResult Alerts()
            => Json(_hospital.GetRecentAlerts(20).Select(a => new
            {
                a.Timestamp,
                type = a.Type.ToString(),
                a.Message,
                cssClass = a.CssClass
            }));

        // POST: /Dashboard/Allocate/{patientId} - auto-pick a suitable bed.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Allocate(Guid patientId)
        {
            _hospital.AllocateBed(patientId);
            return RedirectToAction(nameof(Index));
        }

        // POST: /Dashboard/AllocateTo/{patientId}?bedId=... - manual pick.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AllocateTo(Guid patientId, Guid bedId)
        {
            _hospital.AllocateBed(patientId, bedId);
            return RedirectToAction(nameof(Index));
        }

        // POST: /Dashboard/Deallocate/{patientId}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Deallocate(Guid patientId)
        {
            _hospital.DeallocatePatient(patientId);
            return RedirectToAction(nameof(Index));
        }
    }
}
