using GuidesDbEntities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuidesDbEntities.Configurations;

public class GuideArticleConfiguration : IEntityTypeConfiguration<GuideArticle>
{
    public void Configure(EntityTypeBuilder<GuideArticle> builder)
    {
        builder.ToTable("guide_articles");

        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.PageId).IsUnique();
        builder.HasIndex(a => a.Title);

        builder.Property(a => a.Title).HasMaxLength(512).IsRequired();
        builder.Property(a => a.Url).HasMaxLength(2048).IsRequired();
        builder.Property(a => a.RawTextHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(a => a.ProcessingSignature).HasMaxLength(64).IsFixedLength().IsRequired();

        builder
            .HasMany(a => a.Chunks)
            .WithOne(c => c.Article)
            .HasForeignKey(c => c.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
