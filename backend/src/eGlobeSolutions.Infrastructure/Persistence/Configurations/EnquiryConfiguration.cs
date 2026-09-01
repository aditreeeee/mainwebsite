using eGlobeSolutions.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eGlobeSolutions.Infrastructure.Persistence.Configurations;

public class EnquiryConfiguration : IEntityTypeConfiguration<Enquiry>
{
    public void Configure(EntityTypeBuilder<Enquiry> builder)
    {
        builder.ToTable("Enquiries");

        builder.Property(e => e.FullName).HasMaxLength(150).IsRequired();
        builder.Property(e => e.HotelName).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Phone).HasMaxLength(30).IsRequired();
        builder.Property(e => e.RoomsRange).HasMaxLength(20);
        builder.Property(e => e.InterestedIn).HasMaxLength(500);
        builder.Property(e => e.OtherInterest).HasMaxLength(300);
        builder.Property(e => e.Message).HasMaxLength(2000);
        builder.Property(e => e.CompanyType).HasMaxLength(150);
        builder.Property(e => e.ExpectedPropertyVolume).HasMaxLength(100);
        builder.Property(e => e.InternalNotes).HasMaxLength(4000);
        builder.Property(e => e.SourceIpAddress).HasMaxLength(64);
        builder.Property(e => e.SourceUserAgent).HasMaxLength(500);
        builder.Property(e => e.SourcePage).HasMaxLength(200);
        builder.Property(e => e.CreatedBy).HasMaxLength(200);
        builder.Property(e => e.UpdatedBy).HasMaxLength(200);
        builder.Property(e => e.DeletedBy).HasMaxLength(200);

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Type);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.Email);

        // Soft-deleted enquiries never show up in normal queries; the admin
        // "Trash" view uses IgnoreQueryFilters() to see them for restore/purge.
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
