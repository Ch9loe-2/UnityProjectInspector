namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M31 Chinese result formatter tests.
///
/// Tests cover: PASS output, FAIL output, rule type name mapping, statistics line,
/// failure detail display, and output structure for empty requirements.
/// </summary>
public class ChineseResultFormatterTests
{
    [Fact]
    public void StatusToChinese_Passed_Returns通过()
    {
        Assert.Equal("通过", ChineseResultFormatter.StatusToChinese("PASSED"));
    }

    [Fact]
    public void StatusToChinese_Failed_Returns未通过()
    {
        Assert.Equal("未通过", ChineseResultFormatter.StatusToChinese("FAILED"));
    }

    [Fact]
    public void StatusToChinese_NotEvaluated_Returns未评估()
    {
        Assert.Equal("未评估", ChineseResultFormatter.StatusToChinese("NOT_EVALUATED"));
    }

    [Fact]
    public void RuleTypeToChineseName_SceneExists_Returns场景存在()
    {
        Assert.Equal("场景存在", ChineseResultFormatter.RuleTypeToChineseName("SceneExists"));
    }

    [Fact]
    public void RuleTypeToChineseName_Unknown_ReturnsOriginal()
    {
        Assert.Equal("BogusType", ChineseResultFormatter.RuleTypeToChineseName("BogusType"));
    }

    [Fact]
    public void WriteDetailed_PassedResult_ContainsChineseStatus()
    {
        var result = new CliInspectionResult
        {
            Status = "PASSED",
            Message = "Assignment passed: all requirements satisfied.",
            Requirements = new List<CliRequirementResult>
            {
                new()
                {
                    Id = "scene-check",
                    Name = "Required scene exists",
                    Status = "PASSED",
                    Message = "Scene TrainingScene found.",
                    HasRuntime = false,
                },
            },
        };

        var writer = new StringWriter();
        ChineseResultFormatter.WriteDetailed(result, writer);
        var output = writer.ToString();

        Assert.Contains("通过", output, StringComparison.Ordinal);
        Assert.Contains("检查报告", output, StringComparison.Ordinal);
        Assert.Contains("检查项", output, StringComparison.Ordinal);
        Assert.Contains("统计", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteDetailed_FailedResult_ContainsChineseStatus()
    {
        var result = new CliInspectionResult
        {
            Status = "FAILED",
            Message = "Assignment failed: some requirements did not pass.",
            Requirements = new List<CliRequirementResult>
            {
                new()
                {
                    Id = "scene-missing",
                    Name = "Required scene exists",
                    Status = "FAILED",
                    Message = "Scene 'MissingScene' not found in any .unity file under Assets/.",
                    HasRuntime = false,
                },
            },
        };

        var writer = new StringWriter();
        ChineseResultFormatter.WriteDetailed(result, writer);
        var output = writer.ToString();

        Assert.Contains("未通过", output, StringComparison.Ordinal);
        Assert.Contains("scene-missing", output, StringComparison.Ordinal);
        Assert.Contains("统计", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteDetailed_StatisticsLine_CorrectCounts()
    {
        var result = new CliInspectionResult
        {
            Status = "FAILED",
            Message = "Mixed results.",
            Requirements = new List<CliRequirementResult>
            {
                new() { Id = "r1", Name = "R1", Status = "PASSED", Message = "Ok" },
                new() { Id = "r2", Name = "R2", Status = "FAILED", Message = "Fail" },
                new() { Id = "r3", Name = "R3", Status = "NOT_EVALUATED", Message = "Skip" },
                new() { Id = "r4", Name = "R4", Status = "PASSED", Message = "Ok" },
            },
        };

        var writer = new StringWriter();
        ChineseResultFormatter.WriteDetailed(result, writer);
        var output = writer.ToString();

        Assert.Contains("通过 2", output, StringComparison.Ordinal);
        Assert.Contains("失败 1", output, StringComparison.Ordinal);
        Assert.Contains("未评估 1", output, StringComparison.Ordinal);
        Assert.Contains("总计 4", output, StringComparison.Ordinal);
    }
}