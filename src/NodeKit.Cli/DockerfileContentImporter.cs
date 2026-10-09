using System;
using System.IO;
using System.Text;

namespace NodeKit.Cli
{
    /// <summary>
    /// recipe create의 DockerfilePath 입력을 읽어 DockerfileContent로 동결한다(S1-05).
    /// 저장된 Recipe는 이 Content만으로 render/submit되므로, 이후 원본 파일이
    /// 바뀌거나 없어져도 결과가 같다. 실패는 모두 입력 오류(exit 2)다.
    ///
    /// 파일은 UTF-8이어야 한다. 앞의 UTF-8 BOM 하나는 Dockerfile 내용이 아니므로
    /// 떼어내고, 나머지 bytes는 줄바꿈까지 그대로 보존한다.
    /// </summary>
    internal static class DockerfileContentImporter
    {
        public const string ReadFailedCode = "DOCKERFILE_READ_FAILED";
        public const string InputConflictCode = "DOCKERFILE_INPUT_CONFLICT";
        public const string BuildContextUnsupportedCode = "BUILD_CONTEXT_UNSUPPORTED";

        public const string DefaultBuildContext = ".";

        private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private static readonly byte[] _utf8Bom = { 0xEF, 0xBB, 0xBF };

        public static string InputConflictMessage =>
            $"[{InputConflictCode}] DockerfilePath와 DockerfileContent를 함께 입력할 수 없습니다. 기존 Dockerfile 파일을 쓰려면 DockerfilePath만, 내용을 직접 넣으려면 DockerfileContent만 입력하세요.";

        public static string BuildContextUnsupportedMessage(string value) =>
            $"[{BuildContextUnsupportedCode}] BuildContext '{value}'는 지원하지 않습니다. NodeKit은 로컬 build context 파일을 빌드 서버로 전송하지 않으므로 기본값 '{DefaultBuildContext}'만 허용합니다.";

        public static bool TryRead(string path, out string content, out string error)
        {
            content = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = $"[{ReadFailedCode}] Dockerfile 경로가 비어 있습니다.";
                return false;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (ArgumentException)
            {
                // NUL 등 경로로 쓸 수 없는 문자. 처리하지 않으면 CLI가 unhandled
                // exception으로 끝난다.
                error = $"[{ReadFailedCode}] Dockerfile 경로가 올바르지 않습니다: {path}";
                return false;
            }
            catch (FileNotFoundException)
            {
                error = $"[{ReadFailedCode}] Dockerfile을 찾을 수 없습니다: {path}";
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                error = $"[{ReadFailedCode}] Dockerfile을 찾을 수 없습니다: {path}";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                // 읽기 권한 없음과 디렉터리 경로가 여기로 온다(IOException 하위 타입이 아님).
                error = $"[{ReadFailedCode}] Dockerfile을 읽을 권한이 없거나 파일이 아닙니다: {path} ({ex.Message})";
                return false;
            }
            catch (IOException ex)
            {
                error = $"[{ReadFailedCode}] Dockerfile을 읽을 수 없습니다: {path} ({ex.Message})";
                return false;
            }

            var offset = bytes.AsSpan().StartsWith(_utf8Bom) ? _utf8Bom.Length : 0;
            try
            {
                content = _strictUtf8.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException)
            {
                error = $"[{ReadFailedCode}] Dockerfile이 UTF-8 텍스트가 아닙니다: {path}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                content = string.Empty;
                error = $"[{ReadFailedCode}] Dockerfile이 비어 있습니다: {path}";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
