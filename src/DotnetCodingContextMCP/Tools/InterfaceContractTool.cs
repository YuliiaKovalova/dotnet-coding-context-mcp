using System.ComponentModel;
using System.Text.Json;
using DotnetCodingContextMCP.Models;
using DotnetCodingContextMCP.Services;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotnetCodingContextMCP.Tools;

[McpServerToolType]
public sealed class InterfaceContractTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_interface_contract")]
    [Description("BEFORE mocking a C# interface in tests, call this to get all its members with ready-to-paste Moq setup patterns. Returns method signatures and 'mock.Setup(x => x.Method(...)).Returns(...)' code. Also lists concrete implementations and parameter types with instantiation examples.")]
    public static async Task<string> GetInterfaceContractAsync(
        RoslynWorkspaceService workspace,
        [Description("Interface name, e.g. 'IRateLimitStorage' or 'ILoadBalancer'")] string interfaceName,
        [Description("Path to .sln, .slnx, .csproj or directory. Required on first call.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (path is not null)
            {
                await workspace.LoadSolutionAsync(path, cancellationToken);
            }

            var types = await workspace.FindTypesByNameAsync(interfaceName, ct: cancellationToken);
            var interfaceType = types.FirstOrDefault(t => t.TypeKind == TypeKind.Interface);

            if (interfaceType is null)
            {
                // Try fuzzy
                types = await workspace.FindTypesByPatternAsync(interfaceName, ct: cancellationToken);
                interfaceType = types.FirstOrDefault(t => t.TypeKind == TypeKind.Interface);
            }

            if (interfaceType is null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "interface_not_found",
                    message = $"Could not find interface: {interfaceName}",
                    hint = "Ensure the name is correct. Use the full type name if ambiguous."
                }, JsonOptions);
            }

            var members = workspace.ExtractInterfaceMembers(interfaceType);
            var implementations = await workspace.FindImplementationsAsync(interfaceType, ct: cancellationToken);

            // Collect parameter types used in interface members
            var parameterTypes = new List<TypeCodingContext>();
            var seen = new HashSet<string>();
            foreach (var member in interfaceType.GetMembers().OfType<IMethodSymbol>())
            {
                foreach (var param in member.Parameters)
                {
                    if (param.Type is INamedTypeSymbol nts &&
                        nts.SpecialType == SpecialType.None &&
                        !nts.ToDisplayString().StartsWith("System.", StringComparison.Ordinal) &&
                        seen.Add(nts.ToDisplayString()))
                    {
                        parameterTypes.Add(workspace.BuildTypeCodingContext(nts));
                    }
                }

                // Also return type if it's a domain type
                if (member.ReturnType is INamedTypeSymbol returnNts &&
                    returnNts.SpecialType == SpecialType.None &&
                    !returnNts.ToDisplayString().StartsWith("System.", StringComparison.Ordinal))
                {
                    // Unwrap Task<T>
                    var actualType = returnNts;
                    if (returnNts.IsGenericType && returnNts.Name == "Task" && returnNts.TypeArguments.Length == 1)
                    {
                        if (returnNts.TypeArguments[0] is INamedTypeSymbol inner)
                        {
                            actualType = inner;
                        }
                    }

                    if (actualType.SpecialType == SpecialType.None &&
                        !actualType.ToDisplayString().StartsWith("System.", StringComparison.Ordinal) &&
                        seen.Add(actualType.ToDisplayString()))
                    {
                        parameterTypes.Add(workspace.BuildTypeCodingContext(actualType));
                    }
                }
            }

            var response = new InterfaceContractResponse
            {
                InterfaceName = interfaceType.Name,
                FullName = interfaceType.ToDisplayString(),
                Using = $"using {interfaceType.ContainingNamespace.ToDisplayString()};",
                Members = members,
                Implementations = implementations.Select(i => i.ToDisplayString()).ToList(),
                ParameterTypes = parameterTypes,
            };

            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = "analysis_failed", message = ex.Message }, JsonOptions);
        }
    }
}
