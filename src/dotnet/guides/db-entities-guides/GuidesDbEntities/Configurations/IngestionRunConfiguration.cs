using GuidesDbEntities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuidesDbEntities.Configurations;

public class IngestionRunConfiguration : IEntityTypeConfiguration<IngestionRun>
{
    public void Configure(EntityTypeBuilder<IngestionRun> builder)
    {
        builder.ToTable("ingestion_runs");

        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.StartedAt);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Source).HasMaxLength(1024);
    }
}
