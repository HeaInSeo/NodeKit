using System;
using System.Threading;

namespace NodeKit.Cli
{
    // Test-only ambient override for RecipeCreateInteractiveRunner's resolveClient
    // fallback chain. Needed because tests that exercise CliApp.Run end-to-end have
    // no way to pass resolveClient explicitly (RecipeCreateCommand.Run calls
    // RecipeCreateInteractiveRunner.Run with no such parameter, matching real CLI
    // usage). Without this, those tests silently depend on NODEKIT_NODEVAULT_URL
    // being absent from the environment to land on the deterministic
    // NullResolveRecipeClient fallback — set that var (e.g. to run the opt-in live
    // integration tests) and they instead hit the real GrpcResolveRecipeClient.
    // Never set outside tests.
    internal static class ResolveRecipeClientTestOverride
    {
        private static readonly AsyncLocal<IResolveRecipeClient?> _current = new();

        internal static IResolveRecipeClient? Current => _current.Value;

        internal static IDisposable Use(IResolveRecipeClient client)
        {
            var previous = _current.Value;
            _current.Value = client;
            return new Restorer(previous);
        }

        private sealed class Restorer : IDisposable
        {
            private readonly IResolveRecipeClient? _previous;

            public Restorer(IResolveRecipeClient? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                _current.Value = _previous;
            }
        }
    }
}
