using eGlobeSolutions.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eGlobeSolutions.Infrastructure.Persistence.Configurations;

public class BlogPostConfiguration : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> builder)
    {
        builder.ToTable("BlogPosts");
        builder.Property(e => e.Title).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Slug).HasMaxLength(150);
        builder.Property(e => e.Category).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Excerpt).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Body).HasMaxLength(20000);
        builder.Property(e => e.AuthorName).HasMaxLength(150);
        builder.Property(e => e.AuthorRole).HasMaxLength(100);
        builder.Property(e => e.CoverImageUrl).HasMaxLength(300);
        builder.Property(e => e.MetaTitle).HasMaxLength(200);
        builder.Property(e => e.MetaDescription).HasMaxLength(400);
        builder.Property(e => e.MetaKeywords).HasMaxLength(400);

        builder.HasIndex(e => e.Slug).IsUnique().HasFilter("[Slug] IS NOT NULL");
        builder.HasIndex(e => e.PublishedAtUtc);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
