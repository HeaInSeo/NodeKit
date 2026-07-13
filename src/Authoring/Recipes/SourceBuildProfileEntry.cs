namespace NodeKit.Authoring.Recipes
{
    /// <summary>One curated Build or Runtime profile entry for RecipeBuildKind.SourceBuildStructured.</summary>
    internal sealed record SourceBuildProfileEntry(string Key, string ImageReference, LocalizedText Label, LocalizedText Description);
}
