using System.ComponentModel;
using System.Text.Json;
using DotnetCodingContextMCP.Models;
using DotnetCodingContextMCP.Services;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotnetCodingContextMCP.Tools;

[McpServerToolType]
public sealed class CodingContextTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_coding_context")]
    [Description("BEFORE writing C# code that uses specific types, call this to get their exact constructors, property names (with settability), instantiation examples, correct using statements, and related types. Prevents build errors from hallucinated type names, wrong constructor signatures, and incorrect property access patterns. Pass comma-separated type names, OR a namespace/project name to get all public types in that scope.")]
    public static async Task<string> GetCodingContextAsync(
        RoslynWorkspaceService workspace,
        [Description("Comma-separated type names (e.g. 'Order, OrderItem, Address') OR a namespace/project pattern (e.g. 'Ordering.Domain') to get all public types in that namespace")] string typeNames,
        [Description("Path to .sln, .slnx, .csproj or directory. Required on first call.")] string? path = null,
        [Description("Include types referenced in constructors/methods of requested types")] bool includeRelated = true,
        [Description("Maximum types to return when using namespace query (default 25)")] int maxTypes = 25,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (path is not null)
            {
                await workspace.LoadSolutionAsync(path, cancellationToken);
            }

            var names = typeNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var resolvedTypes = new List<TypeCodingContext>();
            var relatedTypes = new List<TypeCodingContext>();
            var processedFullNames = new HashSet<string>();

            foreach (var name in names)
            {
                var types = await workspace.FindTypesByNameAsync(name, ct: cancellationToken);
                if (types.Count == 0)
                {
                    // Try pattern match (also matches namespace patterns)
                    types = await workspace.FindTypesByPatternAsync(name, ct: cancellationToken);
                }

                // If pattern matched many types, it's likely a namespace query — limit results
                var typesToProcess = types.Count > 5 && names.Length == 1
                    ? types.Take(maxTypes).ToList()
                    : types as IReadOnlyList<INamedTypeSymbol>;

                foreach (var type in typesToProcess)
                {
                    var fullName = type.ToDisplayString();
                    if (!processedFullNames.Add(fullName)) continue;

                    var context = workspace.BuildTypeCodingContext(type);
                    resolvedTypes.Add(context);

                    if (includeRelated)
                    {
                        var related = workspace.CollectRelatedTypes(type);
                        foreach (var relatedType in related)
                        {
                            var relFullName = relatedType.ToDisplayString();
                            if (processedFullNames.Add(relFullName))
                            {
                                relatedTypes.Add(workspace.BuildTypeCodingContext(relatedType));
                            }
                        }
                    }
                }
            }

            if (resolvedTypes.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "no_types_found",
                    message = $"Could not find types: {typeNames}",
                    hint = "Check spelling or provide the full type name. Use comma-separated names for multiple types."
                }, JsonOptions);
            }

            var response = new CodingContextResponse
            {
                Types = resolvedTypes,
                RelatedTypes = relatedTypes,
            };

            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = "analysis_failed", message = ex.Message }, JsonOptions);
        }
    }
}
