using System.Diagnostics;

namespace UnityProjectInspector.Core.Trace;

/// <summary>
/// Thread-safe collector for <see cref="WorkflowStage"/> records.
///
/// Used by <see cref="Assignments.InspectionWorkflowRunner"/> and
/// <see cref="Runtime.RuntimeRunner"/> to record Core-level pipeline stages.
/// The CLI layer records its own stages separately and merges them at format time.
/// </summary>
public class WorkflowStageCollector
{
    private readonly List<WorkflowStage> _stages = new();
    private readonly object _lock = new();

    /// <summary>A snapshot of all stages recorded so far, in insertion order.</summary>
    public IReadOnlyList<WorkflowStage> Stages
    {
        get
        {
            lock (_lock)
                return _stages.ToArray();
        }
    }

    /// <summary>
    /// Adds a stage directly. Use when the caller has already computed duration/status.
    /// </summary>
    public void Add(WorkflowStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        lock (_lock)
            _stages.Add(stage);
    }

    /// <summary>
    /// Records a stage by wrapping an async operation with Stopwatch timing.
    /// On success → status "passed". On exception → status "failed" with Message.
    /// </summary>
    public async Task<T> RecordAsync<T>(string name, Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            Add(new WorkflowStage
            {
                Name = name,
                Status = "passed",
                DurationMs = sw.ElapsedMilliseconds,
            });
            return result;
        }
        catch (Exception ex)
        {
            Add(new WorkflowStage
            {
                Name = name,
                Status = "failed",
                DurationMs = sw.ElapsedMilliseconds,
                Message = ex.Message,
            });
            throw;
        }
    }

    /// <summary>
    /// Records a void async stage (no return value).
    /// </summary>
    public async Task RecordAsync(string name, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var sw = Stopwatch.StartNew();
        try
        {
            await action();
            Add(new WorkflowStage
            {
                Name = name,
                Status = "passed",
                DurationMs = sw.ElapsedMilliseconds,
            });
        }
        catch (Exception ex)
        {
            Add(new WorkflowStage
            {
                Name = name,
                Status = "failed",
                DurationMs = sw.ElapsedMilliseconds,
                Message = ex.Message,
            });
            throw;
        }
    }

    /// <summary>
    /// Records a synchronous stage (no Task wrapper needed).
    /// </summary>
    public T Record<T>(string name, Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = func();
            Add(new WorkflowStage
            {
                Name = name,
                Status = "passed",
                DurationMs = sw.ElapsedMilliseconds,
            });
            return result;
        }
        catch (Exception ex)
        {
            Add(new WorkflowStage
            {
                Name = name,
                Status = "failed",
                DurationMs = sw.ElapsedMilliseconds,
                Message = ex.Message,
            });
            throw;
        }
    }

    /// <summary>
    /// Records a skipped stage (runtime-only stage on a static-only path).
    /// </summary>
    public void RecordSkipped(string name)
    {
        Add(new WorkflowStage
        {
            Name = name,
            Status = "skipped",
            DurationMs = 0,
        });
    }
}