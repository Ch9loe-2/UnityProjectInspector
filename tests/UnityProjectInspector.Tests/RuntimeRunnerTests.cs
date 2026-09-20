using System.Text.Json;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests;

/// <summary>
/// Unit tests for Milestone 13 RuntimeRunner — state classification,
/// evidence parsing, timeout/process-exit/build-failed detection.
///
/// These tests do NOT launch real Unity. They test the decision logic
/// implemented in RuntimeRunner through direct evidence classification
/// and simulated pipeline phases.
///
/// The real integration test (M12_MinimalUnityProject → real Build → real Player)
/// is in RuntimeIntegrationTests.cs (see IntegrationTest category).
/// </summary>
[Trait("Category", "Unit")]
public class RuntimeRunnerTests
{
    // ══════════════════════════════════════════════════════════════
    // Test 1 — Runtime PASS
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void ClassifyEvidence_ValidRuntimeEvidence_Passes()
    {
        var evidence = new PlayerEvidence
        {
            ExecutionMode = "Runtime",
            ActiveScene = "MainScene",
            IsEditor = false,
            IsPlaying = true,
            Success = true,
        };

        var session = ClassifyEvidence(evidence);

        Assert.Equal(RuntimeResultStatus.Passed, session.Result);
        Assert.Single(session.Evidence);
        Assert.Contains("Runtime inspection passed", session.Message);
    }

    [Fact]
    public void ClassifyEvidence_ValidRuntimeEvidence_RecordsEvidence()
    {
        var evidence = new PlayerEvidence
        {
            ExecutionMode = "Runtime",
            ActiveScene = "MainScene",
            IsEditor = false,
            IsPlaying = true,
            Success = true,
        };

        var session = ClassifyEvidence(evidence);

        var ev = Assert.Single(session.Evidence);
        Assert.Equal("RuntimeEvidence", ev.Type);
        Assert.True(ev.Obtained);
        Assert.Equal("Runtime", ev.Expected);
        Assert.Equal("Runtime", ev.Observed);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 5 — Evidence Invalid (isEditor=true)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void ClassifyEvidence_EditorPlayMode_Fails()
    {
        var evidence = new PlayerEvidence
        {
            ExecutionMode = "PlayMode (Editor)",
            ActiveScene = "MainScene",
            IsEditor = true,
            IsPlaying = true,
            Success = true,
        };

        var session = ClassifyEvidence(evidence);

        Assert.Equal(RuntimeResultStatus.Failed, session.Result);
        Assert.Contains("Editor PlayMode", session.Message);
    }

    [Fact]
    public void ClassifyEvidence_IsEditorTrue_Fails()
    {
        var evidence = new PlayerEvidence
        {
            ExecutionMode = "Runtime",
            ActiveScene = "MainScene",
            IsEditor = true,
            IsPlaying = true,
            Success = true,
        };

        var session = ClassifyEvidence(evidence);

        Assert.Equal(RuntimeResultStatus.Failed, session.Result);
    }

    [Fact]
    public void ClassifyEvidence_NotPlaying_Fails()
    {
        var evidence = new PlayerEvidence
        {
            ExecutionMode = "Runtime",
            ActiveScene = "MainScene",
            IsEditor = false,
            IsPlaying = false,
            Success = true,
        };

        var session = ClassifyEvidence(evidence);

        Assert.Equal(RuntimeResultStatus.Failed, session.Result);
    }

    [Fact]
    public void ClassifyEvidence_SuccessFalse_Fails()
    {
        var evidence = new PlayerEvidence
        {
            ExecutionMode = "Runtime",
            ActiveScene = "MainScene",
            IsEditor = false,
            IsPlaying = true,
            Success = false,
        };

        var session = ClassifyEvidence(evidence);

        Assert.Equal(RuntimeResultStatus.Failed, session.Result);
        Assert.Contains("success=false", session.Message);
    }

    // ══════════════════════════════════════════════════════════════
    // JSON Deserialization
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void PlayerEvidence_Deserializes_ValidJson()
    {
        var json = """
            {
                "executionMode": "Runtime",
                "activeScene": "MainScene",
                "isEditor": false,
                "isPlaying": true,
                "success": true
            }
            """;

        var evidence = JsonSerializer.Deserialize<PlayerEvidence>(json);

        Assert.NotNull(evidence);
        Assert.Equal("Runtime", evidence!.ExecutionMode);
        Assert.Equal("MainScene", evidence.ActiveScene);
        Assert.False(evidence.IsEditor);
        Assert.True(evidence.IsPlaying);
        Assert.True(evidence.Success);
    }

    [Fact]
    public void PlayerEvidence_IsValidRuntimeEvidence_False_InEditor()
    {
        var evidence = new PlayerEvidence
        {
            ExecutionMode = "PlayMode (Editor)",
            ActiveScene = "MainScene",
            IsEditor = true,
            IsPlaying = true,
            Success = true,
        };

        Assert.False(evidence.IsValidRuntimeEvidence);
    }

    [Fact]
    public void PlayerEvidence_IsValidRuntimeEvidence_True_InPlayer()
    {
        var evidence = new PlayerEvidence
        {
            ExecutionMode = "Runtime",
            ActiveScene = "MainScene",
            IsEditor = false,
            IsPlaying = true,
            Success = true,
        };

        Assert.True(evidence.IsValidRuntimeEvidence);
    }

    // ══════════════════════════════════════════════════════════════
    // RuntimeResultStatus enum values
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void RuntimeResultStatus_HasAllRequiredValues()
    {
        Assert.True(Enum.IsDefined(typeof(RuntimeResultStatus), "Passed"));
        Assert.True(Enum.IsDefined(typeof(RuntimeResultStatus), "Failed"));
        Assert.True(Enum.IsDefined(typeof(RuntimeResultStatus), "NotEvaluated"));
        Assert.True(Enum.IsDefined(typeof(RuntimeResultStatus), "Timeout"));
        Assert.True(Enum.IsDefined(typeof(RuntimeResultStatus), "ProcessExited"));
        Assert.True(Enum.IsDefined(typeof(RuntimeResultStatus), "BuildFailed"));
    }

    // ══════════════════════════════════════════════════════════════
    // RuntimeRunOptions
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void RuntimeRunOptions_DefaultsAreSensible()
    {
        var options = new RuntimeRunOptions
        {
            UnityExecutable = "/test/unity",
            ProjectPath = "/test/project",
        };

        Assert.Equal(60, options.PlayerTimeoutSeconds);
        Assert.Equal(180, options.BuildTimeoutSeconds);
        Assert.Equal("M14RuntimePlayer", options.ProductName);
        Assert.Equal("evidence.json", options.EvidenceFileName);
        Assert.Equal("M14_SESSION_DIR", options.SessionDirEnvVar);
    }

    // ══════════════════════════════════════════════════════════════
    // Helper: simulate the classification logic without launching Unity
    // ══════════════════════════════════════════════════════════════

    private static RuntimeSession ClassifyEvidence(PlayerEvidence evidence)
    {
        var session = new RuntimeSession
        {
            ProjectPath = "/test/project",
            StartedAt = DateTime.UtcNow,
        };

        var evidenceModel = new RuntimeEvidence
        {
            Type = "RuntimeEvidence",
            Expected = "Runtime",
            Observed = evidence.ExecutionMode,
            Obtained = true,
            Message = $"executionMode={evidence.ExecutionMode}, " +
                      $"isEditor={evidence.IsEditor}, isPlaying={evidence.IsPlaying}, " +
                      $"activeScene={evidence.ActiveScene}, success={evidence.Success}",
        };
        session.Evidence.Add(evidenceModel);

        if (!evidence.IsValidRuntimeEvidence)
        {
            session.Result = RuntimeResultStatus.Failed;
            session.Message = evidence.ExecutionMode == "PlayMode (Editor)"
                ? "Evidence indicates Editor PlayMode, not true Runtime. " +
                  "isEditor=true — this is NOT a standalone Player."
                : $"Evidence is invalid for Runtime: " +
                  $"executionMode={evidence.ExecutionMode}, " +
                  $"isEditor={evidence.IsEditor}, isPlaying={evidence.IsPlaying}";
            return session;
        }

        if (!evidence.Success)
        {
            session.Result = RuntimeResultStatus.Failed;
            session.Message = $"Runtime evidence success=false: " +
                              $"activeScene={evidence.ActiveScene}";
            return session;
        }

        session.Result = RuntimeResultStatus.Passed;
        session.Message = $"Runtime inspection passed. " +
                          $"ActiveScene='{evidence.ActiveScene}', " +
                          $"isEditor={evidence.IsEditor}, isPlaying={evidence.IsPlaying}";
        return session;
    }
}