namespace LiveAuthCore.Models;

public sealed class RotateSecretResponse
{
    public required Guid ProjectId { get; set; }
    public required string PublicKey { get; set; }
    public required string SecretKey { get; set; }
    public required DateTime RotatedAt { get; set; }
}