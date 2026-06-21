using System;
using System.Collections.Generic;
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

            string pending = string.Empty;

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
                    pending += line[..^1] + " ";
                    continue;
                }

                var fullLine = (pending + line).Trim();
                pending = string.Empty;

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
                    instruction.Value = new List<string>(
                        rest.Split(_spaceSeparators, StringSplitOptions.RemoveEmptyEntries));
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
    }
}
