using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NodeKit.Authoring.ToolFunctionRecipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// fresh 요청 하나가 고정한 base image A와 script source. ScriptPath는 Recipe에
    /// 선언된 논리 상대 경로 그대로이며, 실행 환경의 절대 경로/CWD는 담지 않는다
    /// (portable source binding = 논리 상대 경로 + exact bytes/hash). Script는
    /// 파일에서 한 번 읽은 내용이고, 이후 원본 파일이 바뀌거나 지워져도 이 값은
    /// 바뀌지 않는다.
    /// </summary>
    internal sealed record ToolFunctionBaseScriptSnapshot(
        string BaseImageDigest,
        string ScriptPath,
        string Script,
        string ScriptSha256);

    /// <summary>
    /// materialize 실패 하나. ExitCode 2는 source 파일 입력 오류(없음/디렉터리/
    /// 읽기 거부/절대 경로/UTF-8 아님), 1은 Recipe 내용의 local validation 실패
    /// (base image digest 형식·누락, ScriptPath 누락, 빈 script)다.
    /// </summary>
    internal sealed record ToolFunctionSourceError(string Code, string Message, int ExitCode);

    /// <summary>
    /// P06.base_script: ToolFunctionRecipe의 exact base image A와 ScriptPath
    /// source를 fresh 요청용으로 읽고 검증해 snapshot으로 고정한다 (v0.9.1 계약
    /// S3-03, S3-02 base image 부분).
    ///
    /// ScriptPath는 Recipe 파일이 있는 디렉터리 기준 상대 host 경로다. CWD로
    /// 해석하지 않고, 절대 경로는 자동 rewrite 없이 거부한다. script는 client에서
    /// 실행하지 않고 내용만 읽는다. base image digest는 'sha256:' + 64 hex 형식만
    /// 확인하고 대소문자를 바꾸지 않는다 — canonical 값은 NodeVault가 정한다.
    /// 네트워크 호출은 하지 않는다. replay는 저장된 envelope를 쓰며 이 클래스를
    /// 다시 부르지 않는다.
    /// </summary>
    internal static class ToolFunctionSourceMaterializer
    {
        public const string MissingBaseImageCode = "MISSING_BASE_IMAGE_DIGEST";
        public const string InvalidBaseImageCode = "INVALID_BASE_IMAGE_DIGEST";
        public const string MissingScriptPathCode = "MISSING_SCRIPT_PATH";
        public const string AbsoluteScriptPathCode = "ABSOLUTE_SCRIPT_PATH";
        public const string ScriptReadFailedCode = "SCRIPT_READ_FAILED";
        public const string ScriptNotUtf8Code = "SCRIPT_NOT_UTF8";
        public const string BlankScriptCode = "BLANK_SCRIPT";

        // nodevault.build.raw_spec.v1 base_image_digest pattern과 같다.
        private static readonly Regex _baseImageDigestPattern =
            new(@"\Asha256:[0-9a-fA-F]{64}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        public static bool TryMaterialize(
            ToolFunctionRecipe recipe,
            string recipeFilePath,
            out ToolFunctionBaseScriptSnapshot? snapshot,
            out ToolFunctionSourceError? error)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            ArgumentException.ThrowIfNullOrEmpty(recipeFilePath);

            snapshot = null;
            if (!TryValidateBaseImageDigest(recipe.BaseToolImageDigest, out error))
            {
                return false;
            }

            if (!TryReadScript(recipe.ScriptPath, recipeFilePath, out var script, out var sha256, out error))
            {
                return false;
            }

            snapshot = new ToolFunctionBaseScriptSnapshot(recipe.BaseToolImageDigest, recipe.ScriptPath, script!, sha256!);
            return true;
        }

        public static bool TryValidateBaseImageDigest(string? digest, out ToolFunctionSourceError? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(digest))
            {
                error = new ToolFunctionSourceError(
                    MissingBaseImageCode,
                    "exact base image digest(baseToolImageDigest)가 없습니다. toolSpecDigest나 다른 이미지 digest로 대신하지 않습니다.",
                    1);
                return false;
            }

            if (!_baseImageDigestPattern.IsMatch(digest))
            {
                error = new ToolFunctionSourceError(
                    InvalidBaseImageCode,
                    $"baseToolImageDigest는 'sha256:' 뒤에 16진수 64자리여야 합니다: '{digest}'",
                    1);
                return false;
            }

            return true;
        }

        private static bool TryReadScript(
            string? scriptPath,
            string recipeFilePath,
            out string? script,
            out string? sha256,
            out ToolFunctionSourceError? error)
        {
            script = null;
            sha256 = null;
            error = null;

            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                error = new ToolFunctionSourceError(MissingScriptPathCode, "scriptPath가 필요합니다.", 1);
                return false;
            }

            if (Path.IsPathRooted(scriptPath))
            {
                error = new ToolFunctionSourceError(
                    AbsoluteScriptPathCode,
                    $"scriptPath는 Recipe 파일 위치 기준 상대 경로여야 합니다(절대 경로는 다른 환경으로 옮길 수 없음): '{scriptPath}'. script를 Recipe 옆에 두고 상대 경로로 지정하세요.",
                    2);
                return false;
            }

            string resolvedPath;
            byte[] bytes;
            try
            {
                // 절대 경로는 위에서 거부했으므로 Join은 Recipe 디렉터리를 버리지 않는다.
                var recipeDirectory = Path.GetDirectoryName(Path.GetFullPath(recipeFilePath))!;
                resolvedPath = Path.GetFullPath(Path.Join(recipeDirectory, scriptPath));
            }
            catch (ArgumentException ex)
            {
                // NUL 같은 파일 시스템에서 쓸 수 없는 문자가 든 경로.
                error = new ToolFunctionSourceError(
                    ScriptReadFailedCode,
                    $"script 경로를 해석할 수 없습니다: '{scriptPath}' ({ex.Message})",
                    2);
                return false;
            }

            try
            {
                bytes = File.ReadAllBytes(resolvedPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                // 읽기 권한 없음과 디렉터리 경로가 여기로 온다(IOException 하위 타입이 아님).
                error = new ToolFunctionSourceError(
                    ScriptReadFailedCode,
                    $"script 파일을 읽을 수 없습니다: '{scriptPath}' → {resolvedPath} ({ex.Message})",
                    2);
                return false;
            }
            catch (IOException ex)
            {
                error = new ToolFunctionSourceError(
                    ScriptReadFailedCode,
                    $"script 파일을 읽을 수 없습니다: '{scriptPath}' → {resolvedPath} ({ex.Message})",
                    2);
                return false;
            }

            try
            {
                script = _strictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                error = new ToolFunctionSourceError(
                    ScriptNotUtf8Code,
                    $"script 파일이 UTF-8 텍스트가 아닙니다: '{scriptPath}' → {resolvedPath}",
                    2);
                return false;
            }

            if (string.IsNullOrWhiteSpace(script))
            {
                script = null;
                error = new ToolFunctionSourceError(
                    BlankScriptCode,
                    $"script 내용이 비어 있거나 공백/개행뿐입니다: '{scriptPath}'",
                    1);
                return false;
            }

            sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            return true;
        }
    }
}
