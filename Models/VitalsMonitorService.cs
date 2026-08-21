namespace WebApplication2.Models
{
    /// <summary>
    /// Background service that simulates continuous patient monitoring.
    /// This is the web equivalent of the WinForms Timer control: every few
    /// seconds it nudges each patient's vitals and re-checks safety rules.
    ///
    /// Registered as a hosted service in Program.cs, so it starts and stops
    /// together with the web application.
    /// </summary>
    public class VitalsMonitorService : BackgroundService
    {
        // Monitoring interval: vitals are checked every 5 minutes
        // (assignment requirement).
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        private readonly HospitalManager _hospital;
        private readonly ILogger<VitalsMonitorService> _logger;
        private readonly Random _random = new();

        public VitalsMonitorService(HospitalManager hospital, ILogger<VitalsMonitorService> logger)
        {
            _hospital = hospital;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Vitals monitor started (interval {Interval}s).", Interval.TotalSeconds);

            using var timer = new PeriodicTimer(Interval);

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    SimulateVitalsTick();
                    _hospital.CheckSanitationDeadlines();
                }
                catch (Exception ex)
                {
                    // Never let one bad tick kill the monitoring loop.
                    _logger.LogError(ex, "Vitals monitor tick failed.");
                }
            }
        }

        /// <summary>
        /// Produces a small random drift for every patient's vitals.
        /// Occasionally a patient's oxygen dips below 92% so the critical
        /// alert path can be demonstrated live on the dashboard.
        /// </summary>
        private void SimulateVitalsTick()
        {
            foreach (var patient in _hospital.Patients.ToList())
            {
                double temperature = Math.Clamp(
                    patient.Temperature + RandomDrift(0.3), 35.0, 42.0);
                int pulse = (int)Math.Clamp(
                    patient.PulseRate + RandomDrift(6), 30, 220);

                // 8% chance per tick of an oxygen dip below the 92% threshold.
                double oxygen;
                if (_random.NextDouble() < 0.08)
                    oxygen = _random.NextDouble() * 6 + 85;      // 85-91 => triggers critical
                else
                    oxygen = Math.Clamp(
                        patient.OxygenLevel + RandomDrift(1.5), 50, 100);

                _hospital.ApplyVitals(patient.PatientID, temperature, pulse, oxygen);
            }
        }

        /// <summary>Random step in the range [-step, +step].</summary>
        private double RandomDrift(double step)
            => (_random.NextDouble() * 2 - 1) * step;
    }
}
