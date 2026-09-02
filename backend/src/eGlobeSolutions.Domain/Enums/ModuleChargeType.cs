namespace eGlobeSolutions.Domain.Enums;

/// <summary>How a module/product's add-on rate is charged when it isn't
/// simply "Included" in the base subscription.</summary>
public enum ModuleChargeType
{
    /// <summary>Monthly rate multiplied by total rooms.</summary>
    PerRoomMonthly = 0,

    /// <summary>Monthly rate multiplied by number of properties.</summary>
    PerPropertyMonthly = 10,

    /// <summary>Single flat monthly rate regardless of size.</summary>
    FlatMonthly = 20,

    /// <summary>One-time charge only (no recurring monthly amount).</summary>
    OneTimeOnly = 30,

    /// <summary>Percentage commission applied to a volume the user provides at
    /// calculation time (e.g. booking value, online payment value).</summary>
    Commission = 40
}
