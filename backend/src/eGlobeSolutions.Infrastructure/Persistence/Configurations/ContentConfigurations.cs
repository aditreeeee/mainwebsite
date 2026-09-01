using eGlobeSolutions.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eGlobeSolutions.Infrastructure.Persistence.Configurations;

public class ContentBlockConfiguration : IEntityTypeConfiguration<ContentBlock>
{
    public void Configure(EntityTypeBuilder<ContentBlock> builder)
    {
        builder.ToTable("ContentBlocks");
        builder.Property(e => e.PageKey).HasMaxLength(60).IsRequired();
        builder.Property(e => e.SectionKey).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Kicker).HasMaxLength(120);
        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.Subtitle).HasMaxLength(400);
        builder.Property(e => e.Body).HasMaxLength(4000);
        builder.Property(e => e.CtaLabel).HasMaxLength(60);
        builder.Property(e => e.CtaUrl).HasMaxLength(200);
        builder.Property(e => e.ImageUrl).HasMaxLength(300);
        builder.HasIndex(e => new { e.PageKey, e.SectionKey });
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("MediaAssets");
        builder.Property(e => e.FileName).HasMaxLength(260).IsRequired();
        builder.Property(e => e.OriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Url).HasMaxLength(400).IsRequired();
        builder.Property(e => e.AltText).HasMaxLength(300);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("MenuItems");
        builder.Property(e => e.Location).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Label).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Url).HasMaxLength(300).IsRequired();
        builder.HasIndex(e => e.Location);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class SeoMetadataConfiguration : IEntityTypeConfiguration<SeoMetadata>
{
    public void Configure(EntityTypeBuilder<SeoMetadata> builder)
    {
        builder.ToTable("SeoMetadata");
        builder.Property(e => e.PageKey).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(400).IsRequired();
        builder.Property(e => e.Keywords).HasMaxLength(400);
        builder.Property(e => e.CanonicalUrl).HasMaxLength(300);
        builder.Property(e => e.OgImageUrl).HasMaxLength(300);
        builder.HasIndex(e => e.PageKey).IsUnique();
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class SiteSettingConfiguration : IEntityTypeConfiguration<SiteSetting>
{
    public void Configure(EntityTypeBuilder<SiteSetting> builder)
    {
        builder.ToTable("SiteSettings");
        builder.Property(e => e.Key).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Value).HasMaxLength(1000);
        builder.Property(e => e.Group).HasMaxLength(60);
        builder.HasIndex(e => e.Key).IsUnique();
    }
}
