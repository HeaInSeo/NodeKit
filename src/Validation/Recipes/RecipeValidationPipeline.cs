using System;
using System.Collections.Generic;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Validation.Recipes
{
    /// <summary>
    /// Single L1 validation gate shared by nodekit validate, nodekit render,
    /// and the future nodekit recipe create — see
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 19.3.
    /// Combines recipe-level checks (RecipeValidator) with the existing
    /// ToolDefinition-level L1 chain after RecipeRenderer flattens the recipe.
    /// </summary>
    internal static class RecipeValidationPipeline
    {
        public static ValidationResult ValidateRecipe(RecipeDocument recipe, bool strictReproducible = false)
        {
            if (recipe.BuildKind is null)
            {
                throw new InvalidOperationException(
                    "Recipe BuildKind가 설정되지 않았습니다. " +
                    "ValidateRecipe() 호출 전에 RecipeKindResolver.Resolve()를 먼저 호출하세요.");
            }

            // 목록 원소 null은 아래 validator/renderer를 NullReferenceException으로
            // 깨뜨리므로 먼저 L1 위반으로 돌려준다(S1-02-C05).
            var nullElements = NullCollectionElementValidator.Validate(recipe, "L1-RCP-019");
            if (!nullElements.IsValid)
            {
                return nullElements;
            }

            var recipeResult = RecipeValidator.Validate(recipe, strictReproducible);
            var definition = RecipeRenderer.Render(recipe);

            IValidator[] l1Validators =
            {
                new RequiredFieldsValidator(),
                new ImageUriValidator(),
                new DockerfileStructureValidator(),
                new PackageVersionValidator(),
            };

            var results = new List<ValidationResult> { recipeResult };
            foreach (var validator in l1Validators)
            {
                results.Add(validator.Validate(definition));
            }

            return ValidationResult.Combine(results);
        }
    }
}
