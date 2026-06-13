using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

/// <summary>
/// Runtime-editable LiveAuth fee settings.
/// </summary>
public class LightningFeeSettings
{
    [Key]
    public int Id { get; set; } = 1;

    public int InvoiceFeeBasisPoints { get; set; } = 200;
    public long InvoiceMinimumFeeSats { get; set; } = 1;

    public int BundleMarkupBasisPoints { get; set; } = 1500;
    public long BundleMarkupMinimumFeeSats { get; set; } = 1;

    public int McpPaidToolFeeBasisPoints { get; set; } = 500;
    public long McpPaidToolMinimumFeeSats { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
