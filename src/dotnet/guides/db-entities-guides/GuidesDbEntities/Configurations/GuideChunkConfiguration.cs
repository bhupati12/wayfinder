using GuidesDbEntities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuidesDbEntities.Configurations;

public class GuideChunkConfiguration : IEntityTypeConfiguration<GuideChunk>
{
    public void Configure(EntityTypeBuilder<GuideChunk> builder)
    {
        builder.ToTable("guide_chunks");

        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.ArticleId, c.Position }).IsUnique();
        builder.HasIndex(c => c.ContentHash);

        builder.Property(c => c.SectionHeading).HasMaxLength(1024).IsRequired();
        builder.Property(c => c.Text).IsRequired();
        builder.Property(c => c.ContentHash).HasMaxLength(64).IsFixedLength().IsRequired();
    }
}
