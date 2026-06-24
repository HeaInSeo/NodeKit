using System.Collections.Generic;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    /// <summary>Parsed result of `nodekit recipe create` CLI options.</summary>
    internal sealed record RecipeCreateOptions(
        RecipeMethodId? Method,
        string? Engine,
        bool AcceptDockerfileWarning,
        bool NonInteractive,
        IReadOnlyList<(string Name, string Value)> Fields,
        IReadOnlyList<(string Name, string Spec)> Inputs,
        IReadOnlyList<(string Name, string Spec)> Outputs,
        string? Error);
}
