using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Models
{
    /// <summary>
    /// Type of bed in the hospital. ICU beds may only host critical patients.
    /// </summary>
    public enum BedType
    {
        General,
        ICU
    }

    /// <summary>
    /// Represents a physical hospital bed.
    /// Validation rules implemented here (per assignment):
    ///  - A bed is only assignable if it was sanitized within the last 48 hours.
    ///  - A bed can host at most one patient (IsOccupied guard).
    /// </summary>
    public class Bed
    {
        // Unique identifier generated automatically (GUID requirement).
        public Guid BedID { get; set; } = Guid.NewGuid();

        [Display(Name = "Ward")]
        [Required(ErrorMessage = "Ward name is required.")]
        [StringLength(60)]
        public string WardName { get; set; } = string.Empty;

        [Display(Name = "Bed Type")]
        public BedType BedType { get; set; } = BedType.General;

        [Display(Name = "Occupied")]
        public bool IsOccupied { get; set; }

        // Timestamp of the last sanitation cycle. Must be within 48h for allocation.
        // Use UtcNow for PostgreSQL timestamp with time zone compatibility (Neon pooled).
        [Display(Name = "Last Sanitized")]
        public DateTime LastSanitized { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// True when more than 48 hours have passed since the last sanitation.
        /// Such beds must not be allocated until re-sanitized.
        /// </summary>
        public bool IsSanitationOverdue()
            => DateTime.UtcNow - LastSanitized > TimeSpan.FromHours(48);

        /// <summary>
        /// A bed can be allocated only when it is free and freshly sanitized.
        /// </summary>
        public bool IsAssignable()
            => !IsOccupied && !IsSanitationOverdue();

        /// <summary>
        /// Marks the bed as sanitized right now (staff action from the UI).
        /// </summary>
        public void MarkSanitized()
            => LastSanitized = DateTime.UtcNow;
    }
}
