using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Services.CostShield;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LiveAuthCore.Tests.Services.CostShield;

public sealed class CostShieldTokenServiceTests
{
    [Fact]
    public void Production_requires_a_persistent_signing_key()
    {
        var act = () => CreateService(
            new Dictionary<string, string?>(),
            "Production");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SigningPrivateKeyPem*production*");
    }

    [Fact]
    public void Base64_encoded_private_key_is_loaded_with_configured_key_id()
    {
        using var rsa = RSA.Create(2048);
        var pemBase64 = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem()));

        using var service = CreateService(
            new Dictionary<string, string?>
            {
                ["CostShield:SigningPrivateKeyPemBase64"] = pemBase64,
                ["CostShield:SigningKeyId"] = "costshield-test-v2"
            },
            "Production");

        var key = service.GetJwks().Keys.Should().ContainSingle().Subject;
        key.Kid.Should().Be("costshield-test-v2");
        key.N.Should().NotBeNullOrWhiteSpace();
        key.E.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Weak_rsa_key_is_rejected()
    {
        using var rsa = RSA.Create(1024);

        var act = () => CreateService(
            new Dictionary<string, string?>
            {
                ["CostShield:SigningPrivateKeyPem"] =
                    rsa.ExportPkcs8PrivateKeyPem()
            },
            "Production");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*at least 2048 bits*");
    }

    [Fact]
    public void Public_key_without_private_material_is_rejected()
    {
        using var rsa = RSA.Create(2048);

        var act = () => CreateService(
            new Dictionary<string, string?>
            {
                ["CostShield:SigningPrivateKeyPem"] =
                    rsa.ExportSubjectPublicKeyInfoPem()
            },
            "Production");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*private key material*");
    }

    [Fact]
    public void Invalid_key_id_is_rejected()
    {
        using var rsa = RSA.Create(2048);

        var act = () => CreateService(
            new Dictionary<string, string?>
            {
                ["CostShield:SigningPrivateKeyPem"] =
                    rsa.ExportPkcs8PrivateKeyPem(),
                ["CostShield:SigningKeyId"] = "invalid key id"
            },
            "Production");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SigningKeyId*");
    }

    private static CostShieldTokenService CreateService(
        IReadOnlyDictionary<string, string?> values,
        string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new CostShieldTokenService(
            configuration,
            new TestWebHostEnvironment
            {
                EnvironmentName = environmentName
            },
            NullLogger<CostShieldTokenService>.Instance);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LiveAuthCore.Tests";
        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
