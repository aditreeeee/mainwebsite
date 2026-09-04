using eGlobeSolutions.Domain.Entities;

namespace eGlobeSolutions.Web.Models.Public;

public class SiteFooterViewModel
{
    public Dictionary<string, string?> Settings { get; set; } = new();
    public List<MenuItem> ProductLinks { get; set; } = new();
    public List<MenuItem> SolutionLinks { get; set; } = new();
    public List<MenuItem> CompanyLinks { get; set; } = new();

    public string Get(string key, string fallback) =>
        Settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;
}
