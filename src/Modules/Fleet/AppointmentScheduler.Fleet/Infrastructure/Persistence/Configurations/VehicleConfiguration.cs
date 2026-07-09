using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AppointmentScheduler.Fleet.Domain;

namespace AppointmentScheduler.Fleet.Infrastructure.Configurations;

/// <summary>Fluent mapping for <see cref="Vehicle"/> (Fleet module, snake_case columns).</summary>
internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles", "fleet");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id");
        builder.Property(v => v.OwnerId).HasColumnName("owner_id").IsRequired();
        builder.Property(v => v.Make).HasColumnName("make").IsRequired();
        builder.Property(v => v.Model).HasColumnName("model").IsRequired();
        builder.Property(v => v.Year).HasColumnName("year").IsRequired();
        builder.Property(v => v.Vin).HasColumnName("vin").IsRequired();

        builder.HasIndex(v => v.OwnerId);
    }
}
