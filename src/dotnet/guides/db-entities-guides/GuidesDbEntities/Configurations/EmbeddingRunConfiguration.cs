using GuidesDbEntities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuidesDbEntities.Configurations;

public class EmbeddingRunConfiguration : IEntityTypeConfiguration<EmbeddingRun>
{
    public void Configure(EntityTypeBuilder<EmbeddingRun> builder)
    {
        builder.ToTable("embedding_runs");

        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.StartedAt);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Model).HasMaxLength(200).IsRequired();
        builder.Property(r => r.EstimatedCostUsd).HasPrecision(12, 6);
    }
}
