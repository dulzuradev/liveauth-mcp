using System.Text.RegularExpressions;

namespace LiveAuthCore.Services.PermitSignal;

public interface IAddressNormalizer
{
    string Normalize(string address);
}

public sealed partial class AddressNormalizer : IAddressNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> Suffixes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STREET"] = "ST", ["ST"] = "ST",
            ["AVENUE"] = "AVE", ["AVE"] = "AVE",
            ["BOULEVARD"] = "BLVD", ["BLVD"] = "BLVD",
            ["ROAD"] = "RD", ["RD"] = "RD",
            ["DRIVE"] = "DR", ["DR"] = "DR",
            ["LANE"] = "LN", ["LN"] = "LN",
            ["COURT"] = "CT", ["CT"] = "CT",
            ["PLACE"] = "PL", ["PL"] = "PL",
            ["PARKWAY"] = "PKWY", ["PKWY"] = "PKWY",
            ["HIGHWAY"] = "HWY", ["HWY"] = "HWY",
            ["TERRACE"] = "TER", ["TER"] = "TER",
            ["CIRCLE"] = "CIR", ["CIR"] = "CIR",
            ["TRAIL"] = "TRL", ["TRL"] = "TRL",
            ["COVE"] = "CV", ["CV"] = "CV"
        };

    public string Normalize(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        var cleaned = NonAddressCharacters().Replace(address.ToUpperInvariant(), " ");
        cleaned = UnitDesignator().Replace(cleaned, " UNIT ");
        var parts = Whitespace().Split(cleaned.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => Suffixes.TryGetValue(part, out var suffix) ? suffix : part);
        return string.Join(' ', parts);
    }

    [GeneratedRegex(@"[^A-Z0-9# ]+")]
    private static partial Regex NonAddressCharacters();

    [GeneratedRegex(@"(?<![A-Z])(?:APARTMENT|APT|SUITE|STE|UNIT|#)(?![A-Z])\s*")]
    private static partial Regex UnitDesignator();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
