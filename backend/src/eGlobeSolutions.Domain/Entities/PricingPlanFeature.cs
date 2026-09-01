using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>One bullet line under a pricing plan's feature list.</summary>
public class PricingPlanFeature : AuditableEntity
{
    public int PricingPlanId { get; set; }
    public PricingPlan? PricingPlan { get; set; }

    public string Text { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
