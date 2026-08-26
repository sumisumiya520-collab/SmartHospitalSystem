using Microsoft.EntityFrameworkCore;

namespace WebApplication2.Models;

/// <summary>
/// Entity Framework database context for the Smart Hospital system (WebApplication2 DB-based).
/// Supports dual provider: SQL Server LocalDB for local dev, PostgreSQL (Neon pooled) for Render.
/// Database is created automatically with EnsureCreated(), so no migration files are needed.
/// </summary>
public class HospitalDbContext : DbContext
{
    public HospitalDbContext(DbContextOptions<HospitalDbContext> options)
        : base(options)
    {
    }

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<AlertEntry> Alerts => Set<AlertEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>().HasKey(p => p.PatientID);
        modelBuilder.Entity<Bed>().HasKey(b => b.BedID);
        modelBuilder.Entity<AlertEntry>().HasKey(a => a.AlertID);

        // Patient: IsCritical has private setter, EF Core can set via backing field.
        modelBuilder.Entity<Patient>()
            .Property(p => p.IsCritical)
            .HasField("<IsCritical>k__BackingField");

        // No FK constraint for AssignedBedID -> Bed, keep as simple Guid? column.
    }
}
