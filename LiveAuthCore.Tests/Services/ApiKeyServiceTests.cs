using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LiveAuthCore.Tests.Services;

public class ApiKeyServiceTests
{
    [Fact]
    public async Task AuthenticateProjectAsync_ScansApiKeyCandidatesUntilSecretMatches()
    {
        var db = CreateDbContext();
        var firstProject = CreateProject("First Project");
        var secondProject = CreateProject("Second Project");
        var hasher = new PasswordHasher<Project>();
        var firstSecret = $"la_sk_first_{Guid.NewGuid():N}";
        var secondSecret = $"la_sk_second_{Guid.NewGuid():N}";
        var firstKey = CreateApiKey(firstProject, hasher.HashPassword(firstProject, firstSecret));
        var secondKey = CreateApiKey(secondProject, hasher.HashPassword(secondProject, secondSecret));
        db.Projects.AddRange(firstProject, secondProject);
        db.ProjectApiKeys.AddRange(firstKey, secondKey);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var project = await service.AuthenticateProjectAsync(secondSecret);

        project.Should().NotBeNull();
        project!.Id.Should().Be(secondProject.Id);
        firstKey.LastUsedAt.Should().BeNull();
        secondKey.LastUsedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthenticateProjectAsync_ScansLegacyProjectHashesUntilSecretMatches()
    {
        var db = CreateDbContext();
        var firstProject = CreateProject("First Legacy Project");
        var secondProject = CreateProject("Second Legacy Project");
        var hasher = new PasswordHasher<Project>();
        var firstSecret = $"la_sk_first_legacy_{Guid.NewGuid():N}";
        var secondSecret = $"la_sk_second_legacy_{Guid.NewGuid():N}";
        firstProject.SecretKeyHash = hasher.HashPassword(firstProject, firstSecret);
        secondProject.SecretKeyHash = hasher.HashPassword(secondProject, secondSecret);
        db.Projects.AddRange(firstProject, secondProject);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var project = await service.AuthenticateProjectAsync(secondSecret);

        project.Should().NotBeNull();
        project!.Id.Should().Be(secondProject.Id);
    }

    private static ApiKeyService CreateService(LiveAuthDbContext db)
    {
        return new ApiKeyService(
            db,
            new AuthEventService(db, new HttpContextAccessor()));
    }

    private static LiveAuthDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LiveAuthDbContext>()
            .UseInMemoryDatabase($"ApiKeyServiceTests_{Guid.NewGuid():N}")
            .Options;

        return new LiveAuthDbContext(options);
    }

    private static Project CreateProject(string name)
    {
        return new Project
        {
            Id = Guid.NewGuid(),
            DeveloperId = Guid.NewGuid(),
            Name = name,
            PublicKey = $"la_pk_{Guid.NewGuid():N}",
            SecretKeyHash = string.Empty,
            IsActive = true,
            Environment = "LIVE",
            Plan = "free",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ProjectApiKey CreateApiKey(Project project, string secretHash)
    {
        return new ProjectApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Project = project,
            PublicKey = $"la_pk_{Guid.NewGuid():N}",
            SecretKeyHash = secretHash,
            Label = "test",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
    }
}
