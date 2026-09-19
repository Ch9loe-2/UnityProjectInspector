using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityProjectInspector.Core.Models;

namespace UnityProjectInspector.Core.Parsing;

/// <summary>
/// Coordinates UnityEvent bindings with C# static analysis to build the full evidence chain:
///
///   UnityEvent → Target Component → Script → Class → Method → Method Invocations
///
/// Uses ScriptResolver for GUID → .cs resolution and CSharpAnalyzer for syntax analysis.
/// Does NOT use SemanticModel, does not execute code, does not load Unity.
/// </summary>
public class UnityEventCodeLinker
{
    private readonly ScriptResolver _scriptResolver;
    private readonly CSharpAnalyzer _csharpAnalyzer;

    /// <summary>
    /// Creates a linker that resolves scripts from the given Unity project root.
    /// </summary>
    public UnityEventCodeLinker(string projectRootPath)
    {
        _scriptResolver = new ScriptResolver(projectRootPath);
        _csharpAnalyzer = new CSharpAnalyzer();
    }

    /// <summary>
    /// Links UnityEvent bindings to their C# method implementations.
    /// Returns one UnityEventCodeLink per binding.
    /// </summary>
    /// <param name="bindings">Parsed UnityEvent bindings from UnityEventParser.</param>
    /// <param name="sceneInfo">Pre-parsed SceneInfo for FileId→GameObject lookups.</param>
    /// <returns>A list of fully resolved (or partially resolved) code links.</returns>
    public List<UnityEventCodeLink> Link(
        List<UnityEventBindingInfo> bindings,
        SceneInfo sceneInfo)
    {
        // Build reverse maps from SceneInfo
        var componentToGameObject = BuildComponentToGameObjectMap(sceneInfo);
        var fileIdToGameObjectName = BuildFileIdToNameMap(sceneInfo);

        var results = new List<UnityEventCodeLink>();

        foreach (var binding in bindings)
        {
            var link = BuildLink(binding, sceneInfo, componentToGameObject, fileIdToGameObjectName);
            results.Add(link);
        }

        return results;
    }

    private UnityEventCodeLink BuildLink(
        UnityEventBindingInfo binding,
        SceneInfo sceneInfo,
        Dictionary<long, long> componentToGameObject,
        Dictionary<long, string> fileIdToGameObjectName)
    {
        var link = new UnityEventCodeLink
        {
            SourceGameObjectFileId = binding.SourceGameObjectFileId,
            SourceComponentFileId = binding.SourceComponentFileId,
            SourceGameObjectName = fileIdToGameObjectName.GetValueOrDefault(binding.SourceGameObjectFileId),
            TargetFileId = binding.TargetFileId,
            TargetGameObjectFileId = binding.TargetGameObjectFileId,
            TargetGameObjectName = fileIdToGameObjectName.GetValueOrDefault(binding.TargetGameObjectFileId),
            MethodName = binding.MethodName,
        };

        // Step 1: Find the target component in SceneInfo to get its ScriptGuid
        var targetComponent = FindComponent(sceneInfo, binding.TargetFileId);

        if (targetComponent == null || targetComponent.ScriptGuid == null)
        {
            link.Status = LinkStatus.Unresolved;
            link.StatusMessage = "Target component not found or has no ScriptGuid in scene";
            return link;
        }

        // Step 2: Resolve GUID → .cs file via ScriptResolver
        var scriptInfo = _scriptResolver.ResolveScript(targetComponent.ScriptGuid);

        if (scriptInfo == null || scriptInfo.ScriptPath == null)
        {
            link.Status = LinkStatus.Unresolved;
            link.StatusMessage = $"Script not found for GUID: {targetComponent.ScriptGuid}";
            return link;
        }

        link.ScriptPath = scriptInfo.ScriptPath;

        // Step 3: Parse the .cs file with CSharpAnalyzer
        CSharpFileInfo? csFileInfo;
        try
        {
            csFileInfo = _csharpAnalyzer.AnalyzeFile(scriptInfo.ScriptPath);
        }
        catch
        {
            link.Status = LinkStatus.Unresolved;
            link.StatusMessage = $"Failed to parse C# source: {scriptInfo.ScriptPath}";
            return link;
        }

        if (csFileInfo.Classes.Count == 0)
        {
            link.Status = LinkStatus.Unresolved;
            link.StatusMessage = "No classes found in script file";
            return link;
        }

        // Step 4: Determine which class contains the target method.
        // Priority: class name from TargetAssemblyTypeName > first class found.
        var targetClassName = ExtractClassName(binding.TargetAssemblyTypeName);

        // Find the target class and method
        CSharpClassInfo? targetClass = null;
        CSharpMethodInfo? targetMethod = null;

        if (targetClassName != null)
        {
            // Try matching by extracted class name first
            targetClass = csFileInfo.Classes
                .FirstOrDefault(c =>
                    string.Equals(c.Name, targetClassName, StringComparison.Ordinal));
        }

        // Fallback: scan all classes for a method matching MethodName
        if (targetClass == null)
        {
            foreach (var cls in csFileInfo.Classes)
            {
                targetMethod = cls.Methods
                    .FirstOrDefault(m =>
                        string.Equals(m.Name, binding.MethodName, StringComparison.Ordinal));

                if (targetMethod != null)
                {
                    targetClass = cls;
                    break;
                }
            }
        }
        else
        {
            targetMethod = targetClass.Methods
                .FirstOrDefault(m =>
                    string.Equals(m.Name, binding.MethodName, StringComparison.Ordinal));
        }

        if (targetClass == null)
        {
            link.Status = LinkStatus.Unresolved;
            link.StatusMessage = $"No matching class found in {scriptInfo.ScriptPath}";
            return link;
        }

        link.ClassName = targetClass.Name;

        if (targetMethod == null)
        {
            link.Status = LinkStatus.Unresolved;
            link.StatusMessage =
                $"Method '{binding.MethodName}' not found in class '{targetClass.Name}'";
            return link;
        }

        // Step 5: Extract method calls from the target method
        var hasNonStaticArgs = false;

        foreach (var call in targetMethod.Calls)
        {
            var areArgsStatic = call.Arguments.Count == 0 ||
                                call.Arguments.All(a => IsLiteralToken(a));

            if (!areArgsStatic)
            {
                hasNonStaticArgs = true;
            }

            link.Calls.Add(new CSharpMethodReferenceInfo
            {
                SourceClass = targetClass.Name,
                SourceMethod = targetMethod.Name,
                FullTarget = call.Target,
                Arguments = call.Arguments,
                AreAllArgumentsStatic = areArgsStatic,
            });
        }

        // Step 6: Determine overall status
        if (link.Calls.Count == 0)
        {
            link.Status = LinkStatus.Resolved;
            link.StatusMessage = "Method exists but contains no method calls";
        }
        else if (hasNonStaticArgs)
        {
            link.Status = LinkStatus.PartiallyResolved;
            link.StatusMessage = "Some call arguments cannot be statically determined";
        }
        else
        {
            link.Status = LinkStatus.Resolved;
        }

        return link;
    }

    /// <summary>
    /// Finds a specific component in SceneInfo by fileId.
    /// </summary>
    private static ComponentInfo? FindComponent(SceneInfo sceneInfo, long fileId)
    {
        foreach (var go in sceneInfo.GameObjects)
        {
            foreach (var comp in go.Components)
            {
                if (comp.FileId == fileId)
                {
                    return comp;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Builds Component fileId → GameObject fileId map.
    /// </summary>
    private static Dictionary<long, long> BuildComponentToGameObjectMap(SceneInfo sceneInfo)
    {
        var map = new Dictionary<long, long>();
        foreach (var go in sceneInfo.GameObjects)
        {
            foreach (var comp in go.Components)
            {
                map[comp.FileId] = go.FileId;
            }
        }
        return map;
    }

    /// <summary>
    /// Builds GameObject fileId → GameObject name map.
    /// </summary>
    private static Dictionary<long, string> BuildFileIdToNameMap(SceneInfo sceneInfo)
    {
        var map = new Dictionary<long, string>();
        foreach (var go in sceneInfo.GameObjects)
        {
            map[go.FileId] = go.Name;
        }
        return map;
    }

    /// <summary>
    /// Extracts the class name from TargetAssemblyTypeName.
    /// "GameManager, Assembly-CSharp" → "GameManager"
    /// Returns null if parsing fails.
    /// </summary>
    private static string? ExtractClassName(string? assemblyTypeName)
    {
        if (string.IsNullOrWhiteSpace(assemblyTypeName))
        {
            return null;
        }

        // Format: "ClassName, Assembly-CSharp" or just "ClassName"
        var commaIndex = assemblyTypeName.IndexOf(',');
        return commaIndex > 0
            ? assemblyTypeName[..commaIndex].Trim()
            : assemblyTypeName.Trim();
    }

    /// <summary>
    /// Heuristically checks whether an argument token is a compile-time literal
    /// (string literal, numeric literal, boolean literal, null).
    /// Variables, member accesses, method calls are NOT literals.
    /// </summary>
    private static bool IsLiteralToken(string token)
    {
        if (token.Length == 0) return true;

        // String literal: "..."
        if (token.Length >= 2 && token.StartsWith('"') && token.EndsWith('"'))
            return true;

        // Character literal: '...'
        if (token.Length >= 2 && token.StartsWith('\'') && token.EndsWith('\''))
            return true;

        // Numeric literal: digits, decimal, hex, scientific
        if (long.TryParse(token, out _) ||
            double.TryParse(token, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return true;

        // Boolean and null
        if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token, "null", StringComparison.OrdinalIgnoreCase))
            return true;

        // Default literal
        if (token == "default")
            return true;

        return false;
    }
}