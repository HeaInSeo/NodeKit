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
        private static readonly char[] SpaceSeparators = { ' ', '\t' };

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

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();

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

                var parts = fullLine.Split(SpaceSeparators, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                var cmd = parts[0].ToUpperInvariant();
                var rest = parts.Length > 1 ? parts[1] : string.Empty;

                var instruction = new DockerfileInstruction
                {
                    Cmd = cmd,
                    Raw = fullLine,
                };

                // COPY: 인자를 개별 토큰으로 분리 (--from=builder 감지용)
                if (cmd == "COPY")
                {
                    instruction.Value = new List<string>(
                        rest.Split(SpaceSeparators, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (cmd == "FROM")
                {
                    instruction.Value = new List<string>(
                        rest.Split(SpaceSeparators, StringSplitOptions.RemoveEmptyEntries));
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
