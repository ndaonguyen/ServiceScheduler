using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Domain.Widgets;

namespace ServiceScheduler.Infrastructure.Persistence.Configurations;

/// <summary>Fluent mapping for <see cref="Widget"/>. Copy this pattern per aggregate.</summary>
internal sealed class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        // Schema is generated from this model by EF Core migrations. Column names are snake_case.
        builder.ToTable("widgets");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasColumnName("id");

        builder.Property(w => w.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
    }
}
