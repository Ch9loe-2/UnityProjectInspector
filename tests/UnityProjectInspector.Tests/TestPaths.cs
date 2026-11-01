using System;
using System.IO;

namespace UnityProjectInspector.Tests;

/// <summary>
/// Resolves test fixtures from the repository root instead of hard-coded
/// absolute paths, so the test suite runs on any machine after a clone.
/// </summary>
internal static class TestPaths
{
    /// <summary>Repository root (directory containing .git / solution file).</summary>
    public static string RepoRoot { get; }

    /// <summary>
    /// A non-trivial, real-world Unity project fixture checked into the repo
    /// (tests/fixtures/RealWorldUnityProject), used by integration tests that
    /// previously pointed at a machine-local clone of 3DIndustrialMonitor.
    /// </summary>
    public static string RealWorldProject =>
        Path.Combine(RepoRoot, "tests", "fixtures", "RealWorldUnityProject");

    static TestPaths()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, "UnityProjectInspector.slnx")))
            {
                RepoRoot = dir.FullName;
                return;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Unable to locate repository root from test output directory: " +
            AppContext.BaseDirectory);
    }
}
