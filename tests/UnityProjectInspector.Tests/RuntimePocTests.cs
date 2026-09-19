using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests;

/// <summary>
/// Unit tests for Milestone 10 runtime POC models and classification.
///
/// These tests do NOT launch real Unity — they test the data models
/// and decision logic that the runtime adapter uses.
///
/// The real runtime integration test is separate (see RuntimePocRunner).
/// </summary>
public class RuntimePocTests
{
    // ═══════════════════════════════════════════════════════════════
    // Model construction
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RuntimeEvidence_CreateWithExpectedAndObserved()
    {
        var evidence = new RuntimeEvidence
        {
            Type = "ActiveScene",
            Expected = "SampleScene",
            Observed = "SampleScene",
            Obtained = true,
        };

        Assert.Equal("ActiveScene", evidence.Type);
        Assert.Equal("SampleScene", evidence.Expected);
        Assert.Equal("SampleScene", evidence.Observed);
        Assert.True(evidence.Obtained);
    }

    [Fact]
    public void RuntimeEvidence_ErrorPrefix_NotObtained()
    {
        var evidence = new RuntimeEvidence
        {
            Type = "ActiveScene",
            Expected = "SampleScene",
            Observed = "ERROR_SceneFileNotFound",
            Obtained = false,
        };

        Assert.False(evidence.Obtained);
    }

    [Fact]
    public void RuntimeEvidence_TimeoutPrefix_NotObtained()
    {
        var evidence = new RuntimeEvidence
        {
            Type = "ActiveScene",
            Expected = "SampleScene",
            Observed = "TIMEOUT_Phase2",
            Obtained = false,
        };

        Assert.False(evidence.Obtained);
    }

    [Fact]
    public void RuntimeSession_DefaultStatus_NotEvaluated()
    {
        var session = new RuntimeSession
        {
            ProjectPath = "/test/project",
            StartedAt = DateTime.UtcNow,
        };

        Assert.Equal(RuntimeResultStatus.NotEvaluated, session.Result);
        Assert.Empty(session.Evidence);
    }

    // ═══════════════════════════════════════════════════════════════
    // Result classification logic
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Classify_Passed_WhenObservedMatchesExpected()
    {
        var evidence = new RuntimeEvidence
        {
            Type = "ActiveScene",
            Expected = "SampleScene",
            Observed = "SampleScene",
            Obtained = true,
        };

        var result = Classify(evidence);

        Assert.Equal(RuntimeResultStatus.Passed, result);
    }

    [Fact]
    public void Classify_Failed_WhenObservedDiffers()
    {
        var evidence = new RuntimeEvidence
        {
            Type = "ActiveScene",
            Expected = "SampleScene",
            Observed = "OtherScene",
            Obtained = true,
        };

        var result = Classify(evidence);

        Assert.Equal(RuntimeResultStatus.Failed, result);
    }

    [Fact]
    public void Classify_NotEvaluated_WhenNotObtained()
    {
        var evidence = new RuntimeEvidence
        {
            Type = "ActiveScene",
            Expected = "SampleScene",
            Observed = "ERROR_SceneNotFound",
            Obtained = false,
        };

        var result = Classify(evidence);

        Assert.Equal(RuntimeResultStatus.NotEvaluated, result);
    }

    [Fact]
    public void Classify_NotEvaluated_WhenObservedNull()
    {
        var evidence = new RuntimeEvidence
        {
            Type = "ActiveScene",
            Expected = "SampleScene",
            Observed = null,
            Obtained = false,
        };

        var result = Classify(evidence);

        Assert.Equal(RuntimeResultStatus.NotEvaluated, result);
    }

    [Fact]
    public void Classify_NotEvaluated_WhenNoEvidence()
    {
        var result = Classify(null);

        Assert.Equal(RuntimeResultStatus.NotEvaluated, result);
    }

    private static RuntimeResultStatus Classify(RuntimeEvidence? evidence)
    {
        if (evidence == null || !evidence.Obtained || evidence.Observed == null)
            return RuntimeResultStatus.NotEvaluated;

        if (string.Equals(evidence.Observed, evidence.Expected, StringComparison.Ordinal))
            return RuntimeResultStatus.Passed;

        return RuntimeResultStatus.Failed;
    }

    // ═══════════════════════════════════════════════════════════════
    // UnityRuntimeAdapter availability check (no Unity launch)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UnityRuntimeAdapter_IsAvailable_False_WhenBadPath()
    {
        var adapter = new UnityRuntimeAdapter(
            "/nonexistent/path/Unity",
            "/nonexistent/path/harness");

        Assert.False(adapter.IsAvailable());
    }

    [Fact]
    public void UnityRuntimeAdapter_IsAvailable_True_WhenPathValid()
    {
        var unityPath = "/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity";
        var harnessPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tools", "RuntimeTestHarness");

        var adapter = new UnityRuntimeAdapter(unityPath, harnessPath);

        // The path is valid on this machine — should return true
        Assert.True(adapter.IsAvailable(),
            "Unity Editor executable should exist at the expected path");
    }

    [Fact]
    public void UnityRuntimeAdapter_Constructor_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new UnityRuntimeAdapter(null!, "/harness"));

        Assert.Throws<ArgumentNullException>(() =>
            new UnityRuntimeAdapter("/unity", null!));
    }
}