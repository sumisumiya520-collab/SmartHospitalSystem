using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Models
{
    /// <summary>
    /// Represents a hospital patient and their live vital signs.
    /// Validation rules implemented here (per assignment):
    ///  - Temperature must stay within 35-42 degrees Celsius.
    ///  - OxygenLevel below 92% automatically marks the patient as Critical.
    /// </summary>
    public class Patient
    {
        // Unique identifier generated automatically (GUID requirement).
        public Guid PatientID { get; set; } = Guid.NewGuid();

        [Display(Name = "Patient Name")]
        [Required(ErrorMessage = "Patient name is required.")]
        [StringLength(100)]
        public string PatientName { get; set; } = string.Empty;

        [Range(1, 120, ErrorMessage = "Age must be between 1 and 120.")]
        public int Age { get; set; }

        [Required(ErrorMessage = "Disease category is required.")]
        [StringLength(60)]
        public string DiseaseCategory { get; set; } = string.Empty;

        // Normal body temperature range accepted by the hospital rules: 35C - 42C.
        [Display(Name = "Temperature (C)")]
        [Range(35, 42, ErrorMessage = "Temperature must be between 35 and 42 degrees Celsius.")]
        public double Temperature { get; set; }

        [Display(Name = "Pulse Rate (bpm)")]
        [Range(30, 220, ErrorMessage = "Pulse rate must be between 30 and 220 bpm.")]
        public int PulseRate { get; set; }

        // Oxygen saturation in percent. Below 92% the patient becomes critical.
        [Display(Name = "Oxygen Level (%)")]
        [Range(50, 100, ErrorMessage = "Oxygen level must be between 50 and 100 %.")]
        public double OxygenLevel { get; set; }

        [Display(Name = "Critical")]
        public bool IsCritical { get; private set; }

        // Bed currently occupied by this patient (null = unallocated).
        public Guid? AssignedBedID { get; set; }

        /// <summary>
        /// Recalculates the critical flag from the current vitals.
        /// Rule: OxygenLevel < 92% => patient is critical.
        /// Called every time new vitals arrive so the flag is always up to date.
        /// </summary>
        public void EvaluateCriticalStatus()
        {
            bool wasCritical = IsCritical;
            IsCritical = OxygenLevel < 92;

            // Flag transitions are interesting for the UI/alerts layer.
            if (!wasCritical && IsCritical)
                VitalsDroppedCritical?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Event raised when this patient's vitals cross into the critical zone.
        /// HospitalManager subscribes to it to broadcast a doctor alert.
        /// </summary>
        public event EventHandler? VitalsDroppedCritical;

        /// <summary>
        /// Applies a fresh set of vitals (used by the monitor service and edit forms),
        /// then re-evaluates the critical status.
        /// </summary>
        public void UpdateVitals(double temperature, int pulseRate, double oxygenLevel)
        {
            Temperature = temperature;
            PulseRate = pulseRate;
            OxygenLevel = oxygenLevel;
            EvaluateCriticalStatus();
        }
    }
}
