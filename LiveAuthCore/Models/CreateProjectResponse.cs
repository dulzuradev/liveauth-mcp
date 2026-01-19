namespace LiveAuthCore.Models;

public sealed class CreateProjectResponse
{
    public required Guid ProjectId { get; set; }
    public required string PublicKey { get; set; }
    public required string SecretKey { get; set; }
}