using Xunit;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LiveAuthCore.Tests.Security;

/// <summary>
/// Security regression tests to ensure sensitive files are not committed to the repository.
/// </summary>
public class SensitiveFilesNotCommittedTests
{
    private const string PrivateKeyPattern = @"-----BEGIN (RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----";
    private readonly string _repoRoot;

    public SensitiveFilesNotCommittedTests()
    {
        _repoRoot = FindRepoRoot();
    }

    /// <summary>
    /// Verifies that SSH private keys are not present in the repository.
    /// This is a critical security check - private keys should never be committed.
    /// </summary>
    [Fact]
    public void NoSshPrivateKeysInRepository()
    {
        // Check that the private key file does not exist
        var privateKeyPath = Path.Combine(_repoRoot, "LiveAuthCore", "Services", "liveAuth_key");
        
        Assert.False(
            File.Exists(privateKeyPath),
            $"SSH private key found at {privateKeyPath}. Private keys should never be committed to the repository."
        );
    }

    /// <summary>
    /// Verifies that SSH private keys are not in git history.
    /// This test runs a git command to check for any private keys in history.
    /// </summary>
    [Fact]
    public void NoSshPrivateKeysInGitHistory()
    {
        // Run git grep to check for private key patterns in object database
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "grep -I -n -E \"BEGIN (RSA |EC |DSA |OPENSSH )?PRIVATE KEY\" HEAD -- . \":(exclude)LiveAuthCore.Tests/Security/SensitiveFilesNotCommittedTests.cs\"",
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // If grep finds matches, it exits with 0; we want non-zero (no matches)
        Assert.NotEqual(0, process.ExitCode);
    }

    /// <summary>
    /// Verifies that the private key file is in .gitignore.
    /// </summary>
    [Fact]
    public void PrivateKeyInGitIgnore()
    {
        var gitignorePath = Path.Combine(_repoRoot, ".gitignore");
        
        Assert.True(
            File.Exists(gitignorePath),
            ".gitignore file should exist"
        );

        var gitignoreContent = File.ReadAllText(gitignorePath);
        
        Assert.Contains(
            "liveAuth_key",
            gitignoreContent,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) &&
                File.Exists(Path.Combine(directory.FullName, ".gitignore")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test binary path.");
    }
}
