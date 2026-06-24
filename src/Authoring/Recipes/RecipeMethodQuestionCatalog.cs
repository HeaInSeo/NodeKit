using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Fixed recommender question order — independent of RecipeMethodAnswers'
    /// record field order. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 10.3.
    /// </summary>
    internal static class RecipeMethodQuestionCatalog
    {
        public static IReadOnlyList<RecipeMethodQuestion> Questions { get; } = new[]
        {
            new RecipeMethodQuestion(
                nameof(RecipeMethodAnswers.IsRestrictedNetwork),
                Text("내부망/폐쇄망 환경인가요?", "Is this a restricted/closed network environment?")),
            new RecipeMethodQuestion(
                nameof(RecipeMethodAnswers.HasInternalPackageMirror),
                Text("내부 package mirror URI를 아시나요?", "Do you know an internal package mirror URI?")),
            new RecipeMethodQuestion(
                nameof(RecipeMethodAnswers.HasExistingContainerImage),
                Text("기존 컨테이너 이미지 URI가 있나요?", "Do you have an existing container image URI?")),
            new RecipeMethodQuestion(
                nameof(RecipeMethodAnswers.HasPackageInPublicChannels),
                Text("public channel에 패키지가 있나요?", "Does the package exist in a public channel?")),
            new RecipeMethodQuestion(
                nameof(RecipeMethodAnswers.HasSourceArchiveAndChecksum),
                Text("source URL과 checksum이 있나요?", "Do you have a source URL and checksum?")),
            new RecipeMethodQuestion(
                nameof(RecipeMethodAnswers.HasExistingDockerfile),
                Text("기존 Dockerfile이 있나요?", "Do you have an existing Dockerfile?")),
        };

        private static LocalizedText Text(string ko, string en) =>
            new(new Dictionary<string, string> { ["ko"] = ko, ["en"] = en });
    }
}
