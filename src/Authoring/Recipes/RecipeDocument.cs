using System;
using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// NodeKit recipe authoring draft. RecipeRenderer turns this into a
    /// ToolDefinition; it is never sent to NodeVault directly.
    /// </summary>
    internal class RecipeDocument
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string SchemaVersion { get; set; } = "draft-1";

        public RecipeBuildKind? BuildKind { get; set; }

        public string ToolName { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Pinned base image used by Conda/Micromamba/PackageMirror/SourceBuild/
        /// DockerfileFallback. Not used by BioContainer (see BioContainerImageUri).
        /// </summary>
        public string BaseImage { get; set; } = string.Empty;

        /// <summary>
        /// container method authoring field — see
        /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 9.3.
        /// Tag-only BaseImage is allowed mid-authoring; ImageDigest is the
        /// separate field that final L1 validation (CLAUDE.md Section 3)
        /// requires to be present. RecipeAuthoringSession.Build() combines
        /// this with BaseImage into BioContainerImageUri, which is what
        /// RecipeRenderer/RecipeValidator actually read for the BioContainer
        /// build kind.
        /// </summary>
        public string ImageDigest { get; set; } = string.Empty;

        // ── Conda / Micromamba / PackageMirror ──────────────────────────────
        public List<string> Channels { get; set; } = new();

        public List<string> Packages { get; set; } = new();

        /// <summary>PackageMirror only: internal mirror URL used in place of public channels.</summary>
        public string PackageMirrorUri { get; set; } = string.Empty;

        /// <summary>
        /// package method authoring field, Defaulted to "conda" by Build() —
        /// see design doc Section 9.4. Not yet consumed by RecipeRenderer.
        /// </summary>
        public string PackageEngine { get; set; } = string.Empty;

        /// <summary>mirror method authoring field, Optional in v1 — see design doc Section 9.5.</summary>
        public string MirrorKind { get; set; } = string.Empty;

        // ── BioContainer ─────────────────────────────────────────────────────

        /// <summary>Pinned external image URI. This build kind's only image input.</summary>
        public string BioContainerImageUri { get; set; } = string.Empty;

        // ── SourceBuild ──────────────────────────────────────────────────────
        public string SourceUri { get; set; } = string.Empty;

        /// <summary>Expected format: "sha256:&lt;64-hex&gt;".</summary>
        public string SourceChecksum { get; set; } = string.Empty;

        public List<string> SourceBuildCommands { get; set; } = new();

        /// <summary>source method authoring field, Recommended (not blocking) — see design doc Section 9.6.</summary>
        public List<string> BuildDependencies { get; set; } = new();

        // ── SourceBuild (structured, RecipeBuildKind.SourceBuildStructured) ──
        // SourceUri/SourceChecksum/SourceBuildCommands/BuildDependencies above
        // are reused as-is — see
        // docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md §5. These
        // fields only apply to the structured kind.

        /// <summary>
        /// Key into SourceBuildProfileCatalog.BuildProfiles, or
        /// SourceBuildProfileCatalog.AdvancedKey ("advanced") to use
        /// BuildProfileImage directly instead of a curated profile.
        /// </summary>
        public string BuildProfile { get; set; } = string.Empty;

        /// <summary>Only read when BuildProfile == SourceBuildProfileCatalog.AdvancedKey.</summary>
        public string BuildProfileImage { get; set; } = string.Empty;

        /// <summary>Key into SourceBuildProfileCatalog.RuntimeProfiles, or "advanced".</summary>
        public string RuntimeProfile { get; set; } = string.Empty;

        /// <summary>Only read when RuntimeProfile == SourceBuildProfileCatalog.AdvancedKey.</summary>
        public string RuntimeProfileImage { get; set; } = string.Empty;

        /// <summary>
        /// Distinct from BuildDependencies (fetch/build-stage-only) — these
        /// are expected to remain in the final runtime image. Recommended,
        /// not actionable yet (no auto-install — see design doc §10 D-8,
        /// same conservative policy as R21/BuildDependenciesAdvisor).
        /// </summary>
        public List<string> RuntimeDependencies { get; set; } = new();

        // ── DockerfileFallback ───────────────────────────────────────────────
        public string DockerfileContent { get; set; } = string.Empty;

        /// <summary>dockerfile method authoring field — alternative to DockerfileContent, see design doc Section 9.7.</summary>
        public string DockerfilePath { get; set; } = string.Empty;

        /// <summary>dockerfile method authoring field, Defaulted to the current directory by Build() — see design doc Section 9.7.</summary>
        public string BuildContext { get; set; } = string.Empty;

        // ── Common ToolDefinition-shaped fields ─────────────────────────────
        public string Script { get; set; } = string.Empty;

        public List<string> Command { get; set; } = new();

        public List<ToolInput> Inputs { get; set; } = new();

        public List<ToolOutput> Outputs { get; set; } = new();

        public string DisplayLabel { get; set; } = string.Empty;

        public string DisplayDescription { get; set; } = string.Empty;

        public string DisplayCategory { get; set; } = string.Empty;

        public List<string> DisplayTags { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Coalesces every nullable-capable property back to its safe default.
        /// System.Text.Json does not enforce C#'s non-nullable annotations at
        /// runtime, so an external recipe.json with e.g. "Command": null or
        /// "SourceChecksum": null deserializes with that property actually
        /// null despite its declared default — which then crashes
        /// RecipeRenderer.Render (called unconditionally by
        /// RecipeValidationPipeline.ValidateRecipe, even when RecipeValidator
        /// already flagged other violations) instead of surfacing as a clean
        /// L1 violation. Call this right after deserializing any
        /// externally-authored RecipeDocument, before validation/rendering.
        /// Interactive authoring (RecipeAuthoringSession) never produces
        /// nulls, so this is a no-op there.
        /// </summary>
        public void Normalize()
        {
            ToolName ??= string.Empty;
            Version ??= string.Empty;
            BaseImage ??= string.Empty;
            ImageDigest ??= string.Empty;
            Channels ??= new List<string>();
            Packages ??= new List<string>();
            PackageMirrorUri ??= string.Empty;
            PackageEngine ??= string.Empty;
            MirrorKind ??= string.Empty;
            BioContainerImageUri ??= string.Empty;
            SourceUri ??= string.Empty;
            SourceChecksum ??= string.Empty;
            SourceBuildCommands ??= new List<string>();
            BuildDependencies ??= new List<string>();
            BuildProfile ??= string.Empty;
            BuildProfileImage ??= string.Empty;
            RuntimeProfile ??= string.Empty;
            RuntimeProfileImage ??= string.Empty;
            RuntimeDependencies ??= new List<string>();
            DockerfileContent ??= string.Empty;
            DockerfilePath ??= string.Empty;
            BuildContext ??= string.Empty;
            Script ??= string.Empty;
            Command ??= new List<string>();
            Inputs ??= new List<ToolInput>();
            Outputs ??= new List<ToolOutput>();
            DisplayLabel ??= string.Empty;
            DisplayDescription ??= string.Empty;
            DisplayCategory ??= string.Empty;
            DisplayTags ??= new List<string>();
        }
    }
}
