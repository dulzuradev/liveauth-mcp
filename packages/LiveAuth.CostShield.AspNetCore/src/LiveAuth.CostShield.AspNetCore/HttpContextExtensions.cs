namespace Microsoft.AspNetCore.Http;

/// <summary>Accessors for the current CostShield authorization.</summary>
public static class LiveAuthCostShieldHttpContextExtensions
{
    /// <summary>
    /// Gets the CostShield authorization established for this request.
    /// </summary>
    public static LiveAuth.CostShield.AspNetCore
        .LiveAuthCostShieldAuthorization?
        GetLiveAuthCostShieldAuthorization(
            this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Features
            .Get<LiveAuth.CostShield.AspNetCore
                .ILiveAuthCostShieldFeature>()
            ?.Authorization;
    }
}
