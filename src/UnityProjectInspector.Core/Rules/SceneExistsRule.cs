using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Checks whether a scene with the given name exists in the project.
///
/// Input: scene name (e.g. "Maze2D", "MainMenu")
/// Passes when a scene with that name is found in the project.
/// Fails when no scene matches the name.
/// </summary>
public class SceneExistsRule : IRule
{
    public string Id => "SCENE_EXISTS";
    public string Name => "Scene Exists";

    /// <summary>
    /// The exact scene name to check (case-sensitive, without .unity extension).
    /// </summary>
    public string SceneName { get; }

    public SceneExistsRule(string sceneName)
    {
        SceneName = sceneName;
    }

    public RuleResult Evaluate(InspectionContext context)
    {
        if (context.ProjectInfo.Scenes.Count == 0)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"Scene '{SceneName}' not found — no scenes in project",
            };
        }

        var scene = context.FindScene(SceneName);

        if (scene == null)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"Scene '{SceneName}' not found in project",
            };
        }

        return new RuleResult
        {
            RuleId = Id,
            RuleName = Name,
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = $"Scene '{SceneName}' exists",
        };
    }
}