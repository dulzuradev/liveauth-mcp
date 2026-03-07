namespace LiveAuthCore.Tests.Mocks;

using LiveAuthCore.Services;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Mock Lightning service for testing - minimal implementation
/// </summary>
public class MockLightningService : LightningService
{
    public MockLightningService(IConfiguration configuration) : base(configuration)
    {
        // Set the private _useMock field via reflection if needed
    }
}
