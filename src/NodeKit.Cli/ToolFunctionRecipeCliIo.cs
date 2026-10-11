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

        /// <summary>
        /// 위와 같지만 진단을 직접 쓰지 않고, 파싱한 바로 그 파일 bytes도 돌려준다(submit의 source snapshot용).
        /// </summary>
        public static bool TryLoad(string path, out ToolFunctionRecipe? recipe, out AuthoringLoadError? error, out byte[]? rawBytes)
        {
            if (!AuthoringFileLoader.TryLoad(path, JsonOptions, out recipe, out error, out rawBytes))
            {
                return false;
            }

            recipe!.Normalize();
            return true;
        }
    }
}
