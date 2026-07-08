using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AppointmentScheduler.Domain.Workforce;

namespace AppointmentScheduler.Infrastructure.Persistence.Configurations;

/// <summary>Fluent mapping for <see cref="Technician"/> (Workforce module, snake_case columns).</summary>
internal sealed class TechnicianConfiguration : IEntityTypeConfiguration<Technician>
{
    public void Configure(EntityTypeBuilder<Technician> builder)
    {
        builder.ToTable("technicians");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.DealershipId).HasColumnName("dealership_id").IsRequired();
        builder.Property(t => t.Name).HasColumnName("name").IsRequired();

        builder.HasIndex(t => t.DealershipId);
    }
}
