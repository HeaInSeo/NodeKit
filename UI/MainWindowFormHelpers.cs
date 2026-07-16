using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
                var wasmPath = Path.Join(
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

            // File I/O and wasmtime bundle-loading can fail in many ways
            // (missing file, permissions, corrupt/incompatible wasm) that
            // all warrant the same handling. Returning null here is NOT a
            // silent fail-open: ValidationViewModel.AddPolicyViolations
            // explicitly blocks with a "POLICY-UNAVAIL" violation when the
            // policy checker is null, so callers never validate anything
            // without either a real policy check or an explicit failure.
#pragma warning disable CA1031
            catch
            {
                return null;
            }
#pragma warning restore CA1031
        }

        /// <summary>Input 행에 "필수" 체크박스를 추가한다(기본 체크됨 — ToolInput.Required 기본값과 동일).</summary>
        public static CheckBox AddRequiredCheckBox(Grid row, int column)
        {
            var box = new CheckBox
            {
                Content = "필수",
                IsChecked = true,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(box, column);
            row.Children.Add(box);
            return box;
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
                var checkBoxes = row.Children.OfType<CheckBox>().ToList();
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
                    Required = checkBoxes.ElementAtOrDefault(0)?.IsChecked ?? true,
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
            catch (JsonException)
            {
                // Malformed user-typed JSON in the Command field — fall back
                // to an empty list rather than blocking the whole form.
                return new List<string>();
            }
        }
    }
}
