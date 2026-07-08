using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AppointmentScheduler.Domain.Fleet;

namespace AppointmentScheduler.Infrastructure.Persistence.Configurations;

/// <summary>Fluent mapping for <see cref="ServiceBay"/> (Fleet module, snake_case columns).</summary>
internal sealed class ServiceBayConfiguration : IEntityTypeConfiguration<ServiceBay>
{
    public void Configure(EntityTypeBuilder<ServiceBay> builder)
    {
        builder.ToTable("service_bays");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");
        builder.Property(b => b.DealershipId).HasColumnName("dealership_id").IsRequired();
        builder.Property(b => b.Label).HasColumnName("label").IsRequired();

        builder.HasIndex(b => b.DealershipId);
    }
}
