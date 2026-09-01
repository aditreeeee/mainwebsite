using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;

namespace eGlobeSolutions.Web.Services;

public class EnquiryService : IEnquiryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<EnquiryService> _logger;

    public EnquiryService(AppDbContext db, ILogger<EnquiryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Enquiry> CreateAsync(Enquiry enquiry, CancellationToken ct = default)
    {
        enquiry.CreatedAtUtc = DateTime.UtcNow;
        enquiry.CreatedBy = "public-website";

        _db.Enquiries.Add(enquiry);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "New {Type} enquiry #{Id} received from {Email}.",
            enquiry.Type, enquiry.Id, enquiry.Email);

        return enquiry;
    }
}
