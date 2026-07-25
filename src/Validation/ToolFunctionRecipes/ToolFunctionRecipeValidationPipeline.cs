using NodeKit.Authoring.ToolFunctionRecipes;

namespace NodeKit.Validation.ToolFunctionRecipes
{
    /// <summary>
    /// nodekit function-recipe create/validate가 공유하는 단일 L1 검증
    /// 게이트(FR-017/FR-018). ToolFunctionRecipeValidator.Validate(...) 하나만
    /// 호출하는 얇은 래퍼 — RecipeValidationPipeline과 달리 렌더링 단계로
    /// 이어지는 추가 L1 validator 체인이 없다(ToolFunctionRecipe는
    /// ToolDefinition으로 렌더링되지 않으므로).
    ///
    /// 검증 통과 시 recipe.State를 Ready로 전이시킨다(메모리 상에서만 — 파일
    /// 저장은 호출자(CLI 커맨드)의 책임이다). 실패 시 State는 건드리지 않는다.
    /// </summary>
    internal static class ToolFunctionRecipeValidationPipeline
    {
        public static ValidationResult Validate(ToolFunctionRecipe recipe)
        {
            var result = ToolFunctionRecipeValidator.Validate(recipe);
            if (result.IsValid)
            {
                recipe.State = ToolFunctionRecipeState.Ready;
            }

            return result;
        }
    }
}
