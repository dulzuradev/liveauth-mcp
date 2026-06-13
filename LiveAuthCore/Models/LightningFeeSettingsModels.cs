namespace LiveAuthCore.Models;

public record LightningFeeSettingsResponse(
    int InvoiceFeeBasisPoints,
    long InvoiceMinimumFeeSats,
    int BundleMarkupBasisPoints,
    long BundleMarkupMinimumFeeSats,
    int McpPaidToolFeeBasisPoints,
    long McpPaidToolMinimumFeeSats,
    DateTime? UpdatedAt);

public class UpdateLightningFeeSettingsRequest
{
    public int InvoiceFeeBasisPoints { get; set; }
    public long InvoiceMinimumFeeSats { get; set; }
    public int BundleMarkupBasisPoints { get; set; }
    public long BundleMarkupMinimumFeeSats { get; set; }
    public int? McpPaidToolFeeBasisPoints { get; set; }
    public long? McpPaidToolMinimumFeeSats { get; set; }
}
