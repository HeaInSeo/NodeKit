using System;
using System.Collections.Generic;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Validation;

namespace NodeKit.Cli
{
    /// <summary>
    /// Parses the `--input Name=Spec` / `--output Name=Spec` grammar:
    /// first comma-token is a preset id or "custom". Custom inputs take
    /// role,format,shape[,optional]; custom outputs take role,format,class.
    /// Preset-based inputs may append ",optional" to set Required=false.
    /// </summary>
    internal static class RecipeCreateInputOutputSpec
    {
        public static IReadOnlyList<ValidationViolation> ApplyInput(RecipeAuthoringSession session, string name, string spec)
        {
            var (input, violations) = BuildInput(name, spec);
            return input != null ? session.AppendListItem("Inputs", input) : violations;
        }

        public static IReadOnlyList<ValidationViolation> ApplyOutput(RecipeAuthoringSession session, string name, string spec)
        {
            var (output, violations) = BuildOutput(name, spec);
            return output != null ? session.AppendListItem("Outputs", output) : violations;
        }

        public static IReadOnlyList<ValidationViolation> EditInput(RecipeAuthoringSession session, int index, string name, string spec)
        {
            var (input, violations) = BuildInput(name, spec);
            return input != null ? session.EditListItem("Inputs", index, input) : violations;
        }

        public static IReadOnlyList<ValidationViolation> EditOutput(RecipeAuthoringSession session, int index, string name, string spec)
        {
            var (output, violations) = BuildOutput(name, spec);
            return output != null ? session.EditListItem("Outputs", index, output) : violations;
        }

        private static (ToolInput? Input, IReadOnlyList<ValidationViolation> Violations) BuildInput(string name, string spec)
        {
            var tokens = spec.Split(',');

            if (tokens[0] == InputOutputPresetCatalog.CustomPresetId)
            {
                if (tokens.Length < 4)
                {
                    return (null, MalformedSpec(name, "custom,role,format,shape[,optional]"));
                }

                return (new ToolInput
                {
                    Name = name,
                    Role = tokens[1],
                    Format = tokens[2],
                    Shape = tokens[3],
                    Required = !IsOptional(tokens, 4),
                }, Array.Empty<ValidationViolation>());
            }

            var preset = InputOutputPresetCatalog.FindInputPreset(tokens[0]);
            return (new ToolInput
            {
                Name = name,
                Role = preset.Role,
                Format = preset.Format,
                Shape = preset.Shape,
                Required = !IsOptional(tokens, 1),
            }, Array.Empty<ValidationViolation>());
        }

        private static (ToolOutput? Output, IReadOnlyList<ValidationViolation> Violations) BuildOutput(string name, string spec)
        {
            var tokens = spec.Split(',');

            if (tokens[0] == InputOutputPresetCatalog.CustomPresetId)
            {
                if (tokens.Length < 4)
                {
                    return (null, MalformedSpec(name, "custom,role,format,class"));
                }

                return (new ToolOutput { Name = name, Role = tokens[1], Format = tokens[2], Class = tokens[3] }, Array.Empty<ValidationViolation>());
            }

            var preset = InputOutputPresetCatalog.FindOutputPreset(tokens[0]);
            return (new ToolOutput { Name = name, Role = preset.Role, Format = preset.Format, Class = preset.Class }, Array.Empty<ValidationViolation>());
        }

        private static bool IsOptional(string[] tokens, int optionalTokenIndex) =>
            tokens.Length > optionalTokenIndex && tokens[optionalTokenIndex] == "optional";

        private static ValidationViolation[] MalformedSpec(string name, string expectedShape) =>
            new[]
            {
                new ValidationViolation(
                    "CLI-SPEC-001",
                    $"{name} 항목의 custom spec 형식이 올바르지 않습니다. 형식: {expectedShape}",
                    name),
            };
    }
}
