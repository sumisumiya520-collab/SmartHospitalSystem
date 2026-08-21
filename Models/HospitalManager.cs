namespace WebApplication2.Models
{
    /// <summary>
    /// Central business service of the hospital system.
    ///
    /// Design patterns used (maps to course outcomes CO1/CO2):
    ///  - SINGLETON: registered once in Program.cs, so every request and the
    ///    background monitor share the same patient/bed/alert state.
    ///  - OBSERVER: exposes C# events that the UI layer subscribes to, keeping
    ///    business rules decoupled from presentation.
    ///
    /// All state is kept in memory (assignment scope) and guarded by a lock,
    /// because HTTP requests and the background vitals monitor run concurrently.
    /// </summary>
    public class HospitalManager
    {
        // ---- Singleton plumbing -------------------------------------------------
        private static readonly object _stateLock = new();

        // ---- In-memory data stores ---------------------------------------------
        private readonly List<Patient> _patients = new();
        private readonly List<Bed> _beds = new();
        private readonly List<AlertEntry> _alerts = new();

        // Keep the feed bounded so it never grows forever during long demos.
        private const int MaxAlerts = 100;

        // Beds already flagged as sanitation-overdue, so the alert fires once
        // per bed instead of spamming the feed every monitor tick.
        private readonly HashSet<Guid> _sanitationAlerted = new();

        public IReadOnlyList<Patient> Patients => _patients;
        public IReadOnlyList<Bed> Beds => _beds;
        public IReadOnlyList<AlertEntry> Alerts => _alerts;

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
            lock (_stateLock)
            {
                patient.EvaluateCriticalStatus();
                _patients.Add(patient);
            }
        }

        public Patient? GetPatient(Guid id)
        {
            lock (_stateLock)
                return _patients.FirstOrDefault(p => p.PatientID == id);
        }

        public bool UpdatePatient(Patient updated)
        {
            lock (_stateLock)
            {
                var existing = _patients.FirstOrDefault(p => p.PatientID == updated.PatientID);
                if (existing == null) return false;

                existing.PatientName = updated.PatientName;
                existing.Age = updated.Age;
                existing.DiseaseCategory = updated.DiseaseCategory;
                existing.UpdateVitals(updated.Temperature, updated.PulseRate, updated.OxygenLevel);

                // Safety rule: a critical patient may never stay in a General ward.
                if (existing.IsCritical && existing.AssignedBedID.HasValue)
                {
                    var bed = _beds.FirstOrDefault(b => b.BedID == existing.AssignedBedID.Value);
                    if (bed is { BedType: BedType.General })
                    {
                        DeallocateInternal(existing, reason: "Patient became critical - moved out of General Ward.");
                    }
                }
                return true;
            }
        }

        public bool RemovePatient(Guid id)
        {
            lock (_stateLock)
            {
                var patient = _patients.FirstOrDefault(p => p.PatientID == id);
                if (patient == null) return false;

                if (patient.AssignedBedID.HasValue)
                    DeallocateInternal(patient, reason: "Patient removed - bed released.");

                _patients.Remove(patient);
                return true;
            }
        }

        // ========================================================================
        //  Bed management
        // ========================================================================

        public void AddBed(Bed bed)
        {
            ArgumentNullException.ThrowIfNull(bed);
            lock (_stateLock)
            {
                if (bed.LastSanitized == default) bed.MarkSanitized();
                _beds.Add(bed);
            }
        }

        public Bed? GetBed(Guid id)
        {
            lock (_stateLock)
                return _beds.FirstOrDefault(b => b.BedID == id);
        }

        public bool UpdateBed(Bed updated)
        {
            lock (_stateLock)
            {
                var existing = _beds.FirstOrDefault(b => b.BedID == updated.BedID);
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
                return true;
            }
        }

        public bool RemoveBed(Guid id)
        {
            lock (_stateLock)
            {
                var bed = _beds.FirstOrDefault(b => b.BedID == id);
                if (bed == null) return false;
                if (bed.IsOccupied)
                {
                    Raise(AllocationFailed, AlertType.AllocationFailed,
                        $"Cannot remove bed '{bed.WardName}' - currently occupied.");
                    return false;
                }
                _beds.Remove(bed);
                return true;
            }
        }

        /// <summary>Staff action: mark a bed as sanitized right now.</summary>
        public bool SanitizeBed(Guid id)
        {
            lock (_stateLock)
            {
                var bed = _beds.FirstOrDefault(b => b.BedID == id);
                if (bed == null) return false;
                bed.MarkSanitized();
                _sanitationAlerted.Remove(bed.BedID);
                Raise(BedAllocated, AlertType.BedAllocated,
                    $"Bed '{bed.WardName}' ({bed.BedType}) sanitized - ready for allocation.");
                return true;
            }
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
            lock (_stateLock)
            {
                var patient = _patients.FirstOrDefault(p => p.PatientID == patientId);
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

                // Candidate beds: free + sanitized within 48 hours.
                IEnumerable<Bed> candidates = _beds.Where(b => b.IsAssignable());

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

                // Commit the allocation atomically (we are inside the lock).
                bed.IsOccupied = true;
                patient.AssignedBedID = bed.BedID;

                Raise(BedAllocated, AlertType.BedAllocated,
                    $"{patient.PatientName} allocated to {bed.BedType} bed '{bed.WardName}'.");
                return true;
            }
        }

        /// <summary>Public deallocate used by the dashboard (discharge / move).</summary>
        public bool DeallocatePatient(Guid patientId, string reason = "Patient deallocated by staff.")
        {
            lock (_stateLock)
            {
                var patient = _patients.FirstOrDefault(p => p.PatientID == patientId);
                if (patient == null || !patient.AssignedBedID.HasValue) return false;
                DeallocateInternal(patient, reason);
                return true;
            }
        }

        private void DeallocateInternal(Patient patient, string reason)
        {
            var bed = _beds.FirstOrDefault(b => b.BedID == patient.AssignedBedID!.Value);
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
            lock (_stateLock)
            {
                var patient = _patients.FirstOrDefault(p => p.PatientID == patientId);
                if (patient == null) return;

                bool wasCritical = patient.IsCritical;
                patient.UpdateVitals(temperature, pulse, oxygen);

                if (!wasCritical && patient.IsCritical)
                {
                    Raise(CriticalVitalAlert, AlertType.CriticalVital,
                        $"CRITICAL ALERT: {patient.PatientName} oxygen level dropped to {oxygen:0.#}% - doctor notified!");
                }
            }
        }

        /// <summary>
        /// Scans all beds and raises a sanitation-overdue alert once per bed
        /// until the bed is re-sanitized.
        /// </summary>
        internal void CheckSanitationDeadlines()
        {
            lock (_stateLock)
            {
                foreach (var bed in _beds.Where(b => b.IsSanitationOverdue()))
                {
                    if (_sanitationAlerted.Add(bed.BedID))
                    {
                        Raise(SanitationOverdue, AlertType.SanitationOverdue,
                            $"SANITATION OVERDUE: bed '{bed.WardName}' not sanitized for over 48 hours.");
                    }
                }
            }
        }

        // ========================================================================
        //  Alert plumbing
        // ========================================================================

        /// <summary>Stores the alert in the feed and notifies all subscribers.</summary>
        private void Raise(EventHandler<AlertEntry>? handlers, AlertType type, string message)
        {
            var entry = new AlertEntry { Type = type, Message = message };
            _alerts.Insert(0, entry);
            if (_alerts.Count > MaxAlerts)
                _alerts.RemoveAt(_alerts.Count - 1);

            handlers?.Invoke(this, entry);
        }

        /// <summary>Returns the latest alerts as JSON-friendly data for the dashboard poller.</summary>
        public IReadOnlyList<AlertEntry> GetRecentAlerts(int count = 20)
        {
            lock (_stateLock)
                return _alerts.Take(count).ToList();
        }

        // ========================================================================
        //  Seed data - gives the dashboard realistic content for screenshots.
        // ========================================================================

        public void SeedSampleData()
        {
            lock (_stateLock)
            {
                if (_patients.Count > 0 || _beds.Count > 0) return; // seed only once

                AddBed(new Bed { WardName = "ICU-101", BedType = BedType.ICU });
                AddBed(new Bed { WardName = "ICU-102", BedType = BedType.ICU });
                AddBed(new Bed { WardName = "General-A1", BedType = BedType.General });
                AddBed(new Bed { WardName = "General-A2", BedType = BedType.General });
                // One stale bed so the sanitation-overdue alert can be demonstrated.
                AddBed(new Bed { WardName = "General-B1", BedType = BedType.General, LastSanitized = DateTime.Now.AddHours(-60) });

                AddPatient(new Patient { PatientName = "Sumaiya Akter", Age = 30, DiseaseCategory = "Pneumonia", Temperature = 38.4, PulseRate = 96, OxygenLevel = 95 });
                AddPatient(new Patient { PatientName = "Rahim Uddin", Age = 64, DiseaseCategory = "Heart Failure", Temperature = 37.1, PulseRate = 110, OxygenLevel = 89 }); // starts critical
                AddPatient(new Patient { PatientName = "Nusrat Jahan", Age = 45, DiseaseCategory = "Dengue", Temperature = 39.6, PulseRate = 88, OxygenLevel = 94 });
                AddPatient(new Patient { PatientName = "Kamal Hossain", Age = 52, DiseaseCategory = "Fracture", Temperature = 36.8, PulseRate = 76, OxygenLevel = 98 });
            }
        }
    }
}
