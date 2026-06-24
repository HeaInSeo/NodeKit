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

        public RecipeBuildKind BuildKind { get; set; }

        public string ToolName { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Pinned base image used by Conda/Micromamba/PackageMirror/SourceBuild/
        /// DockerfileFallback. Not used by BioContainer (see BioContainerImageUri).
        /// </summary>
        public string BaseImage { get; set; } = string.Empty;

        // ── Conda / Micromamba / PackageMirror ──────────────────────────────
        public List<string> Channels { get; set; } = new();

        public List<string> Packages { get; set; } = new();

        /// <summary>PackageMirror only: internal mirror URL used in place of public channels.</summary>
        public string PackageMirrorUri { get; set; } = string.Empty;

        // ── BioContainer ─────────────────────────────────────────────────────
        /// <summary>Pinned external image URI. This build kind's only image input.</summary>
        public string BioContainerImageUri { get; set; } = string.Empty;

        // ── SourceBuild ──────────────────────────────────────────────────────
        public string SourceUri { get; set; } = string.Empty;

        /// <summary>Expected format: "sha256:&lt;64-hex&gt;".</summary>
        public string SourceChecksum { get; set; } = string.Empty;

        public List<string> SourceBuildCommands { get; set; } = new();

        // ── DockerfileFallback ───────────────────────────────────────────────
        public string DockerfileContent { get; set; } = string.Empty;

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
    }
}
