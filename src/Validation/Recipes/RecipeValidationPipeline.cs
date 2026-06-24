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
        public static ValidationResult ValidateRecipe(RecipeDocument recipe)
        {
            var recipeResult = RecipeValidator.Validate(recipe);
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
