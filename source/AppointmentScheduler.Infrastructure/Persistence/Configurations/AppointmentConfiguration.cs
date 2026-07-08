using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AppointmentScheduler.Domain.Booking;

namespace AppointmentScheduler.Infrastructure.Persistence.Configurations;

/// <summary>Fluent mapping for <see cref="Appointment"/> (Booking module, snake_case columns).</summary>
internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.OwnerId).HasColumnName("owner_id").IsRequired();
        builder.Property(a => a.VehicleId).HasColumnName("vehicle_id").IsRequired();
        builder.Property(a => a.DealershipId).HasColumnName("dealership_id").IsRequired();
        builder.Property(a => a.ServiceTypeId).HasColumnName("service_type_id").IsRequired();
        builder.Property(a => a.ServiceBayId).HasColumnName("service_bay_id").IsRequired();
        builder.Property(a => a.TechnicianId).HasColumnName("technician_id").IsRequired();
        builder.Property(a => a.ScheduledStart).HasColumnName("scheduled_start").IsRequired();
        builder.Property(a => a.ScheduledEnd).HasColumnName("scheduled_end").IsRequired();
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        // Plain indexes on the assigned-resource columns. The no-overlap EXCLUDE constraint is #6.
        builder.HasIndex(a => a.ServiceBayId);
        builder.HasIndex(a => a.TechnicianId);
        builder.HasIndex(a => a.OwnerId);
    }
}
