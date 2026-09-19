using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests;

/// <summary>
/// Standalone runner for the real Unity runtime proof of concept.
///
/// This is intentionally NOT a normal [Fact] — it has heavyweight
/// dependencies (Unity installation, 30–120s launch time) and must
/// be invoked explicitly, not as part of `dotnet test`.
///
/// Usage:
///   dotnet run --project tests/RuntimePocRunner -- --scene <path>
///
/// Or via script:
///   tools/run_runtime_poc.sh
/// </summary>
public static class RuntimePocRunner
{
    private const string DefaultUnityPath =
        "/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity";

    private static readonly string DefaultHarnessPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..", "..", "..", "..", "..",
        "tools", "RuntimeTestHarness");

    private const string DefaultProjectRoot =
        "/Users/tangluyi/Documents/GitHub/3DIndustrialMonitor";

    private const string DefaultScenePath =
        "/Users/tangluyi/Documents/GitHub/3DIndustrialMonitor/Assets/Scenes/SampleScene.unity";

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== UnityProjectInspector Runtime POC ===\n");

        // Parse args
        var scenePath = DefaultScenePath;
        var expectedScene = "ExportDeviceButton";
        var unityPath = DefaultUnityPath;
        var harnessPath = DefaultHarnessPath;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--scene" && i + 1 < args.Length) scenePath = args[i + 1];
            if (args[i] == "--unity" && i + 1 < args.Length) unityPath = args[i + 1];
            if (args[i] == "--harness" && i + 1 < args.Length) harnessPath = args[i + 1];
        }

        // Validate
        if (!File.Exists(scenePath))
        {
            Console.WriteLine($"❌ Scene not found: {scenePath}");
            return 1;
        }

        if (!File.Exists(unityPath))
        {
            Console.WriteLine($"❌ Unity Editor not found: {unityPath}");
            Console.WriteLine("   Install Unity 2022.3.62f3c1 via Unity Hub.");
            return 1;
        }

        if (!Directory.Exists(harnessPath) || !Directory.Exists(Path.Combine(harnessPath, "Assets")))
        {
            Console.WriteLine($"❌ RuntimeTestHarness project not found at: {harnessPath}");
            Console.WriteLine("   Expected at: tools/RuntimeTestHarness/");
            return 1;
        }

        var adapter = new UnityRuntimeAdapter(unityPath, harnessPath);

        Console.WriteLine($"Unity Editor:      {unityPath}");
        Console.WriteLine($"Harness Project:   {harnessPath}");
        Console.WriteLine($"Target Scene:      {scenePath}");
        Console.WriteLine($"Expected Scene:    {expectedScene}");
        Console.WriteLine();

        // Check target project safety
        Console.WriteLine("Checking target project safety...");
        var beforeStatus = GetGitStatus();
        Console.WriteLine($"  Before: {beforeStatus}");

        // Run
        Console.WriteLine("\nLaunching Unity Editor (may take 30–120s for cold start)...");
        Console.WriteLine();

        var config = new UnityRuntimeAdapter.RuntimeTestConfig
        {
            ScenePath = scenePath,
            ExpectedScene = expectedScene,
            TimeoutSeconds = 240,
        };

        var session = await adapter.RunAsync(config);

        // Report
        Console.WriteLine("\n=== Runtime POC Results ===");
        Console.WriteLine($"Result:           {session.Result}");
        Console.WriteLine($"Message:          {session.Message}");

        foreach (var evidence in session.Evidence)
        {
            Console.WriteLine($"\nEvidence:");
            Console.WriteLine($"  Type:           {evidence.Type}");
            Console.WriteLine($"  Expected:       {evidence.Expected}");
            Console.WriteLine($"  Observed:       {evidence.Observed ?? "(none)"}");
            Console.WriteLine($"  Obtained:       {evidence.Obtained}");
            Console.WriteLine($"  Message:        {evidence.Message}");
        }

        // Final target project safety check
        Console.WriteLine("\nChecking target project safety...");
        var afterStatus = GetGitStatus();
        Console.WriteLine($"  After:  {afterStatus}");

        if (beforeStatus == afterStatus)
        {
            Console.WriteLine("  ✅ Target project NOT modified by Inspector");
        }
        else
        {
            Console.WriteLine("  ⚠️  Target project status changed! Investigating...");
            Console.WriteLine($"  Before: {beforeStatus}");
            Console.WriteLine($"  After:  {afterStatus}");
        }

        Console.WriteLine("\n=== POC Complete ===");
        return session.Result == RuntimeResultStatus.Passed ? 0 : 1;
    }

    private static string GetGitStatus()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"-C \"{DefaultProjectRoot}\" status --short",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return "(unknown)";
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10000);
            return string.IsNullOrWhiteSpace(output) ? "clean" : output.Trim();
        }
        catch
        {
            return "(check failed)";
        }
    }
}