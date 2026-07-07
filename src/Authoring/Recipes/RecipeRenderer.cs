using System;
using System.Collections.Generic;
using System.Text;
using NodeKit.Authoring;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Renders a RecipeDocument into a ToolDefinition for the current legacy
    /// BuildRequest path. Every build kind produces a non-empty DockerfileContent
    /// because BuildRequest.dockerfile_content is a required field regardless
    /// of how the image's contents were chosen (RequiredFieldsValidator L1-REQ-002).
    /// </summary>
    internal static class RecipeRenderer
    {
        public static ToolDefinition Render(RecipeDocument recipe)
        {
            ArgumentNullException.ThrowIfNull(recipe);

            var definition = new ToolDefinition
            {
                Name = recipe.ToolName,
                Version = recipe.Version,
                Script = recipe.Script,
                Command = new List<string>(recipe.Command),
                Inputs = new List<ToolInput>(recipe.Inputs),
                Outputs = new List<ToolOutput>(recipe.Outputs),
                DisplayLabel = recipe.DisplayLabel,
                DisplayDescription = recipe.DisplayDescription,
                DisplayCategory = recipe.DisplayCategory,
                DisplayTags = new List<string>(recipe.DisplayTags),
            };

            switch (recipe.BuildKind!.Value)
            {
                case RecipeBuildKind.Conda:
                    RenderInstallerFamily(recipe, definition, "conda", recipe.Channels);
                    break;
                case RecipeBuildKind.Micromamba:
                    RenderInstallerFamily(recipe, definition, "micromamba", recipe.Channels);
                    break;
                case RecipeBuildKind.PackageMirror:
                    RenderInstallerFamily(recipe, definition, "conda", new List<string> { recipe.PackageMirrorUri });
                    break;
                case RecipeBuildKind.BioContainer:
                    RenderBioContainer(recipe, definition);
                    break;
                case RecipeBuildKind.SourceBuild:
                    RenderSourceBuild(recipe, definition);
                    break;
                case RecipeBuildKind.DockerfileFallback:
                    definition.ImageUri = recipe.BaseImage;
                    definition.DockerfileContent = recipe.DockerfileContent;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(recipe), recipe.BuildKind.Value, "Unknown recipe build kind.");
            }

            return definition;
        }

        private static void RenderInstallerFamily(
            RecipeDocument recipe,
            ToolDefinition definition,
            string installer,
            IReadOnlyList<string> channels)
        {
            definition.ImageUri = recipe.BaseImage;

            var dockerfile = new StringBuilder();
            dockerfile.Append("FROM ").Append(recipe.BaseImage).Append('\n');

            // Channel/mirror config runs on its own RUN line, never the same
            // line as "<installer> install" — PackageVersionValidator scans
            // every token after "install" as a package pin and would
            // misread a channel name (no '=' in it) as an unpinned package.
            var channelConfigCommand = installer == "micromamba"
                ? "micromamba config append channels "
                : "conda config --add channels ";
            foreach (var channel in channels)
            {
                if (string.IsNullOrWhiteSpace(channel))
                {
                    continue;
                }

                dockerfile.Append("RUN ").Append(channelConfigCommand).Append(channel).Append('\n');
            }

            if (recipe.Packages.Count > 0)
            {
                // micromamba (unlike conda-forge/miniforge images) doesn't auto-activate
                // an environment for plain RUN steps, so "micromamba install" fails with
                // "No target prefix specified" unless a target env is named explicitly.
                var envArgs = installer == "micromamba" ? "-n base " : string.Empty;
                dockerfile.Append("RUN ").Append(installer).Append(" install ").Append(envArgs).Append("-y ")
                    .Append(string.Join(' ', recipe.Packages)).Append('\n');
            }

            definition.DockerfileContent = dockerfile.ToString();
        }

        private static void RenderBioContainer(RecipeDocument recipe, ToolDefinition definition)
        {
            definition.ImageUri = recipe.BioContainerImageUri;
            definition.DockerfileContent = $"FROM {recipe.BioContainerImageUri}\n";
        }

        private static void RenderSourceBuild(RecipeDocument recipe, ToolDefinition definition)
        {
            definition.ImageUri = recipe.BaseImage;

            var buildCommands = recipe.SourceBuildCommands.Count > 0
                ? string.Join(" && ", recipe.SourceBuildCommands)
                : string.Empty;

            // sha256sum -c expects a bare hex digest, not the "sha256:" prefix
            // RecipeDocument.SourceChecksum carries for self-description.
            var checksumHex = recipe.SourceChecksum.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? recipe.SourceChecksum["sha256:".Length..]
                : recipe.SourceChecksum;

            var dockerfile = new StringBuilder();
            dockerfile.Append("FROM ").Append(recipe.BaseImage).Append('\n');
            dockerfile.Append("RUN curl -fsSL -o source.tar.gz \"").Append(recipe.SourceUri).Append("\" && ")
                .Append("echo \"").Append(checksumHex).Append("  source.tar.gz\" | sha256sum -c - && ")
                .Append("tar -xzf source.tar.gz");

            if (buildCommands.Length > 0)
            {
                dockerfile.Append(" && ").Append(buildCommands);
            }

            dockerfile.Append('\n');

            // SourceBuildCommands runs arbitrary shell (unlike Conda/Micromamba/
            // PackageMirror, which only ever install pinned packages), so this is
            // the one auto-generated build kind with real-Dockerfile-fallback-level
            // exposure. The build itself still runs before this line (as whatever
            // user the base image defaults to), so this only fixes the image's
            // runtime default user, not the build step.
            dockerfile.Append("USER 1000\n");
            definition.DockerfileContent = dockerfile.ToString();
        }
    }
}
