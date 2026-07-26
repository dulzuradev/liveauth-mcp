using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace LiveAuth.CostShield.AspNetCore;

/// <summary>
/// Requires a valid CostShield authorization before an action executes.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = true)]
public sealed class LiveAuthProtectedAttribute
    : Attribute, IFilterFactory, IOrderedFilter
{
    /// <summary>Protects an action with the named CostShield policy.</summary>
    public LiveAuthProtectedAttribute(string action)
    {
        if (string.IsNullOrWhiteSpace(action) ||
            action.Trim().Length > 100)
        {
            throw new ArgumentException(
                "A protected action of 100 characters or less is required.",
                nameof(action));
        }
        Action = action.Trim();
    }

    /// <summary>The expected protected-action name.</summary>
    public string Action { get; }

    /// <summary>
    /// Optional expected origin. It must exactly match the token binding.
    /// </summary>
    public string? Origin { get; set; }

    /// <summary>Controls remote token consumption.</summary>
    public LiveAuthCostShieldConsumeMode Consume { get; set; }
        = LiveAuthCostShieldConsumeMode.Auto;

    /// <summary>The MVC authorization-filter order.</summary>
    public int Order { get; set; } = -1000;

    /// <inheritdoc />
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var verifier = serviceProvider.GetRequiredService<
            ILiveAuthCostShieldVerifier>();
        return new LiveAuthCostShieldAuthorizationFilter(
            verifier,
            Action,
            Origin,
            Consume);
    }
}
