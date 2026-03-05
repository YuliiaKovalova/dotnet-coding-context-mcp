using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using DotnetCodingContextMCP.Models;

namespace DotnetCodingContextMCP.Services;

public sealed class RoslynWorkspaceService : IDisposable
{
    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<Solution> LoadSolutionAsync(string path, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _workspace?.Dispose();
            _workspace = MSBuildWorkspace.Create();
            _workspace.WorkspaceFailed += (_, e) => { /* suppress diagnostics */ };

            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                _solution = await _workspace.OpenSolutionAsync(path, cancellationToken: ct);
            }
            else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await _workspace.OpenProjectAsync(path, cancellationToken: ct);
                _solution = project.Solution;
            }
            else
            {
                // Try to find .sln in directory
                var slnFile = Directory.GetFiles(path, "*.sln").FirstOrDefault()
                           ?? Directory.GetFiles(path, "*.slnx").FirstOrDefault();
                if (slnFile != null)
                {
                    _solution = await _workspace.OpenSolutionAsync(slnFile, cancellationToken: ct);
                }
                else
                {
                    throw new InvalidOperationException($"No solution or project file found at: {path}");
                }
            }

            return _solution;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Solution GetSolution() =>
        _solution ?? throw new InvalidOperationException("No solution loaded. Call load_solution or provide a path.");

    public async Task<IReadOnlyList<INamedTypeSymbol>> FindTypesByNameAsync(
        string typeName, Solution? solution = null, CancellationToken ct = default)
    {
        var sol = solution ?? GetSolution();
        var results = new List<INamedTypeSymbol>();

        foreach (var project in sol.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            // Try exact match first
            var symbols = compilation.GetSymbolsWithName(typeName, SymbolFilter.Type, ct);
            foreach (var symbol in symbols)
            {
                if (symbol is INamedTypeSymbol nts &&
                    nts.DeclaredAccessibility == Accessibility.Public &&
                    nts.Locations.Any(l => l.IsInSource))
                {
                    results.Add(nts);
                }
            }
        }

        // Deduplicate by full name
        return results
            .GroupBy(t => t.ToDisplayString())
            .Select(g => g.First())
            .ToList();
    }

    public async Task<IReadOnlyList<INamedTypeSymbol>> FindTypesByPatternAsync(
        string pattern, Solution? solution = null, CancellationToken ct = default)
    {
        var sol = solution ?? GetSolution();
        var results = new List<INamedTypeSymbol>();

        foreach (var project in sol.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            var symbols = compilation.GetSymbolsWithName(
                name => name.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                SymbolFilter.Type, ct);

            foreach (var symbol in symbols)
            {
                if (symbol is INamedTypeSymbol nts &&
                    (nts.DeclaredAccessibility == Accessibility.Public ||
                     nts.DeclaredAccessibility == Accessibility.Internal) &&
                    nts.Locations.Any(l => l.IsInSource))
                {
                    results.Add(nts);
                }
            }
        }

        return results
            .GroupBy(t => t.ToDisplayString())
            .Select(g => g.First())
            .ToList();
    }

    public TypeCodingContext BuildTypeCodingContext(INamedTypeSymbol type)
    {
        var constructors = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .Where(c => !c.IsImplicitlyDeclared || type.IsRecord)
            .Select(c => FormatConstructor(c, type))
            .ToList();

        var properties = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic)
            .Select(p => new Models.PropertyInfo
            {
                Name = p.Name,
                Type = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Settable = p.SetMethod is not null &&
                           p.SetMethod.DeclaredAccessibility == Accessibility.Public &&
                           !p.SetMethod.IsInitOnly,
                IsRequired = p.IsRequired,
            })
            .ToList();

        var methods = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public &&
                        m.MethodKind == MethodKind.Ordinary && !m.IsStatic)
            .Select(m => new Models.MethodInfo
            {
                Name = m.Name,
                ReturnType = m.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Signature = FormatMethodSignature(m),
            })
            .ToList();

        var interfaces = type.AllInterfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            .ToList();

        return new TypeCodingContext
        {
            Name = type.Name,
            FullName = type.ToDisplayString(),
            Using = $"using {type.ContainingNamespace.ToDisplayString()};",
            Kind = GetTypeKind(type),
            Constructors = constructors,
            Properties = properties,
            PublicMethods = methods,
            Instantiation = GenerateInstantiation(type),
            Interfaces = interfaces,
            BaseType = type.BaseType is { SpecialType: not SpecialType.System_Object }
                ? type.BaseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) : null,
            IsAbstract = type.IsAbstract,
            IsSealed = type.IsSealed,
        };
    }

    public async Task<IReadOnlyList<INamedTypeSymbol>> FindImplementationsAsync(
        INamedTypeSymbol interfaceType, Solution? solution = null, CancellationToken ct = default)
    {
        var sol = solution ?? GetSolution();
        var implementations = new List<INamedTypeSymbol>();

        foreach (var project in sol.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            var allTypes = GetAllTypes(compilation.GlobalNamespace);
            foreach (var type in allTypes)
            {
                if (!type.IsAbstract && type.AllInterfaces.Any(i =>
                    SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, interfaceType.OriginalDefinition)))
                {
                    implementations.Add(type);
                }
            }
        }

        return implementations
            .GroupBy(t => t.ToDisplayString())
            .Select(g => g.First())
            .ToList();
    }

    public IReadOnlyList<InterfaceMember> ExtractInterfaceMembers(INamedTypeSymbol interfaceType)
    {
        var members = new List<InterfaceMember>();

        foreach (var member in interfaceType.GetMembers())
        {
            if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
            {
                members.Add(new InterfaceMember
                {
                    Name = method.Name,
                    ReturnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    Signature = FormatMethodSignature(method),
                    MockSetup = GenerateMockSetup(method),
                });
            }
            else if (member is IPropertySymbol prop)
            {
                members.Add(new InterfaceMember
                {
                    Name = prop.Name,
                    ReturnType = prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    Signature = $"{prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {prop.Name} {{ {(prop.GetMethod != null ? "get; " : "")}{(prop.SetMethod != null ? "set; " : "")}}}",
                    MockSetup = $"mock.Setup(x => x.{prop.Name}).Returns(default({prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}))",
                });
            }
        }

        return members;
    }

    public HashSet<INamedTypeSymbol> CollectRelatedTypes(INamedTypeSymbol type)
    {
        var related = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        CollectRelatedTypesRecursive(type, related, depth: 0, maxDepth: 1);
        return related;
    }

    private void CollectRelatedTypesRecursive(
        INamedTypeSymbol type, HashSet<INamedTypeSymbol> collected, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        // From constructors
        foreach (var ctor in type.InstanceConstructors)
        {
            foreach (var param in ctor.Parameters)
            {
                TryAddNamedType(param.Type, collected);
            }
        }

        // From public methods
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>()
                     .Where(m => m.MethodKind == MethodKind.Ordinary && m.DeclaredAccessibility == Accessibility.Public))
        {
            TryAddNamedType(method.ReturnType, collected);
            foreach (var param in method.Parameters)
            {
                TryAddNamedType(param.Type, collected);
            }
        }

        // From public properties
        foreach (var prop in type.GetMembers().OfType<IPropertySymbol>()
                     .Where(p => p.DeclaredAccessibility == Accessibility.Public))
        {
            TryAddNamedType(prop.Type, collected);
        }
    }

    private static void TryAddNamedType(ITypeSymbol typeSymbol, HashSet<INamedTypeSymbol> collected)
    {
        // Unwrap Task<T>, List<T>, etc.
        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } generic)
        {
            foreach (var arg in generic.TypeArguments)
            {
                TryAddNamedType(arg, collected);
            }
        }

        if (typeSymbol is INamedTypeSymbol nts &&
            nts.SpecialType == SpecialType.None &&
            !nts.ToDisplayString().StartsWith("System.", StringComparison.Ordinal) &&
            !nts.ToDisplayString().StartsWith("Microsoft.", StringComparison.Ordinal) &&
            nts.ContainingNamespace is not null &&
            !nts.IsAnonymousType)
        {
            collected.Add(nts);
        }
    }

    private static string FormatConstructor(IMethodSymbol ctor, INamedTypeSymbol type)
    {
        var parameters = string.Join(", ", ctor.Parameters.Select(p =>
            $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"));
        return $"new {type.Name}({parameters})";
    }

    private static string FormatMethodSignature(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p =>
            $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"));
        var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return $"{returnType} {method.Name}({parameters})";
    }

    public string GenerateInstantiationHint(INamedTypeSymbol type)
        => GenerateInstantiation(type);

    public IReadOnlyList<string> GenerateTestHints(INamedTypeSymbol type)
    {
        var hints = new List<string>();

        // Guard clause / validation hints
        foreach (var ctor in type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal))
        {
            foreach (var param in ctor.Parameters)
            {
                if (param.Type.IsReferenceType && !param.Type.NullableAnnotation.Equals(NullableAnnotation.Annotated))
                {
                    hints.Add($"Test constructor throws when {param.Name} is null");
                }
            }
        }

        // Method-level hints
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && m.DeclaredAccessibility == Accessibility.Public))
        {
            // Null argument tests
            var refParams = method.Parameters
                .Where(p => p.Type.IsReferenceType && !p.HasExplicitDefaultValue)
                .ToList();
            if (refParams.Count > 0)
            {
                hints.Add($"Test {method.Name} throws ArgumentNullException for null {string.Join(", ", refParams.Select(p => p.Name))}");
            }

            // Return type hints
            var returnDisplay = method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (returnDisplay.StartsWith("Task<", StringComparison.Ordinal))
            {
                hints.Add($"Test {method.Name} async success and failure paths");
            }

            if (method.ReturnType is INamedTypeSymbol { IsGenericType: true } generic &&
                (generic.Name == "IEnumerable" || generic.Name == "IReadOnlyList" || generic.Name == "List"))
            {
                hints.Add($"Test {method.Name} returns empty collection (not null) when no results");
            }

            // Boolean return
            if (returnDisplay is "bool" or "Boolean")
            {
                hints.Add($"Test {method.Name} returns true AND false cases");
            }
        }

        // Property edge cases
        foreach (var prop in type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic))
        {
            if (prop.SetMethod is not null && prop.SetMethod.DeclaredAccessibility == Accessibility.Public)
            {
                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    hints.Add($"Test {prop.Name} with null, empty, and whitespace strings");
                }
                else if (prop.Type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Double or SpecialType.System_Decimal)
                {
                    hints.Add($"Test {prop.Name} with zero, negative, and boundary values");
                }
            }
        }

        // Domain-specific patterns
        if (type.GetMembers().Any(m => m.Name.Contains("Status", StringComparison.Ordinal) ||
                                       m.Name.Contains("State", StringComparison.Ordinal)))
        {
            hints.Add("Test all state transitions and verify invalid transitions throw");
        }

        if (type.BaseType?.Name == "Entity" || type.AllInterfaces.Any(i => i.Name == "IAggregateRoot"))
        {
            hints.Add("Test domain events are raised on state changes");
            hints.Add("Test equality based on Id, not reference");
        }

        if (type.AllInterfaces.Any(i => i.Name == "IEquatable" || i.Name == "IComparable"))
        {
            hints.Add("Test Equals, GetHashCode, and operator== symmetry");
        }

        return hints.Take(10).ToList();
    }

    private static string GenerateInstantiation(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface || type.IsAbstract)
        {
            return $"// Cannot instantiate directly — {(type.TypeKind == TypeKind.Interface ? "interface" : "abstract class")}";
        }

        // For records with primary constructor
        if (type.IsRecord)
        {
            var primaryCtor = type.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                .OrderByDescending(c => c.Parameters.Length)
                .FirstOrDefault();

            if (primaryCtor is not null && primaryCtor.Parameters.Length > 0)
            {
                var args = string.Join(", ", primaryCtor.Parameters.Select(p => GetDefaultValue(p.Type, p.Name)));
                return $"new {type.Name}({args})";
            }
        }

        // Prefer constructor with most parameters
        var bestCtor = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (bestCtor is not null && bestCtor.Parameters.Length > 0)
        {
            var args = string.Join(", ", bestCtor.Parameters.Select(p => GetDefaultValue(p.Type, p.Name)));
            return $"new {type.Name}({args})";
        }

        // Check for settable properties (object initializer)
        var settableProps = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic &&
                        p.SetMethod is not null &&
                        p.SetMethod.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        if (settableProps.Count > 0)
        {
            var inits = string.Join(", ", settableProps.Take(3).Select(p =>
                $"{p.Name} = {GetDefaultValue(p.Type, p.Name)}"));
            return $"new {type.Name} {{ {inits} }}";
        }

        return $"new {type.Name}()";
    }

    private static string GetDefaultValue(ITypeSymbol type, string paramName)
    {
        var displayName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        return displayName switch
        {
            "string" => $"\"{paramName}\"",
            "int" or "Int32" => "0",
            "long" or "Int64" => "0L",
            "double" or "Double" => "0.0",
            "float" or "Single" => "0f",
            "decimal" or "Decimal" => "0m",
            "bool" or "Boolean" => "false",
            "TimeSpan" => "TimeSpan.Zero",
            "DateTime" => "DateTime.UtcNow",
            "Guid" => "Guid.NewGuid()",
            "CancellationToken" => "CancellationToken.None",
            _ when type.TypeKind == TypeKind.Interface => $"Mock.Of<{displayName}>()",
            _ when type.TypeKind == TypeKind.Delegate => $"() => Task.CompletedTask",
            _ when displayName.StartsWith("Func<", StringComparison.Ordinal) => $"() => Task.FromResult(default({GetFuncReturnType(displayName)})!)",
            _ when displayName.StartsWith("Action", StringComparison.Ordinal) => "() => { }",
            _ when displayName.StartsWith("List<", StringComparison.Ordinal) => $"new {displayName}()",
            _ when displayName.StartsWith("IList<", StringComparison.Ordinal) => $"new {displayName.Replace("IList<", "List<")}()",
            _ when displayName.StartsWith("IReadOnlyList<", StringComparison.Ordinal) => $"Array.Empty<{ExtractGenericArg(displayName)}>()",
            _ when displayName.StartsWith("IEnumerable<", StringComparison.Ordinal) => $"Array.Empty<{ExtractGenericArg(displayName)}>()",
            _ when displayName.StartsWith("Dictionary<", StringComparison.Ordinal) => $"new {displayName}()",
            _ when displayName.StartsWith("IDictionary<", StringComparison.Ordinal) => $"new {displayName.Replace("IDictionary<", "Dictionary<")}()",
            _ when type.IsReferenceType => $"default({displayName})!",
            _ => $"default({displayName})",
        };
    }

    private static string GetFuncReturnType(string funcType)
    {
        // Extract last type arg from Func<..., TResult>
        var lastComma = funcType.LastIndexOf(',');
        if (lastComma >= 0)
        {
            return funcType[(lastComma + 1)..^1].Trim();
        }
        // Func<TResult>
        return funcType[5..^1].Trim();
    }

    private static string ExtractGenericArg(string genericType)
    {
        var start = genericType.IndexOf('<') + 1;
        var end = genericType.LastIndexOf('>');
        return start > 0 && end > start ? genericType[start..end] : "object";
    }

    private static string GenerateMockSetup(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p =>
            $"It.IsAny<{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}>()"));
        var call = $"x => x.{method.Name}({parameters})";

        var returnType = method.ReturnType;
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            return $"mock.Setup({call})";
        }

        var returnDisplay = returnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        // Handle Task/Task<T>
        if (returnDisplay == "Task")
        {
            return $"mock.Setup({call}).Returns(Task.CompletedTask)";
        }
        if (returnDisplay.StartsWith("Task<", StringComparison.Ordinal))
        {
            var innerType = returnDisplay[5..^1];
            return $"mock.Setup({call}).ReturnsAsync(default({innerType})!)";
        }

        return $"mock.Setup({call}).Returns(default({returnDisplay})!)";
    }

    private static string GetTypeKind(INamedTypeSymbol type)
    {
        if (type.IsRecord) return type.IsValueType ? "record struct" : "record";
        if (type.TypeKind == TypeKind.Interface) return "interface";
        if (type.TypeKind == TypeKind.Enum) return "enum";
        if (type.TypeKind == TypeKind.Struct) return "struct";
        if (type.IsAbstract) return "abstract class";
        if (type.IsSealed) return "sealed class";
        return "class";
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
        }
        foreach (var childNs in ns.GetNamespaceMembers())
        {
            foreach (var type in GetAllTypes(childNs))
            {
                yield return type;
            }
        }
    }

    public void Dispose()
    {
        _workspace?.Dispose();
    }
}
