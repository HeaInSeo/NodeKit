using System;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Threading;

namespace NodeKit.Cli.Localization
{
    /// <summary>
    /// UF01 (#96): 사람이 읽는 CLI 문구를 resource에서 꺼낸다. 한국어(<c>CliStringsKo.resx</c>)가 기준 문구이고 영어
    /// (<c>CliStringsEn.resx</c>)에 key가 없으면 한국어로 돌아간다. 두 파일 모두 주 assembly에 들어가는 중립 resource라
    /// satellite assembly나 OS culture(ICU) 데이터에 기대지 않는다 — invariant globalization package에서도 같은 결과다.
    /// <para>
    /// 현재 언어는 <see cref="Use"/>로 연 범위에만 적용되는 <see cref="AsyncLocal{T}"/>다. 프로세스 전역
    /// <see cref="CultureInfo.CurrentUICulture"/>를 바꾸지 않으므로 같은 프로세스의 반복·병렬 <c>CliApp.Run</c> 사이에
    /// 언어가 새지 않는다. 범위 밖 기본값은 한국어다.
    /// </para>
    /// <para>
    /// 이 문구는 human output 전용이다. Recipe/raw_spec/request bytes, JSONL key·값, exit code는 언어와 무관하며 여기서
    /// 만들지 않는다.
    /// </para>
    /// </summary>
    internal static class CliText
    {
        private static readonly Assembly _resourceAssembly = typeof(CliText).Assembly;

        internal static readonly ResourceManager Ko =
            new("NodeKit.Cli.Resources.CliStringsKo", _resourceAssembly);

        internal static readonly ResourceManager En =
            new("NodeKit.Cli.Resources.CliStringsEn", _resourceAssembly);

        private static readonly AsyncLocal<CliLanguage?> _current = new();

        public static CliLanguage Current => _current.Value ?? CliLanguage.Ko;

        /// <summary>이 범위 동안 <paramref name="language"/>로 출력한다. Dispose하면 이전 언어로 돌아간다.</summary>
        public static IDisposable Use(CliLanguage language)
        {
            var previous = _current.Value;
            _current.Value = language;
            return new Scope(previous);
        }

        public static string Get(string key)
        {
            if (Current == CliLanguage.En)
            {
                var en = En.GetString(key, CultureInfo.InvariantCulture);
                if (en is not null)
                {
                    return en;
                }
            }

            return Ko.GetString(key, CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException($"CLI resource key is missing from CliStringsKo.resx: {key}");
        }

        public static string Format(string key, params object?[] args) =>
            string.Format(CultureInfo.InvariantCulture, Get(key), args);

        private sealed class Scope : IDisposable
        {
            private readonly CliLanguage? _previous;
            private bool _disposed;

            public Scope(CliLanguage? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _current.Value = _previous;
            }
        }
    }
}
