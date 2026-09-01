using eGlobeSolutions.Domain.Entities;

namespace eGlobeSolutions.Web.Services;

public interface IEnquiryService
{
    Task<Enquiry> CreateAsync(Enquiry enquiry, CancellationToken ct = default);
}
