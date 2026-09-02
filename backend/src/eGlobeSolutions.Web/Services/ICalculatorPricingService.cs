using eGlobeSolutions.Web.Models.Public.Calculator;

namespace eGlobeSolutions.Web.Services;

public interface ICalculatorPricingService
{
    Task<CalculatorCatalogDto> GetCatalogAsync(CancellationToken ct = default);
    Task<CalculateResultDto> CalculateAsync(CalculateRequest request, CancellationToken ct = default);
}
