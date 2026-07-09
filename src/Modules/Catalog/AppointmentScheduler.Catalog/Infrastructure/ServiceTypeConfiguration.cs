using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AppointmentScheduler.Catalog.Domain;

namespace AppointmentScheduler.Catalog.Infrastructure;

/// <summary>Fluent mapping for <see cref="ServiceType"/> (Catalog module, snake_case columns).</summary>
internal sealed class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable("service_types", "catalog");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.Name).HasColumnName("name").IsRequired();
        // TimeSpan maps to PostgreSQL `interval` via Npgsql.
        builder.Property(s => s.Duration).HasColumnName("duration").IsRequired();
    }
}
