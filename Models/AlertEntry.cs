namespace WebApplication2.Models
{
    /// <summary>
    /// Categories of notifications the system raises (assignment: Event Notifications).
    /// </summary>
    public enum AlertType
    {
        CriticalVital,      // A patient's vital crossed a safety threshold.
        BedAllocated,       // Successful bed allocation.
        AllocationFailed,   // Allocation attempt rejected by a safety rule.
        SanitationOverdue   // A bed has not been sanitized within 48 hours.
    }

    /// <summary>
    /// One entry in the hospital alert feed shown on the monitoring dashboard.
    /// </summary>
    public class AlertEntry
    {
        public Guid AlertID { get; set; } = Guid.NewGuid();

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public AlertType Type { get; set; }

        public string Message { get; set; } = string.Empty;

        /// <summary>Bootstrap-style CSS class used to color the alert row in the UI.</summary>
        public string CssClass => Type switch
        {
            AlertType.CriticalVital => "alert-danger",
            AlertType.AllocationFailed => "alert-warning",
            AlertType.SanitationOverdue => "alert-secondary",
            _ => "alert-success"
        };
    }
}
