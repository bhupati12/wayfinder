using GuidesDbEntities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuidesDbEntities.Configurations;

public class GuideChunkConfiguration : IEntityTypeConfiguration<GuideChunk>
{
    /// <summary>
    /// Length of the embedding column (nomic-embed-text). The embedding client's configured
    /// dimensions must match; a different model needs a migration that changes this.
    /// </summary>
    public const int EmbeddingDimensions = 768;

    public void Configure(EntityTypeBuilder<GuideChunk> builder)
    {
        builder.ToTable("guide_chunks");

        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.ArticleId, c.Position }).IsUnique();
        builder.HasIndex(c => c.ContentHash);

        builder.Property(c => c.SectionHeading).HasMaxLength(1024).IsRequired();
        builder.Property(c => c.Text).IsRequired();
        builder.Property(c => c.ContentHash).HasMaxLength(64).IsFixedLength().IsRequired();

        builder.Property(c => c.Embedding).HasColumnType($"vector({EmbeddingDimensions})");
        builder.Property(c => c.EmbeddingModel).HasMaxLength(200);
        builder.Property(c => c.EmbeddedContentHash).HasMaxLength(64).IsFixedLength();

        // Approximate nearest-neighbour index for cosine distance (the <=> operator). HNSW builds a
        // layered proximity graph: queries are fast and recall is high, at the price of slower
        // inserts and more memory than IVFFlat. m and ef_construction are pgvector's defaults.
        builder
            .HasIndex(c => c.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops")
            .HasStorageParameter("m", 16)
            .HasStorageParameter("ef_construction", 64);
    }
}
