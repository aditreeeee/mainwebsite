namespace eGlobeSolutions.Domain.Enums;

/// <summary>How a module/product is offered under a given pricing plan.</summary>
public enum ModuleAvailability
{
    /// <summary>Bundled into the plan's base subscription at no extra charge.</summary>
    Included = 0,

    /// <summary>Available for this plan at its own add-on rate.</summary>
    AddOn = 10,

    /// <summary>Cannot be selected under this plan.</summary>
    NotAvailable = 20
}
