using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wasmtime;

namespace NodeKit.Policy
{
    internal static class OpaWasmHelpers
    {
        public static int BuiltinRegexMatch(Memory memory, int patternPtr, int valuePtr)
        {
            try
            {
                var pattern = ReadOpaString(memory, patternPtr);
                var value = ReadOpaString(memory, valuePtr);
                if (pattern == null || value == null)
                {
                    return 0;
                }

                return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase) ? 1 : 0;
            }
#pragma warning disable CA1031
            catch
            {
                return 0;
            }
#pragma warning restore CA1031
        }

        public static int BuiltinRegexIsValid(Memory memory, int patternPtr)
        {
            try
            {
                var pattern = ReadOpaString(memory, patternPtr);
                if (pattern == null)
                {
                    return 0;
                }

                _ = new Regex(pattern);
                return 1;
            }
#pragma warning disable CA1031
            catch
            {
                return 0;
            }
#pragma warning restore CA1031
        }

        public static string SerializeInstructions(List<DockerfileInstruction> instructions)
        {
            using var ms = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(ms);
            writer.WriteStartArray();
            foreach (var inst in instructions)
            {
                writer.WriteStartObject();
                writer.WriteString("Cmd", inst.Cmd);
                writer.WriteString("Raw", inst.Raw);
                writer.WritePropertyName("Value");
                writer.WriteStartArray();
                foreach (var value in inst.Value)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.Flush();
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        public static PolicyResult ParseResult(string resultJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(resultJson);
                var violations = new List<PolicyViolation>();

                foreach (var entry in doc.RootElement.EnumerateArray())
                {
                    if (!entry.TryGetProperty("result", out var result))
                    {
                        continue;
                    }

                    if (result.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var msg in result.EnumerateArray())
                    {
                        var message = msg.GetString() ?? string.Empty;
                        var ruleId = ExtractRuleId(message);
                        violations.Add(new PolicyViolation(ruleId, message));
                    }
                }

                return new PolicyResult(violations);
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                return new PolicyResult(new[]
                {
                    new PolicyViolation("WASM-PARSE-ERR", $"결과 파싱 실패: {ex.Message}. raw={resultJson}"),
                });
            }
#pragma warning restore CA1031
        }

        private static string? ReadOpaString(Memory memory, int ptr)
        {
            if (ptr == 0)
            {
                return null;
            }

            try
            {
                var memLen = (int)memory.GetLength();
                if (ptr + 8 > memLen)
                {
                    return null;
                }

                var type = memory.ReadInt32(ptr);
                if (type != 3)
                {
                    return memory.ReadNullTerminatedString(ptr);
                }

                var len = memory.ReadInt32(ptr + 4);
                if (len < 0 || len > 65536 || ptr + 8 + len > memLen)
                {
                    return null;
                }

                return memory.ReadString(ptr + 8, len, Encoding.UTF8);
            }
#pragma warning disable CA1031
            catch
            {
                return null;
            }
#pragma warning restore CA1031
        }

        private static string ExtractRuleId(string message)
        {
            var match = Regex.Match(message, @"^(DFM\d+):");
            return match.Success ? match.Groups[1].Value : "DFM000";
        }
    }
}
