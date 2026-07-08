using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AppointmentScheduler.Domain.Workforce;

namespace AppointmentScheduler.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent mapping for <see cref="TechnicianQualification"/> (Workforce module, snake_case columns).
/// Composite key on (technician_id, service_type_id).
/// </summary>
internal sealed class TechnicianQualificationConfiguration : IEntityTypeConfiguration<TechnicianQualification>
{
    public void Configure(EntityTypeBuilder<TechnicianQualification> builder)
    {
        builder.ToTable("technician_qualifications");

        builder.HasKey(q => new { q.TechnicianId, q.ServiceTypeId });
        builder.Property(q => q.TechnicianId).HasColumnName("technician_id");
        builder.Property(q => q.ServiceTypeId).HasColumnName("service_type_id");

        builder.HasIndex(q => q.ServiceTypeId);
    }
}
