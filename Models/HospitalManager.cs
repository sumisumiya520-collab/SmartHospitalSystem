using Microsoft.EntityFrameworkCore;

namespace WebApplication2.Models
{
    /// <summary>
    /// Central business service of the hospital system (WebApplication2 DB-based).
    /// Now uses HospitalDbContext (EF Core) with Neon pooled PostgreSQL (Render) or LocalDB (dev).
    /// Supports dual provider, EnsureCreated, and persistent Alerts.
    /// Design patterns: Scoped service (per-request) + Observer via events.
    /// </summary>
    public class HospitalManager
    {
        private readonly HospitalDbContext _db;

        // Keep feed bounded (100 latest) for dashboard performance.
        private const int MaxAlerts = 100;

        public HospitalManager(HospitalDbContext db)
        {
            _db = db;
        }

        public IReadOnlyList<Patient> Patients => _db.Patients.ToList();
        public IReadOnlyList<Bed> Beds => _db.Beds.ToList();
        public IReadOnlyList<AlertEntry> Alerts => _db.Alerts.OrderByDescending(a => a.Timestamp).ToList();

        // ---- Events (Observer pattern) ------------------------------------------
        /// <summary>Raised when any patient's vital crosses a safety threshold.</summary>
        public event EventHandler<AlertEntry>? CriticalVitalAlert;

        /// <summary>Raised after a successful bed allocation.</summary>
        public event EventHandler<AlertEntry>? BedAllocated;

        /// <summary>Raised when an allocation attempt is rejected by a rule.</summary>
        public event EventHandler<AlertEntry>? AllocationFailed;

        /// <summary>Raised when a bed passes the 48h sanitation deadline.</summary>
        public event EventHandler<AlertEntry>? SanitationOverdue;

        // ========================================================================
        //  Patient management
        // ========================================================================

        public void AddPatient(Patient patient)
        {
            ArgumentNullException.ThrowIfNull(patient);
            patient.EvaluateCriticalStatus();
            _db.Patients.Add(patient);
            _db.SaveChanges();
        }

        public Patient? GetPatient(Guid id)
        {
            return _db.Patients.FirstOrDefault(p => p.PatientID == id);
        }

        public bool UpdatePatient(Patient updated)
        {
            var existing = _db.Patients.FirstOrDefault(p => p.PatientID == updated.PatientID);
            if (existing == null) return false;

            existing.PatientName = updated.PatientName;
            existing.Age = updated.Age;
            existing.DiseaseCategory = updated.DiseaseCategory;
            existing.UpdateVitals(updated.Temperature, updated.PulseRate, updated.OxygenLevel);

            // Safety rule: a critical patient may never stay in a General ward.
            if (existing.IsCritical && existing.AssignedBedID.HasValue)
            {
                var bed = _db.Beds.FirstOrDefault(b => b.BedID == existing.AssignedBedID.Value);
                if (bed is { BedType: BedType.General })
                {
                    DeallocateInternal(existing, reason: "Patient became critical - moved out of General Ward.");
                }
            }

            _db.SaveChanges();
            return true;
        }

        public bool RemovePatient(Guid id)
        {
            var patient = _db.Patients.FirstOrDefault(p => p.PatientID == id);
            if (patient == null) return false;

            if (patient.AssignedBedID.HasValue)
                DeallocateInternal(patient, reason: "Patient removed - bed released.");

            _db.Patients.Remove(patient);
            _db.SaveChanges();
            return true;
        }

        // ========================================================================
        //  Bed management
        // ========================================================================

        public void AddBed(Bed bed)
        {
            ArgumentNullException.ThrowIfNull(bed);
            if (bed.LastSanitized == default) bed.MarkSanitized();
            _db.Beds.Add(bed);
            _db.SaveChanges();
        }

        public Bed? GetBed(Guid id)
        {
            return _db.Beds.FirstOrDefault(b => b.BedID == id);
        }

        public bool UpdateBed(Bed updated)
        {
            var existing = _db.Beds.FirstOrDefault(b => b.BedID == updated.BedID);
            if (existing == null) return false;

            // Rule check: cannot change an occupied ICU bed to General while
            // a critical patient sits in it.
            if (existing.IsOccupied && updated.BedType == BedType.General && existing.BedType == BedType.ICU)
            {
                Raise(AllocationFailed, AlertType.AllocationFailed,
                    $"Bed {existing.WardName} type change rejected: occupied by a critical patient.");
                return false;
            }

            existing.WardName = updated.WardName;
            existing.BedType = updated.BedType;
            _db.SaveChanges();
            return true;
        }

        public bool RemoveBed(Guid id)
        {
            var bed = _db.Beds.FirstOrDefault(b => b.BedID == id);
            if (bed == null) return false;
            if (bed.IsOccupied)
            {
                Raise(AllocationFailed, AlertType.AllocationFailed,
                    $"Cannot remove bed '{bed.WardName}' - currently occupied.");
                return false;
            }
            _db.Beds.Remove(bed);
            _db.SaveChanges();
            return true;
        }

        /// <summary>Staff action: mark a bed as sanitized right now.</summary>
        public bool SanitizeBed(Guid id)
        {
            var bed = _db.Beds.FirstOrDefault(b => b.BedID == id);
            if (bed == null) return false;
            bed.MarkSanitized();
            _db.SaveChanges();
            Raise(BedAllocated, AlertType.BedAllocated,
                $"Bed '{bed.WardName}' ({bed.BedType}) sanitized - ready for allocation.");
            return true;
        }

        // ========================================================================
        //  Bed allocation engine
        // ========================================================================

        /// <summary>
        /// Attempts to allocate a free, freshly-sanitized bed to a patient.
        /// Safety rules enforced (per assignment constraints):
        ///   1. A bed hosts at most one patient.
        ///   2. Bed must be sanitized within the last 48 hours.
        ///   3. ICU beds only for critical patients.
        ///   4. Critical patients never go to the General Ward.
        /// Returns true on success; on failure an AllocationFailed alert explains why.
        /// </summary>
        public bool AllocateBed(Guid patientId, Guid? requestedBedId = null)
        {
            var patient = _db.Patients.FirstOrDefault(p => p.PatientID == patientId);
            if (patient == null)
            {
                Raise(AllocationFailed, AlertType.AllocationFailed, "Allocation failed: patient not found.");
                return false;
            }

            if (patient.AssignedBedID.HasValue)
            {
                Raise(AllocationFailed, AlertType.AllocationFailed,
                    $"{patient.PatientName} is already allocated to a bed.");
                return false;
            }

            // Candidate beds: free + sanitized within 48 hours (client-side check for IsSanitationOverdue).
            var candidates = _db.Beds.Where(b => !b.IsOccupied).ToList().Where(b => b.IsAssignable());

            // Optional manual pick from the UI must still satisfy every rule.
            if (requestedBedId.HasValue)
                candidates = candidates.Where(b => b.BedID == requestedBedId.Value);
            else
                candidates = candidates.Where(b =>
                    patient.IsCritical ? b.BedType == BedType.ICU : b.BedType == BedType.General);

            var bed = candidates.OrderBy(b => b.WardName).FirstOrDefault();
            if (bed == null)
            {
                string why = patient.IsCritical
                    ? "no free ICU bed available (or sanitation overdue)."
                    : "no free General bed available (or sanitation overdue).";
                Raise(AllocationFailed, AlertType.AllocationFailed,
                    $"Allocation failed for {patient.PatientName}: {why}");
                return false;
            }

            // Rule 3 & 4: bed type must match criticality.
            if (patient.IsCritical && bed.BedType != BedType.ICU)
            {
                Raise(AllocationFailed, AlertType.AllocationFailed,
                    $"Rejected: critical patient {patient.PatientName} cannot be placed in General Ward '{bed.WardName}'.");
                return false;
            }
            if (!patient.IsCritical && bed.BedType == BedType.ICU)
            {
                Raise(AllocationFailed, AlertType.AllocationFailed,
                    $"Rejected: ICU bed '{bed.WardName}' reserved for critical patients only.");
                return false;
            }

            // Commit the allocation atomically.
            bed.IsOccupied = true;
            patient.AssignedBedID = bed.BedID;
            _db.SaveChanges();

            Raise(BedAllocated, AlertType.BedAllocated,
                $"{patient.PatientName} allocated to {bed.BedType} bed '{bed.WardName}'.");
            return true;
        }

        /// <summary>Public deallocate used by the dashboard (discharge / move).</summary>
        public bool DeallocatePatient(Guid patientId, string reason = "Patient deallocated by staff.")
        {
            var patient = _db.Patients.FirstOrDefault(p => p.PatientID == patientId);
            if (patient == null || !patient.AssignedBedID.HasValue) return false;
            DeallocateInternal(patient, reason);
            _db.SaveChanges();
            return true;
        }

        private void DeallocateInternal(Patient patient, string reason)
        {
            var bed = _db.Beds.FirstOrDefault(b => b.BedID == patient.AssignedBedID!.Value);
            if (bed != null)
            {
                bed.IsOccupied = false;
                Raise(BedAllocated, AlertType.BedAllocated,
                    $"Bed '{bed.WardName}' released. {reason}");
            }
            patient.AssignedBedID = null;
        }

        // ========================================================================
        //  Monitoring support (called by VitalsMonitorService)
        // ========================================================================

        /// <summary>
        /// Applies new simulated vitals and raises a doctor alert when the
        /// patient transitions into the critical zone (OxygenLevel below 92%).
        /// </summary>
        internal void ApplyVitals(Guid patientId, double temperature, int pulse, double oxygen)
        {
            var patient = _db.Patients.FirstOrDefault(p => p.PatientID == patientId);
            if (patient == null) return;

            bool wasCritical = patient.IsCritical;
            patient.UpdateVitals(temperature, pulse, oxygen);
            _db.SaveChanges();

            if (!wasCritical && patient.IsCritical)
            {
                Raise(CriticalVitalAlert, AlertType.CriticalVital,
                    $"CRITICAL ALERT: {patient.PatientName} oxygen level dropped to {oxygen:0.#}% - doctor notified!");
            }
        }

        /// <summary>
        /// Scans all beds and raises a sanitation-overdue alert once per bed
        /// per 6 hours (throttled, persisted via Alerts table).
        /// </summary>
        internal void CheckSanitationDeadlines()
        {
            var beds = _db.Beds.ToList().Where(b => b.IsSanitationOverdue()).ToList();
            foreach (var bed in beds)
            {
                // Throttle: one alert per bed per 6 hours (check Alerts table).
                bool recentlyReported = _db.Alerts.Any(a =>
                    a.Type == AlertType.SanitationOverdue &&
                    a.Timestamp > DateTime.UtcNow.AddHours(-6) &&
                    a.Message.Contains(bed.WardName));

                if (!recentlyReported)
                {
                    Raise(SanitationOverdue, AlertType.SanitationOverdue,
                        $"SANITATION OVERDUE: bed '{bed.WardName}' not sanitized for over 48 hours.");
                }
            }
        }

        // ========================================================================
        //  Alert plumbing
        // ========================================================================

        /// <summary>Stores the alert in the database and notifies all subscribers.</summary>
        private void Raise(EventHandler<AlertEntry>? handlers, AlertType type, string message)
        {
            var entry = new AlertEntry { Type = type, Message = message, Timestamp = DateTime.UtcNow };
            _db.Alerts.Add(entry);
            _db.SaveChanges();

            // Keep only latest 100 alerts (delete oldest).
            var count = _db.Alerts.Count();
            if (count > MaxAlerts)
            {
                var toRemove = _db.Alerts.OrderBy(a => a.Timestamp).Take(count - MaxAlerts).ToList();
                _db.Alerts.RemoveRange(toRemove);
                _db.SaveChanges();
            }

            handlers?.Invoke(this, entry);
        }

        /// <summary>Returns the latest alerts as JSON-friendly data for the dashboard poller.</summary>
        public IReadOnlyList<AlertEntry> GetRecentAlerts(int count = 20)
        {
            return _db.Alerts.OrderByDescending(a => a.Timestamp).Take(count).ToList();
        }

        // ========================================================================
        //  Seed data - gives the dashboard realistic content for screenshots.
        // ========================================================================

        public void SeedSampleData()
        {
            if (_db.Patients.Any() || _db.Beds.Any()) return; // seed only once

            var beds = new List<Bed>
            {
                new Bed { WardName = "ICU-101", BedType = BedType.ICU, LastSanitized = DateTime.UtcNow.AddHours(-5) },
                new Bed { WardName = "ICU-102", BedType = BedType.ICU, LastSanitized = DateTime.UtcNow.AddHours(-10) },
                new Bed { WardName = "General-A1", BedType = BedType.General, LastSanitized = DateTime.UtcNow.AddHours(-2) },
                new Bed { WardName = "General-A2", BedType = BedType.General, LastSanitized = DateTime.UtcNow.AddHours(-12) },
                new Bed { WardName = "General-B1", BedType = BedType.General, LastSanitized = DateTime.UtcNow.AddHours(-60) },
            };
            _db.Beds.AddRange(beds);
            _db.SaveChanges();

            var patients = new List<Patient>
            {
                new Patient { PatientName = "Sumaiya Akter", Age = 30, DiseaseCategory = "Pneumonia", Temperature = 38.4, PulseRate = 96, OxygenLevel = 95 },
                new Patient { PatientName = "Rahim Uddin", Age = 64, DiseaseCategory = "Heart Failure", Temperature = 37.1, PulseRate = 110, OxygenLevel = 89 },
                new Patient { PatientName = "Nusrat Jahan", Age = 45, DiseaseCategory = "Dengue", Temperature = 39.6, PulseRate = 88, OxygenLevel = 94 },
                new Patient { PatientName = "Kamal Hossain", Age = 52, DiseaseCategory = "Fracture", Temperature = 36.8, PulseRate = 76, OxygenLevel = 98 },
            };
            // Evaluate critical status for seed patients (Rahim is critical).
            foreach (var p in patients) p.EvaluateCriticalStatus();
            _db.Patients.AddRange(patients);
            _db.SaveChanges();
        }
    }
}
