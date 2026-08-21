using Microsoft.AspNetCore.Mvc;
using WebApplication2.Models;

namespace WebApplication2.Controllers
{
    /// <summary>
    /// UI requirement: Add / update / remove patients.
    /// All business state lives in the injected HospitalManager singleton.
    /// </summary>
    public class PatientsController : Controller
    {
        private readonly HospitalManager _hospital;

        public PatientsController(HospitalManager hospital)
            => _hospital = hospital;

        // GET: /Patients
        public IActionResult Index()
            => View(_hospital.Patients.ToList());

        // GET: /Patients/Details/{id}
        public IActionResult Details(Guid id)
        {
            var patient = _hospital.GetPatient(id);
            return patient == null ? NotFound() : View(patient);
        }

        // GET: /Patients/Create
        public IActionResult Create() => View(new Patient());

        // POST: /Patients/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Patient patient)
        {
            if (!ModelState.IsValid) return View(patient);

            _hospital.AddPatient(patient);
            TempData["Status"] = $"Patient '{patient.PatientName}' added.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Patients/Edit/{id}
        public IActionResult Edit(Guid id)
        {
            var patient = _hospital.GetPatient(id);
            return patient == null ? NotFound() : View(patient);
        }

        // POST: /Patients/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(Guid id, Patient patient)
        {
            if (id != patient.PatientID) return BadRequest();
            if (!ModelState.IsValid) return View(patient);

            if (!_hospital.UpdatePatient(patient))
                return NotFound();

            TempData["Status"] = $"Patient '{patient.PatientName}' updated.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Patients/Delete/{id}
        public IActionResult Delete(Guid id)
        {
            var patient = _hospital.GetPatient(id);
            return patient == null ? NotFound() : View(patient);
        }

        // POST: /Patients/Delete/{id}
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(Guid id)
        {
            _hospital.RemovePatient(id);
            TempData["Status"] = "Patient removed.";
            return RedirectToAction(nameof(Index));
        }
    }
}
