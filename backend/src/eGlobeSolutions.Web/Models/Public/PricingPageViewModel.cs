using eGlobeSolutions.Domain.Entities;

namespace eGlobeSolutions.Web.Models.Public;

public class PricingPageViewModel
{
    public List<PricingPlan> Plans { get; set; } = new();
    public List<PricingComparisonRow> ComparisonRows { get; set; } = new();
    public List<FaqItem> Faqs { get; set; } = new();
    public SeoMetadata? Seo { get; set; }
}
