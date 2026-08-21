using Microsoft.AspNetCore.Mvc;
using WebApplication2.Models;

namespace WebApplication2.Controllers
{
    /// <summary>
    /// UI requirement: Add / update / remove beds, plus the staff action to
    /// mark a bed as sanitized (clears the 48-hour sanitation clock).
    /// </summary>
    public class BedsController : Controller
    {
        private readonly HospitalManager _hospital;

        public BedsController(HospitalManager hospital)
            => _hospital = hospital;

        // GET: /Beds
        public IActionResult Index()
            => View(_hospital.Beds.ToList());

        // GET: /Beds/Create
        public IActionResult Create() => View(new Bed());

        // POST: /Beds/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Bed bed)
        {
            if (!ModelState.IsValid) return View(bed);

            _hospital.AddBed(bed);
            TempData["Status"] = $"Bed '{bed.WardName}' added.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Beds/Edit/{id}
        public IActionResult Edit(Guid id)
        {
            var bed = _hospital.GetBed(id);
            return bed == null ? NotFound() : View(bed);
        }

        // POST: /Beds/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(Guid id, Bed bed)
        {
            if (id != bed.BedID) return BadRequest();
            if (!ModelState.IsValid) return View(bed);

            if (!_hospital.UpdateBed(bed))
            {
                ModelState.AddModelError(string.Empty,
                    "Cannot change this bed's type while a critical patient occupies it.");
                return View(bed);
            }

            TempData["Status"] = $"Bed '{bed.WardName}' updated.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Beds/Sanitize/{id}  - staff marks the bed as freshly cleaned.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Sanitize(Guid id)
        {
            _hospital.SanitizeBed(id);
            TempData["Status"] = "Bed marked as sanitized.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Beds/Delete/{id}
        public IActionResult Delete(Guid id)
        {
            var bed = _hospital.GetBed(id);
            return bed == null ? NotFound() : View(bed);
        }

        // POST: /Beds/Delete/{id}
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(Guid id)
        {
            var removed = _hospital.RemoveBed(id);
            TempData["Status"] = removed ? "Bed removed." : "Bed is occupied and cannot be removed.";
            return RedirectToAction(nameof(Index));
        }
    }
}
