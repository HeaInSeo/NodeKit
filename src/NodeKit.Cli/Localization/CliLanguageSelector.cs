using System;
using System.Collections.Generic;
using System.IO;

namespace NodeKit.Cli.Localization
{
    /// <summary>
    /// UF01 (#96): CLI 출력 언어를 정한다. 우선순위는 다음과 같다.
    /// <list type="number">
    /// <item><c>--lang &lt;값&gt;</c> 또는 <c>--lang=&lt;값&gt;</c> (명령 앞·뒤 어디든 한 번). 지원하지 않는 값, 값 누락,
    /// 중복 지정은 exit 2 사용 오류다.</item>
    /// <item>환경 변수 <c>NODEKIT_LANG</c>. 비어 있으면 없는 것으로 본다. 지원하지 않는 값이면 경고 한 줄을 stderr에 남기고
    /// 한국어로 표시한다(명령은 계속 실행한다).</item>
    /// <item>기본값 한국어. OS의 <c>LANG</c>/<c>LC_ALL</c>은 보지 않는다 — 영어 OS에서도 기존 한국어 기본 UX를 지킨다.</item>
    /// </list>
    /// 지원값은 기본 언어 subtag가 <c>ko</c> 또는 <c>en</c>인 값이다(대소문자 무시, <c>_</c>는 <c>-</c>와 같고 <c>.UTF-8</c> 같은
    /// encoding 접미사는 무시). 예: <c>ko</c>, <c>ko-KR</c>, <c>en</c>, <c>en_US.UTF-8</c>.
    /// </summary>
    internal static class CliLanguageSelector
    {
        public const string OptionName = "--lang";
        public const string EnvironmentVariable = "NODEKIT_LANG";

        /// <summary>
        /// <paramref name="args"/>에서 <c>--lang</c>을 찾아 지우고 언어를 정한다. 실패하면 사용 오류를 stderr에 쓰고 false를
        /// 돌려준다. 그때 <paramref name="language"/>는 오류 문구를 쓴 언어(환경 변수 또는 한국어)다.
        /// </summary>
        public static bool TrySelect(
            string[] args,
            Func<string, string?> getEnvironmentVariable,
            TextWriter stderr,
            out CliLanguage language,
            out string[] remainingArgs)
        {
            var fromEnvironment = FromEnvironment(getEnvironmentVariable, out var unsupportedEnvironmentValue);
            language = fromEnvironment ?? CliLanguage.Ko;
            remainingArgs = args;

            string? optionValue = null;
            var found = false;
            var kept = new List<string>(args.Length);
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string? value;
                if (arg == OptionName)
                {
                    // 다른 option 파서와 같이, 값이 없거나 다음 token이 또 다른 option처럼 보이면 값 누락이다.
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        WriteError(language, stderr, "Option_MissingValue", OptionName);
                        return false;
                    }

                    value = args[++i];
                }
                else if (arg.StartsWith(OptionName + "=", StringComparison.Ordinal))
                {
                    value = arg[(OptionName.Length + 1)..];
                }
                else
                {
                    kept.Add(arg);
                    continue;
                }

                if (found)
                {
                    WriteError(language, stderr, "Option_Duplicate", OptionName);
                    return false;
                }

                found = true;
                optionValue = value;
            }

            if (found)
            {
                if (TryParse(optionValue, out var fromOption))
                {
                    language = fromOption;
                }
                else
                {
                    WriteError(language, stderr, "Lang_InvalidOption", optionValue);
                    return false;
                }
            }
            else if (unsupportedEnvironmentValue is not null)
            {
                WriteError(language, stderr, "Lang_EnvironmentUnsupported", unsupportedEnvironmentValue);
            }

            remainingArgs = kept.ToArray();
            return true;
        }

        /// <summary>지원값이면 언어로 바꾼다. 빈 값과 null은 지원값이 아니다.</summary>
        public static bool TryParse(string? value, out CliLanguage language)
        {
            language = CliLanguage.Ko;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim().Replace('_', '-');
            var end = normalized.IndexOfAny(new[] { '-', '.', '@' });
            var primary = (end < 0 ? normalized : normalized[..end]).ToLowerInvariant();
            switch (primary)
            {
                case "ko":
                    language = CliLanguage.Ko;
                    return true;
                case "en":
                    language = CliLanguage.En;
                    return true;
                default:
                    return false;
            }
        }

        private static CliLanguage? FromEnvironment(Func<string, string?> getEnvironmentVariable, out string? unsupportedValue)
        {
            unsupportedValue = null;
            var raw = getEnvironmentVariable(EnvironmentVariable);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (TryParse(raw, out var language))
            {
                return language;
            }

            unsupportedValue = raw;
            return null;
        }

        // 선택 실패 오류는 아직 CliApp의 언어 범위가 열리기 전에 쓰이므로 여기서 그 언어(환경 변수 또는 한국어)로 연다.
        private static void WriteError(CliLanguage language, TextWriter stderr, string key, string? argument)
        {
            using (CliText.Use(language))
            {
                stderr.WriteLine(CliText.Format(key, argument));
            }
        }
    }
}
