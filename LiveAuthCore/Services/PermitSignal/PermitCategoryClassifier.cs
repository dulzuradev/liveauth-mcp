using LiveAuthCore.Data.Entities.PermitSignal;

namespace LiveAuthCore.Services.PermitSignal;

public interface IPermitCategoryClassifier
{
    IReadOnlyList<string> Classify(string? permitType, string? permitSubtype, string? description);
}

public sealed class PermitCategoryClassifier : IPermitCategoryClassifier
{
    private static readonly (string Category, string[] Terms)[] Rules =
    [
        (PermitWorkCategories.Roofing, ["roof", "reroof", "re-roof", "shingle"]),
        (PermitWorkCategories.Hvac, ["hvac", "air condition", "rooftop unit", "heat pump", "furnace", "ductwork", "duct work", "cooling", "rtu"]),
        (PermitWorkCategories.Electrical, ["electrical", "electric", "wiring", "rewire", "service upgrade", "panel upgrade", "switchgear", "transformer", "amp service", "photovoltaic inverter"]),
        (PermitWorkCategories.Plumbing, ["plumbing", "plumber", "sewer", "water heater", "water line", "gas line", "backflow", "fixture"]),
        (PermitWorkCategories.Solar, ["solar", "photovoltaic", "pv system"]),
        (PermitWorkCategories.FireProtection, ["fire alarm", "fire sprinkler", "fire protection", "standpipe", "suppression system"]),
        (PermitWorkCategories.Mechanical, ["mechanical", "boiler", "elevator"]),
        (PermitWorkCategories.Structural, ["structural", "foundation", "seismic", "load bearing", "retaining wall", "underpin"]),
        (PermitWorkCategories.Demolition, ["demolition", "demolish", "demo permit", "wrecking"]),
        (PermitWorkCategories.NewConstruction, ["new construction", "new building", "construct new", "ground-up", "ground up"]),
        (PermitWorkCategories.TenantImprovement, ["tenant improvement", "tenant build", "tenant finish", "commercial interior", " ti "]),
        (PermitWorkCategories.Renovation, ["renovation", "remodel", "alteration", "addition", "rehabilitation", "repair"])
    ];

    public IReadOnlyList<string> Classify(string? permitType, string? permitSubtype, string? description)
    {
        var text = $" {permitType} {permitSubtype} {description} ".ToLowerInvariant();
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (category, terms) in Rules)
        {
            if (terms.Any(text.Contains))
                categories.Add(category);
        }

        if (text.Contains("building") || text.Contains("construction") ||
            categories.Contains(PermitWorkCategories.NewConstruction))
            categories.Add(PermitWorkCategories.GeneralConstruction);

        if (categories.Count == 0)
            categories.Add(PermitWorkCategories.Other);

        return categories.OrderBy(CategoryOrder).ThenBy(category => category).ToArray();
    }

    private static int CategoryOrder(string category) => category switch
    {
        PermitWorkCategories.Hvac or PermitWorkCategories.Electrical or PermitWorkCategories.Plumbing or
            PermitWorkCategories.Roofing or PermitWorkCategories.Solar or PermitWorkCategories.FireProtection => 0,
        PermitWorkCategories.Mechanical or PermitWorkCategories.Structural or PermitWorkCategories.Demolition => 1,
        PermitWorkCategories.NewConstruction or PermitWorkCategories.Renovation or PermitWorkCategories.TenantImprovement => 2,
        PermitWorkCategories.GeneralConstruction => 3,
        _ => 4
    };
}

public static class TradeCategoryNormalizer
{
    public static string? Normalize(string? trade)
    {
        var value = trade?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains("hvac") || value.Contains("heating") || value.Contains("cooling")) return PermitWorkCategories.Hvac;
        if (value.Contains("electric")) return PermitWorkCategories.Electrical;
        if (value.Contains("plumb")) return PermitWorkCategories.Plumbing;
        if (value.Contains("roof")) return PermitWorkCategories.Roofing;
        if (value.Contains("solar") || value.Contains("photovoltaic")) return PermitWorkCategories.Solar;
        if (value.Contains("fire")) return PermitWorkCategories.FireProtection;
        if (value.Contains("mechanic")) return PermitWorkCategories.Mechanical;
        if (value.Contains("structur")) return PermitWorkCategories.Structural;
        if (value.Contains("demolition")) return PermitWorkCategories.Demolition;
        if (value.Contains("general") || value.Contains("construction")) return PermitWorkCategories.GeneralConstruction;
        return PermitWorkCategories.Normalize(trade);
    }
}
