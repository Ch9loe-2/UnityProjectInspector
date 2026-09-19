using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Converts a RuleDefinition into an IRule instance.
///
/// Mapping:
///   SceneExists        → SceneExistsRule(Target)
///   GameObjectExists   → GameObjectExistsRule(Target)
///   ComponentExists    → ComponentExistsRule(Target, ExpectedClass)
///   UnityEventBinding  → UnityEventBindingRule(Target, ExpectedMethod, ExpectedClass)
///   CodeEvidence       → CodeEvidenceRule(Target, ExpectedMethod)
///
/// The factory does NOT validate the definition against a Unity project.
/// It only constructs the IRule with the parameters from the definition.
/// Evaluation happens later when RuleEngine.Run() is called.
/// </summary>
public static class RuleFactory
{
    public static IRule Create(RuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.Type switch
        {
            "SceneExists" => CreateSceneExists(definition),
            "GameObjectExists" => CreateGameObjectExists(definition),
            "ComponentExists" => CreateComponentExists(definition),
            "UnityEventBinding" => CreateUnityEventBinding(definition),
            "CodeEvidence" => CreateCodeEvidence(definition),
            _ => throw new ArgumentException(
                $"Unknown rule type '{definition.Type}'. Supported types: SceneExists, " +
                "GameObjectExists, ComponentExists, UnityEventBinding, CodeEvidence")
        };
    }

    private static SceneExistsRule CreateSceneExists(RuleDefinition def)
    {
        var target = def.Target
            ?? throw new ArgumentException("SceneExists rule requires 'target' (scene name)");

        return new SceneExistsRule(target);
    }

    private static GameObjectExistsRule CreateGameObjectExists(RuleDefinition def)
    {
        var target = def.Target
            ?? throw new ArgumentException("GameObjectExists rule requires 'target' (GameObject name)");

        return new GameObjectExistsRule(target);
    }

    private static ComponentExistsRule CreateComponentExists(RuleDefinition def)
    {
        var target = def.Target
            ?? throw new ArgumentException("ComponentExists rule requires 'target' (GameObject name)");
        var componentType = def.ExpectedClass
            ?? throw new ArgumentException("ComponentExists rule requires 'expectedClass' (component type)");

        return new ComponentExistsRule(target, componentType);
    }

    private static UnityEventBindingRule CreateUnityEventBinding(RuleDefinition def)
    {
        var source = def.Target
            ?? throw new ArgumentException("UnityEventBinding rule requires 'target' (source GameObject name)");
        var methodName = def.ExpectedMethod
            ?? throw new ArgumentException("UnityEventBinding rule requires 'expectedMethod'");
        var className = def.ExpectedClass;

        return new UnityEventBindingRule(source, methodName, className);
    }

    private static CodeEvidenceRule CreateCodeEvidence(RuleDefinition def)
    {
        var source = def.Target
            ?? throw new ArgumentException("CodeEvidence rule requires 'target' (source GameObject name)");
        var methodName = def.ExpectedMethod
            ?? throw new ArgumentException("CodeEvidence rule requires 'expectedMethod'");

        // CodeEvidenceRule constructor: (sourceGameObjectName, targetMethodName, expectedCallTarget?, expectedArgument?)
        // For JSON-based usage, we only verify the method exists — no expected call or argument by default.
        return new CodeEvidenceRule(source, methodName);
    }
}