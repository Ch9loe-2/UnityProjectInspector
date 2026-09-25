namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M31 Interactive CLI tests.
///
/// Tests cover: SanitizeId helper, entry-point behavior (interactive mode
/// triggers on empty args), regression (normal inspect still works),
/// and output format of helper methods.
/// </summary>
public class InteractiveCliTests
{
    // ─── SanitizeId ───────────────────────────────────────────

    [Fact]
    public void SanitizeId_NormalName_ReturnsLower()
    {
        Assert.Equal("trainingscene", InteractiveCli.SanitizeId("TrainingScene"));
    }

    [Fact]
    public void SanitizeId_WithSpaces_ReplacesWithHyphen()
    {
        Assert.Equal("main-camera", InteractiveCli.SanitizeId("Main Camera"));
    }

    [Fact]
    public void SanitizeId_SpecialChars_Replaced()
    {
        Assert.Equal("player-controller-1", InteractiveCli.SanitizeId("Player_Controller#1"));
    }

    [Fact]
    public void SanitizeId_AlreadyLower_StaysLower()
    {
        Assert.Equal("abc123", InteractiveCli.SanitizeId("abc123"));
    }

    [Fact]
    public void SanitizeId_LeadingTrailingSpecialChars_Trimmed()
    {
        // Leading/trailing hyphens from special chars should be trimmed
        var result = InteractiveCli.SanitizeId("_MainScene_");
        Assert.Equal("mainscene", result);
    }

    // ─── Entry point regression: normal inspect still works ───

    [Fact]
    public async Task InteractiveCli_DoesNotInterfereWithExistingCli()
    {
        // Run with full inspect args — must bypass interactive mode
        var repoRoot = FindRepoRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "fixtures", "MinimalUnityProject");
        var assignment = Path.Combine(repoRoot, "examples", "assignments", "scene-check.json");

        var prevOut = Console.Out;
        var prevErr = Console.Error;
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            var exit = await Program.Main(new[]
            {
                "inspect", "--project", fixtureDir, "--assignment", assignment
            });
            Assert.Equal((int)CliExitCode.Passed, exit);
            Assert.Contains("PASSED", outWriter.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }

    [Fact]
    public async Task InteractiveCli_EmptyArgs_DoesNotThrow()
    {
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);

        // Mock stdin with "q" to quickly exit the interactive loop
        var prevIn = Console.In;
        try
        {
            using var sr = new StringReader("q");
            Console.SetIn(sr);
            var exit = await Program.Main(Array.Empty<string>());
            // With empty args + mock stdin "q", the interactive mode tries to
            // use "q" as a project path which doesn't exist → InvalidInput (exit 2)
            Assert.Equal((int)CliExitCode.InvalidInput, exit);
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
            Console.SetIn(prevIn);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(AppContext.BaseDirectory)!;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "UnityProjectInspector.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Repo root not found.");
    }
}