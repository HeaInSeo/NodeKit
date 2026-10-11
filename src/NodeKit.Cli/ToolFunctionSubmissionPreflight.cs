using System;
using System.IO;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Grpc;
using NodeKit.Validation;
using NodeKit.Validation.ToolFunctionRecipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// current preflight를 통과한 fresh 요청 basis. Source는 이번에 읽어 고정한
    /// base image A와 script, RawSpecJson은 그 값으로 만든 실제 전송용
    /// nodevault.build.raw_spec.v1 payload(한 줄 JSON)다.
    /// </summary>
    internal sealed record ToolFunctionCurrentPreflight(
        ToolFunctionBaseScriptSnapshot Source,
        string RawSpecJson);

    /// <summary>
    /// P06.current: render/submit 경계에서 파일의 현재 Recipe 내용을 다시 검증한다
    /// (v0.9.1 계약 S3-09 current_preflight_ready).
    ///
    /// 저장된 State=Ready 표지만 믿지 않는다. 순서: 목록 원소 null → Ready 여부
    /// (Draft/Approved 등은 거부하고 State를 바꾸지 않는다) → 현재 내용 full L1 →
    /// base image A·ScriptPath source materialize → raw_spec v1 생성. typed 등록까지
    /// 갈 호출자(submit)는 C1 형식과 typed mapping 전제도 확인한다. render는 RPC가
    /// 없는 미리보기이므로 C1 형식 guard(S3-02-F, typed 제출 한정)는 적용하지 않는다.
    ///
    /// 승인 fingerprint/hash는 계산하지 않고, 네트워크 호출도 하지 않는다. 실패하면
    /// 진단을 stderr에 쓰고 exit code(1 = Recipe 내용, 2 = source 파일 입력 오류)를
    /// 돌려준다 — 호출자는 출력 파일을 쓰거나 RPC를 보내지 않는다. replay는 저장된
    /// envelope를 쓰며 이 경로를 다시 타지 않는다.
    /// </summary>
    internal static class ToolFunctionSubmissionPreflight
    {
        public const string NotReadyCode = "RECIPE_NOT_READY";

        public static bool TryRun(
            ToolFunctionRecipe recipe,
            string recipeFilePath,
            bool requireTypedRegistration,
            TextWriter stderr,
            out ToolFunctionCurrentPreflight? preflight,
            out int exitCode)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            ArgumentException.ThrowIfNullOrEmpty(recipeFilePath);
            ArgumentNullException.ThrowIfNull(stderr);

            preflight = null;
            exitCode = 1;

            // 목록 원소 null은 validator가 접근하기 전에 L1 위반으로 돌려준다(S4-03-C).
            var nullElements = NullCollectionElementValidator.Validate(recipe, "L1-TFR-013");
            if (!nullElements.IsValid)
            {
                CliApp.PrintViolations(nullElements.Violations, stderr);
                return false;
            }

            if (recipe.State != ToolFunctionRecipeState.Ready)
            {
                stderr.WriteLine(
                    $"{NotReadyCode}: State가 {recipe.State}입니다. 먼저 검증을 통과해 Ready 상태가 되어야 합니다 (nodekit function-recipe validate 실행). NodeKit은 승인 상태를 만들거나 대신하지 않습니다.");
                return false;
            }

            // State 전이 없이 현재 내용만 검증한다(ValidationPipeline은 성공 시 State를 쓴다).
            var current = ToolFunctionRecipeValidator.Validate(recipe);
            if (!current.IsValid)
            {
                CliApp.PrintViolations(current.Violations, stderr);
                return false;
            }

            if (!ToolFunctionSourceMaterializer.TryMaterialize(recipe, recipeFilePath, out var source, out var sourceError))
            {
                stderr.WriteLine($"{sourceError!.Code}: {sourceError.Message}");
                exitCode = sourceError.ExitCode;
                return false;
            }

            if (!ToolFunctionRawSpecFactory.TryBuild(source!.BaseImageDigest, source.Script, indented: false, out var rawSpecJson, out var wireError))
            {
                return Fail(wireError!, stderr, out exitCode);
            }

            if (requireTypedRegistration
                && (!ToolFunctionRegistrationRequestFactory.TryCheckToolSpecDigest(recipe.ToolSpecDigest, out wireError)
                    || !ToolFunctionRegistrationRequestFactory.TryCheckSpecMapping(recipe, out wireError)))
            {
                return Fail(wireError!, stderr, out exitCode);
            }

            preflight = new ToolFunctionCurrentPreflight(source, rawSpecJson!);
            exitCode = 0;
            return true;
        }

        private static bool Fail(ToolFunctionWireError error, TextWriter stderr, out int exitCode)
        {
            stderr.WriteLine($"{error.Code}: {error.Message}");
            exitCode = error.ExitCode;
            return false;
        }
    }
}
