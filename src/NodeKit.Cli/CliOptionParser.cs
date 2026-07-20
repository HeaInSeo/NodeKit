using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit CLI 하위 명령(submit/validate/render)이 공유하는 옵션 파서.
    /// 알려지지 않은 옵션, 값 옵션의 값 누락/중복/다른 옵션처럼 보이는 값을
    /// 명시적 에러로 stderr에 남기고 false를 반환한다 — 예전에는 명령마다
    /// 각자 Array.IndexOf 기반으로 느슨하게 파싱해서 명령별로 엄격도가
    /// 달랐다(submit만 엄격, validate/render는 미지원 옵션을 조용히 무시).
    /// </summary>
    internal static class CliOptionParser
    {
        public static bool TryParse(
            string[] args,
            int startIndex,
            TextWriter stderr,
            IReadOnlyCollection<string> valueOptions,
            IReadOnlyCollection<string> flagOptions,
            out Dictionary<string, string> values,
            out HashSet<string> flags)
        {
            values = new Dictionary<string, string>();
            flags = new HashSet<string>();

            for (var i = startIndex; i < args.Length; i++)
            {
                var arg = args[i];
                if (valueOptions.Contains(arg))
                {
                    if (values.ContainsKey(arg))
                    {
                        stderr.WriteLine($"{arg} 옵션이 여러 번 지정되었습니다.");
                        return false;
                    }

                    // 다음 토큰이 없거나 그 자체가 또 다른 옵션처럼 보이면(-- 로 시작)
                    // "값 누락"으로 취급한다 — 그렇지 않으면 `--out --strict-reproducible`
                    // 같은 실수가 "--strict-reproducible"을 값으로 그대로 삼켜버린다.
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", System.StringComparison.Ordinal))
                    {
                        stderr.WriteLine($"{arg} 옵션에는 값이 필요합니다.");
                        return false;
                    }

                    values[arg] = args[i + 1];
                    i++;
                    continue;
                }

                if (flagOptions.Contains(arg))
                {
                    flags.Add(arg);
                    continue;
                }

                var supported = string.Join(", ", valueOptions.Select(o => $"{o} <value>").Concat(flagOptions));
                stderr.WriteLine($"알 수 없는 옵션입니다: {arg} (지원: {supported})");
                return false;
            }

            return true;
        }
    }
}
