using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodeKit.Authoring.ToolFunctionRecipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// validate/render/submit이 공유하는 ToolFunctionRecipe 파일 읽기 —
    /// CliApp.TryLoadRecipe(RecipeDocument용)와 동일한 관례.
    /// </summary>
    internal static class ToolFunctionRecipeCliIo
    {
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static bool TryLoad(string path, TextWriter stderr, out ToolFunctionRecipe? recipe)
        {
            if (!AuthoringFileLoader.TryLoad(path, JsonOptions, out recipe, out var error))
            {
                stderr.WriteLine(error!.Message);
                return false;
            }

            recipe!.Normalize();
            return true;
        }
    }
}
