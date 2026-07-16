using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NodeKit.Policy
{
    /// <summary>
    /// Dockerfile 내용을 DockGuard OPA 정책이 기대하는 명령어 목록으로 파싱한다.
    /// 완전한 Dockerfile 파서가 아닌, DFM001~DFM004 검사에 필요한 구조만 추출한다.
    /// </summary>
    internal static class DockerfileParser
    {
        private static readonly char[] _spaceSeparators = { ' ', '\t' };

        private static readonly Regex _heredocPattern = new(
            @"<<-?\s*(['""]?)(?<delim>[A-Za-z_][A-Za-z0-9_]*)\1",
            RegexOptions.Compiled);

        /// <summary>
        /// Dockerfile 내용을 파싱하여 명령어 목록을 반환한다.
        /// </summary>
        public static List<DockerfileInstruction> Parse(string dockerfile)
        {
            if (string.IsNullOrWhiteSpace(dockerfile))
            {
                return new List<DockerfileInstruction>();
            }

            var instructions = new List<DockerfileInstruction>();
            var lines = dockerfile.Split('\n', StringSplitOptions.None);

            var pending = new StringBuilder();

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();

                // 주석 및 빈 줄 건너뜀
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                // 줄 이음 처리 (백슬래시 연속)
                if (line.EndsWith('\\'))
                {
                    pending.Append(line[..^1]).Append(' ');
                    continue;
                }

                var fullLine = (pending.ToString() + line).Trim();
                pending.Clear();

                if (string.IsNullOrWhiteSpace(fullLine))
                {
                    continue;
                }

                var parts = fullLine.Split(_spaceSeparators, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                var cmd = parts[0].ToUpperInvariant();
                var rest = parts.Length > 1 ? parts[1] : string.Empty;

                // heredoc 본문은 별도 명령어로 파싱하지 않고 건너뛴다 (예: RUN <<EOF ... EOF)
                var heredocMatch = _heredocPattern.Match(rest);
                if (heredocMatch.Success)
                {
                    var delimiter = heredocMatch.Groups["delim"].Value;
                    var bodyEnd = i + 1;
                    while (bodyEnd < lines.Length && lines[bodyEnd].Trim() != delimiter)
                    {
                        bodyEnd++;
                    }

                    i = bodyEnd;
                }

                var instruction = new DockerfileInstruction
                {
                    Cmd = cmd,
                    Raw = fullLine,
                };

                // COPY/ADD: 인자를 개별 토큰으로 분리 (--from=builder 감지용)
                if (cmd == "COPY" || cmd == "ADD")
                {
                    instruction.Value = ParseCopyOrAddArgs(rest);
                }
                else if (cmd == "FROM")
                {
                    instruction.Value = new List<string>(
                        rest.Split(_spaceSeparators, StringSplitOptions.RemoveEmptyEntries));
                }
                else
                {
                    instruction.Value = string.IsNullOrEmpty(rest)
                        ? new List<string>()
                        : new List<string> { rest };
                }

                instructions.Add(instruction);
            }

            return instructions;
        }

        // COPY/ADD도 exec-form과 마찬가지로 JSON 배열 문법(예: ADD ["url", "dest"])을
        // 쓸 수 있다. 이전에는 공백으로만 split해서 콤마 뒤 공백이 있는 흔한 포맷에서
        // 첫 토큰이 "[\"https://..."가 되어 DockerfileStructureValidator의 remote-source
        // 문자열 접두어 검사(그리고 ".."/변수 참조 검사도 마찬가지로)를 우회했다.
        // "--from=builder"처럼 배열 앞에 올 수 있는 플래그를 먼저 떼어낸 뒤, 남은
        // 부분이 '['로 시작하면 실제 JSON 배열로 파싱한다. 형식이 JSON이 아니거나
        // 파싱에 실패하면 기존 공백 split으로 폴백한다.
        private static List<string> ParseCopyOrAddArgs(string rest)
        {
            var flags = new List<string>();
            var remainder = rest.TrimStart();
            while (remainder.StartsWith("--", StringComparison.Ordinal))
            {
                var spaceIndex = remainder.IndexOfAny(_spaceSeparators);
                if (spaceIndex < 0)
                {
                    flags.Add(remainder);
                    remainder = string.Empty;
                    break;
                }

                flags.Add(remainder[..spaceIndex]);
                remainder = remainder[(spaceIndex + 1)..].TrimStart();
            }

            if (remainder.StartsWith('['))
            {
                try
                {
                    var values = JsonSerializer.Deserialize<List<string>>(remainder);
                    if (values is not null)
                    {
                        flags.AddRange(values);
                        return flags;
                    }
                }
                catch (JsonException)
                {
                    // 배열처럼 보이지만 형식이 잘못된 경우 — 아래 공백 split
                    // 폴백으로 넘어가서 최소한 무언가는 검사받게 한다.
                }
            }

            flags.AddRange(remainder.Split(_spaceSeparators, StringSplitOptions.RemoveEmptyEntries));
            return flags;
        }
    }
}
