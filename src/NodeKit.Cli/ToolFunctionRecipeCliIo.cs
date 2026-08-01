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
            recipe = null;
            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                stderr.WriteLine($"recipe 파일을 읽을 수 없습니다: {path} ({ex.Message})");
                return false;
            }

            try
            {
                recipe = JsonSerializer.Deserialize<ToolFunctionRecipe>(content, JsonOptions);
            }
            catch (JsonException ex)
            {
                stderr.WriteLine($"recipe JSON 파싱에 실패했습니다: {path} ({ex.Message})");
                return false;
            }

            if (recipe is null)
            {
                stderr.WriteLine($"recipe 파일이 비어있습니다: {path}");
                return false;
            }

            recipe.Normalize();
            return true;
        }
    }
}
