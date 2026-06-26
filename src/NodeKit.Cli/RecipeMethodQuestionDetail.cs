using System.Collections.Generic;

namespace NodeKit.Cli
{
    internal sealed record RecipeMethodQuestionDetail(
        string Header,
        string Meaning,
        IReadOnlyList<string> Examples,
        IReadOnlyList<string> YesEffects,
        IReadOnlyList<string> NoEffects,
        IReadOnlyList<string> EnterEffects);
}
