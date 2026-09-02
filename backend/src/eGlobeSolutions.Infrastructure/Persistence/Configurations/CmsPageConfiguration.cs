using eGlobeSolutions.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eGlobeSolutions.Infrastructure.Persistence.Configurations;

public class CmsPageConfiguration : IEntityTypeConfiguration<CmsPage>
{
    public void Configure(EntityTypeBuilder<CmsPage> builder)
    {
        builder.ToTable("CmsPages");
        builder.Property(e => e.Title).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Slug).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Body).HasMaxLength(50000).IsRequired();
        builder.Property(e => e.Subtitle).HasMaxLength(500);
        builder.Property(e => e.MetaTitle).HasMaxLength(200);
        builder.Property(e => e.MetaDescription).HasMaxLength(400);
        builder.Property(e => e.MetaKeywords).HasMaxLength(400);

        builder.HasIndex(e => e.Slug).IsUnique();
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
