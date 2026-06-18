using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using NodeKit.Authoring;
using NodeKit.Policy;

namespace NodeKit.UI
{
    internal static class MainWindowFormHelpers
    {
        public static WasmPolicyChecker? TryLoadPolicyChecker()
        {
            try
            {
                var wasmPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "assets",
                    "policy",
                    "dockguard.wasm");

                if (!File.Exists(wasmPath))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(wasmPath);
                return new WasmPolicyChecker(new PolicyBundle(bytes, $"local:{Path.GetFileName(wasmPath)}"));
            }
#pragma warning disable CA1031
            catch
            {
                return null;
            }
#pragma warning restore CA1031
        }

        public static List<ToolInput> CollectInputSpecs(StackPanel panel)
        {
            var result = new List<ToolInput>();
            foreach (var child in panel.Children)
            {
                if (child is not Grid row)
                {
                    continue;
                }

                var boxes = row.Children.OfType<TextBox>().ToList();
                var combos = row.Children.OfType<ComboBox>().ToList();
                var name = boxes.ElementAtOrDefault(0)?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                result.Add(new ToolInput
                {
                    Name = name,
                    Role = boxes.ElementAtOrDefault(1)?.Text?.Trim() ?? string.Empty,
                    Format = boxes.ElementAtOrDefault(2)?.Text?.Trim() ?? string.Empty,
                    Shape = combos.ElementAtOrDefault(0)?.SelectedItem?.ToString() ?? "single",
                    Required = true,
                });
            }

            return result;
        }

        public static List<ToolOutput> CollectOutputSpecs(StackPanel panel)
        {
            var result = new List<ToolOutput>();
            foreach (var child in panel.Children)
            {
                if (child is not Grid row)
                {
                    continue;
                }

                var boxes = row.Children.OfType<TextBox>().ToList();
                var combos = row.Children.OfType<ComboBox>().ToList();
                var name = boxes.ElementAtOrDefault(0)?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                result.Add(new ToolOutput
                {
                    Name = name,
                    Role = boxes.ElementAtOrDefault(1)?.Text?.Trim() ?? string.Empty,
                    Format = boxes.ElementAtOrDefault(2)?.Text?.Trim() ?? string.Empty,
                    Shape = combos.ElementAtOrDefault(0)?.SelectedItem?.ToString() ?? "single",
                    Class = combos.ElementAtOrDefault(1)?.SelectedItem?.ToString() ?? "primary",
                });
            }

            return result;
        }

        public static List<string> ParseCommandJson(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(raw.Trim());
                return parsed ?? new List<string>();
            }
#pragma warning disable CA1031
            catch
            {
                return new List<string>();
            }
#pragma warning restore CA1031
        }
    }
}
