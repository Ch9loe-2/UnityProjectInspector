using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityProjectInspector.Core.Models;

namespace UnityProjectInspector.Core.Parsing;

/// <summary>
/// Analyzes C# source files using Roslyn SyntaxTree (no SemanticModel).
/// Extracts classes, methods, and method invocation expressions.
///
/// This is static analysis only — no code execution, no compilation, no type resolution.
/// </summary>
public class CSharpAnalyzer
{
    /// <summary>
    /// Analyzes a .cs file from disk.
    /// </summary>
    /// <param name="filePath">Path to the .cs file.</param>
    /// <returns>Parsed CSharpFileInfo with classes, methods, and calls.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    public CSharpFileInfo AnalyzeFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"C# source file not found: {filePath}", filePath);
        }

        var sourceText = File.ReadAllText(filePath);
        return AnalyzeSource(sourceText, filePath);
    }

    /// <summary>
    /// Analyzes C# source code text. Extracted for testability without file I/O.
    /// </summary>
    public CSharpFileInfo AnalyzeSource(string sourceText, string filePath)
    {
        var result = new CSharpFileInfo
        {
            FilePath = filePath,
        };

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return result;
        }

        // Parse with Roslyn SyntaxTree
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);

        // Get the root — even if there are diagnostics, we can still walk partial tree
        var root = syntaxTree.GetRoot();

        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return result;
        }

        // Extract namespace if present
        var namespaceDecl = compilationUnit.Members
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();

        var fileScopedNamespace = compilationUnit.Members
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        string? namespaceName = null;

        if (namespaceDecl != null)
        {
            namespaceName = namespaceDecl.Name.ToString();
        }
        else if (fileScopedNamespace != null)
        {
            namespaceName = fileScopedNamespace.Name.ToString();
        }

        // Extract classes
        var classDeclarations = compilationUnit.DescendantNodes()
            .OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classDeclarations)
        {
            var classInfo = new CSharpClassInfo
            {
                Name = classDecl.Identifier.Text,
                Namespace = namespaceName,
            };

            // Extract methods
            var methodDeclarations = classDecl.Members
                .OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDeclarations)
            {
                var methodInfo = new CSharpMethodInfo
                {
                    Name = methodDecl.Identifier.Text,
                };

                // Extract invocation expressions within this method's body
                if (methodDecl.Body != null)
                {
                    var invocations = methodDecl.Body.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>();

                    foreach (var invocation in invocations)
                    {
                        var target = GetCallTarget(invocation.Expression);
                        var args = invocation.ArgumentList.Arguments
                            .Select(a => a.ToString())
                            .ToList();

                        methodInfo.Calls.Add(new CSharpMethodCallInfo
                        {
                            Target = target,
                            Arguments = args,
                        });
                    }
                }

                classInfo.Methods.Add(methodInfo);
            }

            result.Classes.Add(classInfo);
        }

        return result;
    }

    /// <summary>
    /// Walks the invocation expression to reconstruct the full call target string.
    /// Examples:
    ///   SceneManager.LoadScene → "SceneManager.LoadScene"
    ///   Debug.Log              → "Debug.Log"
    ///   Application.Quit       → "Application.Quit"
    ///   SomeMethod()           → "SomeMethod"
    /// </summary>
    private static string GetCallTarget(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => $"{GetCallTarget(memberAccess.Expression)}.{memberAccess.Name.Identifier.Text}",
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            _ => expression.ToString(),
        };
    }
}