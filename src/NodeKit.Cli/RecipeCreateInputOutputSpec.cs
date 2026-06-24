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
            var tokens = spec.Split(',');

            ToolInput input;
            if (tokens[0] == InputOutputPresetCatalog.CustomPresetId)
            {
                if (tokens.Length < 4)
                {
                    return MalformedSpec(name, "custom,role,format,shape[,optional]");
                }

                input = new ToolInput
                {
                    Name = name,
                    Role = tokens[1],
                    Format = tokens[2],
                    Shape = tokens[3],
                    Required = !IsOptional(tokens, 4),
                };
            }
            else
            {
                var preset = InputOutputPresetCatalog.FindInputPreset(tokens[0]);
                input = new ToolInput
                {
                    Name = name,
                    Role = preset.Role,
                    Format = preset.Format,
                    Shape = preset.Shape,
                    Required = !IsOptional(tokens, 1),
                };
            }

            return session.AppendListItem("Inputs", input);
        }

        public static IReadOnlyList<ValidationViolation> ApplyOutput(RecipeAuthoringSession session, string name, string spec)
        {
            var tokens = spec.Split(',');

            ToolOutput output;
            if (tokens[0] == InputOutputPresetCatalog.CustomPresetId)
            {
                if (tokens.Length < 4)
                {
                    return MalformedSpec(name, "custom,role,format,class");
                }

                output = new ToolOutput { Name = name, Role = tokens[1], Format = tokens[2], Class = tokens[3] };
            }
            else
            {
                var preset = InputOutputPresetCatalog.FindOutputPreset(tokens[0]);
                output = new ToolOutput { Name = name, Role = preset.Role, Format = preset.Format, Class = preset.Class };
            }

            return session.AppendListItem("Outputs", output);
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
