using System;
using System.IO;
using System.Threading;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// Interactive clue-based entry flow for 쉬운 안내 모드 (GuidedBeginner).
    /// Implements Sections 8.2–14 of NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md:
    /// 7-choice clue picker, per-clue sub-flows (install command, container
    /// image, source/GitHub, Dockerfile, internal mirror, tool-name-only),
    /// and the "아무것도 모름" safe-exit path.
    /// Returns the selected RecipeMethodId after pre-populating session fields,
    /// or null when the user exits without saving.
    /// </summary>
    internal static class BeginnerGuideFlow
    {
        public static RecipeMethodId? Run(
            RecipeAuthoringSession session,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation)
        {
            return Run(session, stdin, stdout, cancellation, NullImageDigestResolver.Instance);
        }

        public static RecipeMethodId? Run(
            RecipeAuthoringSession session,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation,
            IImageDigestResolver digestResolver)
        {
            ArgumentNullException.ThrowIfNull(digestResolver);
            return PromptCluePicker(session, stdin, stdout, cancellation, digestResolver);
        }

        private static RecipeMethodId? PromptCluePicker(
            RecipeAuthoringSession session,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation,
            IImageDigestResolver digestResolver)
        {
            stdout.WriteLine("쉬운 안내 모드");
            stdout.WriteLine();
            stdout.WriteLine("정확히 몰라도 괜찮습니다.");
            stdout.WriteLine("알고 있는 것만 선택하세요.");
            stdout.WriteLine();
            stdout.WriteLine("무엇을 알고 있나요?");
            stdout.WriteLine();
            stdout.WriteLine("[1] 도구 이름만 알고 있다");
            stdout.WriteLine("    예: bwa, samtools, fastqc");
            stdout.WriteLine();
            stdout.WriteLine("[2] 설치 명령을 알고 있다");
            stdout.WriteLine("    예: conda install -c bioconda bwa=0.7.17");
            stdout.WriteLine("        micromamba install -c bioconda samtools=1.20");
            stdout.WriteLine();
            stdout.WriteLine("[3] 컨테이너 이미지 주소를 알고 있다");
            stdout.WriteLine("    예: quay.io/biocontainers/bwa:0.7.17--h7132678_9");
            stdout.WriteLine("        ghcr.io/example/tool:1.0.0@sha256:...");
            stdout.WriteLine();
            stdout.WriteLine("[4] GitHub 또는 소스코드 주소를 알고 있다");
            stdout.WriteLine("    예: https://github.com/lh3/bwa");
            stdout.WriteLine("        https://example.org/tool-1.0.0.tar.gz");
            stdout.WriteLine();
            stdout.WriteLine("[5] Dockerfile을 가지고 있다");
            stdout.WriteLine("    예: ./Dockerfile");
            stdout.WriteLine();
            stdout.WriteLine("[6] 회사/학교 내부 저장소를 써야 한다");
            stdout.WriteLine("    예: https://mirror.company.local/conda");
            stdout.WriteLine();
            stdout.WriteLine("[7] 잘 모르겠다");
            stdout.WriteLine();
            stdout.WriteLine("이전 화면으로 돌아가려면 /back, 저장하지 않고 종료하려면 /cancel을 입력하세요.");
            stdout.WriteLine();
            stdout.WriteLine("선택:");

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var line = ReadTrimmedLine(stdin);
                switch (line)
                {
                    case "1":
                        return RunToolNameFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "2":
                        return RunInstallCommandFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "3":
                        return RunContainerImageFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "4":
                        return RunSourceFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "5":
                        return RunDockerfileFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "6":
                        return RunMirrorFlow(session, stdin, stdout, cancellation);
                    case "7":
                        return RunNoClueFlow(session, stdin, stdout, cancellation, digestResolver);
                    default:
                        stdout.WriteLine("1–7 중에서 선택해 주세요.");
                        stdout.WriteLine("선택:");
                        break;
                }
            }
        }

        // ── Section 14: 도구 이름만 알고 있는 경우 ─────────────────────────────
        private static RecipeMethodId? RunToolNameFlow(
            RecipeAuthoringSession session,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation,
            IImageDigestResolver digestResolver)
        {
            string name;
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                stdout.WriteLine();
                stdout.WriteLine("도구 이름을 입력해 주세요.");
                stdout.WriteLine();
                stdout.WriteLine("예:");
                stdout.WriteLine("  bwa");
                stdout.WriteLine("  samtools");
                stdout.WriteLine("  fastqc");
                stdout.WriteLine();
                stdout.WriteLine("도구 이름:");

                name = ReadTrimmedLine(stdin);
                if (name.Length > 0)
                {
                    break;
                }

                stdout.WriteLine("도구 이름을 입력해 주세요.");
            }

            PrintToolLookupGuidance(stdout, name);

            stdout.WriteLine();
            stdout.WriteLine($"'{name}' 도구를 설치하거나 실행하는 예시를 본 적 있나요?");
            stdout.WriteLine();
            stdout.WriteLine("[1] conda install 또는 micromamba install 예시를 봤다");
            stdout.WriteLine("[2] docker run 또는 컨테이너 이미지 주소를 봤다");
            stdout.WriteLine("[3] GitHub 또는 source archive 주소를 봤다");
            stdout.WriteLine("[4] Dockerfile을 받았다");
            stdout.WriteLine("[5] 회사/학교 내부 저장소에서 설치해야 한다");
            stdout.WriteLine("[6] 아무것도 모른다");
            stdout.WriteLine();
            stdout.WriteLine("선택:");

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var line = ReadTrimmedLine(stdin);
                switch (line)
                {
                    case "1":
                        return RunInstallCommandFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "2":
                        return RunContainerImageFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "3":
                        return RunSourceFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "4":
                        return RunDockerfileFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "5":
                        return RunMirrorFlow(session, stdin, stdout, cancellation);
                    case "6":
                        return RunNoClueFlow(session, stdin, stdout, cancellation, digestResolver);
                    default:
                        stdout.WriteLine("1–6 중에서 선택해 주세요.");
                        stdout.WriteLine("선택:");
                        break;
                }
            }
        }

        // ── Section 9: 설치 명령 기반 흐름 ─────────────────────────────────────
        private static RecipeMethodId? RunInstallCommandFlow(
            RecipeAuthoringSession session,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation,
            IImageDigestResolver digestResolver)
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                stdout.WriteLine();
                stdout.WriteLine("설치 명령을 입력해 주세요.");
                stdout.WriteLine();
                stdout.WriteLine("예:");
                stdout.WriteLine("  conda install -c bioconda bwa=0.7.17");
                stdout.WriteLine("  micromamba install -c bioconda samtools=1.20");
                stdout.WriteLine();
                stdout.WriteLine("설치 명령:");

                var command = ReadRawLine(stdin);
                var parsed = InstallCommandParser.Parse(command);

                switch (parsed.Status)
                {
                    case InstallCommandParseStatus.Parsed:
                    case InstallCommandParseStatus.PartiallyParsed:
                        var choice = PromptPartialParseChoice(parsed, stdin, stdout, cancellation);
                        if (choice == InstallParseChoice.UseValues)
                        {
                            return PrePopulatePackageMethod(session, parsed);
                        }
                        else if (choice == InstallParseChoice.Reenter)
                        {
                            continue;
                        }
                        else if (choice == InstallParseChoice.SwitchMethod)
                        {
                            return PromptCluePicker(session, stdin, stdout, cancellation, digestResolver);
                        }
                        else
                        {
                            throw new RecipeCreateCancelledException();
                        }

                    case InstallCommandParseStatus.Failed:
                        var failChoice = PromptFailedParseChoice(parsed, stdin, stdout, cancellation);
                        if (failChoice == InstallParseChoice.UseValues)
                        {
                            session.SelectMethod(RecipeMethodId.Package);
                            return RecipeMethodId.Package;
                        }
                        else if (failChoice == InstallParseChoice.Reenter)
                        {
                            continue;
                        }
                        else if (failChoice == InstallParseChoice.SwitchMethod)
                        {
                            return PromptCluePicker(session, stdin, stdout, cancellation, digestResolver);
                        }
                        else
                        {
                            throw new RecipeCreateCancelledException();
                        }

                    default:
                        throw new InvalidOperationException($"Unexpected parse status: {parsed.Status}");
                }
            }
        }

        private enum InstallParseChoice { UseValues, Reenter, SwitchMethod, Cancel }

        private static InstallParseChoice PromptPartialParseChoice(
            InstallCommandParseResult parsed,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation)
        {
            stdout.WriteLine();
            if (parsed.Status == InstallCommandParseStatus.Parsed)
            {
                stdout.WriteLine("설치 명령을 이해했습니다.");
            }
            else
            {
                stdout.WriteLine("설치 명령을 일부 이해했습니다.");
            }

            stdout.WriteLine();
            stdout.WriteLine("이해한 값:");
            if (parsed.PackageEngine != null)
            {
                stdout.WriteLine($"  PackageEngine: {parsed.PackageEngine}");
            }

            if (parsed.Channels.Count > 0)
            {
                stdout.WriteLine("  Channels:");
                foreach (var ch in parsed.Channels)
                {
                    stdout.WriteLine($"    - {ch}");
                }
            }

            if (parsed.Packages.Count > 0)
            {
                stdout.WriteLine("  Packages:");
                foreach (var pkg in parsed.Packages)
                {
                    stdout.WriteLine($"    - {pkg}");
                }
            }

            if (parsed.Missing.Count > 0)
            {
                stdout.WriteLine();
                stdout.WriteLine("추가로 필요한 값:");
                foreach (var m in parsed.Missing)
                {
                    stdout.WriteLine($"  {m}");
                }
            }

            if (parsed.Warnings.Count > 0)
            {
                stdout.WriteLine();
                stdout.WriteLine("주의:");
                foreach (var w in parsed.Warnings)
                {
                    stdout.WriteLine($"  {w}");
                }
            }

            stdout.WriteLine();
            stdout.WriteLine("선택:");
            stdout.WriteLine("[1] 이해한 값을 사용하고 부족한 값을 직접 입력한다");
            stdout.WriteLine("[2] 설치 명령을 다시 입력한다");
            stdout.WriteLine("[3] 다른 작성 방식을 선택한다");
            stdout.WriteLine("[4] 취소한다");
            stdout.WriteLine();
            stdout.WriteLine("선택:");

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var line = ReadTrimmedLine(stdin);
                switch (line)
                {
                    case "1": return InstallParseChoice.UseValues;
                    case "2": return InstallParseChoice.Reenter;
                    case "3": return InstallParseChoice.SwitchMethod;
                    case "4": return InstallParseChoice.Cancel;
                    default:
                        stdout.WriteLine("1–4 중에서 선택해 주세요.");
                        stdout.WriteLine("선택:");
                        break;
                }
            }
        }

        private static InstallParseChoice PromptFailedParseChoice(
            InstallCommandParseResult parsed,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation)
        {
            stdout.WriteLine();
            stdout.WriteLine("설치 명령을 자동으로 이해하지 못했습니다.");
            stdout.WriteLine();
            stdout.WriteLine("괜찮습니다. 필요한 값을 하나씩 입력하면 됩니다.");

            if (parsed.Warnings.Count > 0)
            {
                stdout.WriteLine();
                stdout.WriteLine("이유:");
                foreach (var w in parsed.Warnings)
                {
                    stdout.WriteLine($"  {w}");
                }
            }

            stdout.WriteLine();
            stdout.WriteLine("이 방식으로 계속하면:");
            stdout.WriteLine("  - package 방식 recipe를 만듭니다.");
            stdout.WriteLine("  - PackageEngine, Channels, Packages를 직접 입력합니다.");
            stdout.WriteLine();
            stdout.WriteLine("선택:");
            stdout.WriteLine("[1] package 방식으로 계속한다");
            stdout.WriteLine("[2] 설치 명령을 다시 입력한다");
            stdout.WriteLine("[3] 다른 작성 방식을 선택한다");
            stdout.WriteLine("[4] 취소한다");
            stdout.WriteLine();
            stdout.WriteLine("선택:");

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var line = ReadTrimmedLine(stdin);
                switch (line)
                {
                    case "1": return InstallParseChoice.UseValues;
                    case "2": return InstallParseChoice.Reenter;
                    case "3": return InstallParseChoice.SwitchMethod;
                    case "4": return InstallParseChoice.Cancel;
                    default:
                        stdout.WriteLine("1–4 중에서 선택해 주세요.");
                        stdout.WriteLine("선택:");
                        break;
                }
            }
        }

        private static RecipeMethodId PrePopulatePackageMethod(RecipeAuthoringSession session, InstallCommandParseResult parsed)
        {
            session.SelectMethod(RecipeMethodId.Package);

            if (parsed.PackageEngine != null)
            {
                session.SetField("PackageEngine", parsed.PackageEngine);
            }

            if (parsed.Channels.Count > 0)
            {
                foreach (var ch in parsed.Channels)
                {
                    session.AppendListItem("Channels", ch);
                }

                session.CompleteListField("Channels");
            }

            if (parsed.Packages.Count > 0)
            {
                foreach (var pkg in parsed.Packages)
                {
                    session.AppendListItem("Packages", pkg);
                }

                session.CompleteListField("Packages");
            }

            return RecipeMethodId.Package;
        }

        // ── Section 10: 컨테이너 이미지 기반 흐름 ──────────────────────────────
        private static RecipeMethodId? RunContainerImageFlow(
            RecipeAuthoringSession session,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation,
            IImageDigestResolver digestResolver)
        {
            string? pendingRef = null;

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                if (pendingRef is null)
                {
                    stdout.WriteLine();
                    stdout.WriteLine("컨테이너 이미지 주소를 입력해 주세요.");
                    stdout.WriteLine();
                    stdout.WriteLine("예:");
                    stdout.WriteLine("  quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:...");
                    stdout.WriteLine("  ghcr.io/example/tool:1.0.0@sha256:...");
                    stdout.WriteLine();
                    stdout.WriteLine("이 값을 사용하면:");
                    stdout.WriteLine("  - container 방식 recipe를 만듭니다.");
                    stdout.WriteLine("  - 이미 만들어진 이미지를 그대로 사용합니다.");
                    stdout.WriteLine("  - digest가 없으면 재현성을 보장할 수 없어 validate에서 실패합니다.");
                    stdout.WriteLine();
                    stdout.WriteLine("이미지 주소:");

                    pendingRef = ReadTrimmedLine(stdin);
                }

                var result = ImageReferenceNormalizer.Normalize(pendingRef, null);

                if (result.Status == ImageReferenceNormalizeStatus.Normalized)
                {
                    session.SelectMethod(RecipeMethodId.Container);
                    session.SetField("ImageRef", result.RepositoryAndTag);
                    session.SetField("ImageDigest", result.Digest!);
                    return RecipeMethodId.Container;
                }

                if (result.Status == ImageReferenceNormalizeStatus.MissingDigest)
                {
                    var resolvedDigest = TryResolveImageDigest(pendingRef, digestResolver, stdout, cancellation);
                    if (resolvedDigest != null)
                    {
                        stdout.WriteLine();
                        stdout.WriteLine("이미지 digest를 확인했습니다.");
                        stdout.WriteLine();
                        stdout.WriteLine($"  {resolvedDigest}");
                        stdout.WriteLine();
                        stdout.WriteLine("이 digest를 사용할까요? [Y/n]");
                        var confirm = ReadTrimmedLine(stdin).ToLowerInvariant();
                        if (confirm.Length == 0 || confirm == "y")
                        {
                            var resolved = ImageReferenceNormalizer.Normalize(pendingRef, resolvedDigest);
                            if (resolved.Status == ImageReferenceNormalizeStatus.Normalized)
                            {
                                session.SelectMethod(RecipeMethodId.Container);
                                session.SetField("ImageRef", resolved.RepositoryAndTag);
                                session.SetField("ImageDigest", resolved.Digest!);
                                return RecipeMethodId.Container;
                            }
                        }

                        stdout.WriteLine("직접 digest를 입력합니다.");
                    }

                    stdout.WriteLine();
                    stdout.WriteLine("입력한 이미지 주소에는 digest가 없습니다.");
                    stdout.WriteLine();
                    stdout.WriteLine("현재 값:");
                    stdout.WriteLine($"  {pendingRef}");
                    stdout.WriteLine();
                    stdout.WriteLine("NodeKit은 재현성을 위해 digest 고정을 요구합니다.");
                    stdout.WriteLine("tag는 나중에 같은 이름으로 다른 이미지가 될 수 있습니다.");
                    stdout.WriteLine();
                    stdout.WriteLine("선택:");
                    stdout.WriteLine("[1] digest가 포함된 이미지 주소를 다시 입력한다");
                    stdout.WriteLine("[2] ImageDigest를 따로 입력한다");
                    stdout.WriteLine("[3] 다른 작성 방식으로 바꾼다");
                    stdout.WriteLine("[4] 취소한다");
                    stdout.WriteLine();
                    stdout.WriteLine("선택:");

                    var choice = ReadContainerMissingDigestChoice(pendingRef, stdin, stdout, cancellation);
                    if (choice == ContainerChoice.Reenter)
                    {
                        pendingRef = null;
                        continue;
                    }
                    else if (choice == ContainerChoice.SwitchMethod)
                    {
                        return PromptCluePicker(session, stdin, stdout, cancellation, digestResolver);
                    }
                    else if (choice == ContainerChoice.Cancel)
                    {
                        throw new RecipeCreateCancelledException();
                    }
                    else
                    {
                        // SeparateDigest — user provided digest separately
                        // result will be re-evaluated in next loop iteration with the digest set
                        stdout.WriteLine();
                        stdout.WriteLine("ImageDigest:");
                        var separateDigest = ReadTrimmedLine(stdin);
                        var combined = ImageReferenceNormalizer.Normalize(pendingRef, separateDigest);
                        if (combined.Status == ImageReferenceNormalizeStatus.Normalized)
                        {
                            session.SelectMethod(RecipeMethodId.Container);
                            session.SetField("ImageRef", combined.RepositoryAndTag);
                            session.SetField("ImageDigest", combined.Digest!);
                            return RecipeMethodId.Container;
                        }

                        // If still MissingDigest, fall through to next loop
                        pendingRef = combined.RepositoryAndTag;
                        continue;
                    }
                }
            }
        }

        private enum ContainerChoice { SeparateDigest, Reenter, SwitchMethod, Cancel }

        private static ContainerChoice ReadContainerMissingDigestChoice(
            string pendingRef,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation)
        {
            _ = pendingRef;
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var line = ReadTrimmedLine(stdin);
                switch (line)
                {
                    case "1": return ContainerChoice.Reenter;
                    case "2": return ContainerChoice.SeparateDigest;
                    case "3": return ContainerChoice.SwitchMethod;
                    case "4": return ContainerChoice.Cancel;
                    default:
                        stdout.WriteLine("1–4 중에서 선택해 주세요.");
                        stdout.WriteLine("선택:");
                        break;
                }
            }
        }

        // ── Section 11: GitHub/소스코드 주소 기반 흐름 ──────────────────────────
        private static RecipeMethodId? RunSourceFlow(
            RecipeAuthoringSession session,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation,
            IImageDigestResolver digestResolver)
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                stdout.WriteLine();
                stdout.WriteLine("소스코드 주소를 입력해 주세요.");
                stdout.WriteLine();
                stdout.WriteLine("예:");
                stdout.WriteLine("  https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz");
                stdout.WriteLine("  https://example.org/tool-1.0.0.tar.gz");
                stdout.WriteLine();
                stdout.WriteLine("이 값을 사용하면:");
                stdout.WriteLine("  - source 방식 recipe를 만듭니다.");
                stdout.WriteLine("  - 이후 SourceUri, SourceChecksum, SourceBuildCommands를 입력하게 됩니다.");
                stdout.WriteLine("  - checksum이 없으면 같은 소스인지 확인할 수 없어 validate에서 실패합니다.");
                stdout.WriteLine();
                stdout.WriteLine("소스코드 주소:");

                var uri = ReadTrimmedLine(stdin);
                if (string.IsNullOrEmpty(uri))
                {
                    stdout.WriteLine("주소를 입력해 주세요.");
                    continue;
                }

                stdout.WriteLine();
                PrintSourceChecksumGuidance(stdout, uri);
                stdout.WriteLine("SourceChecksum:");

                var checksum = ReadTrimmedLine(stdin);
                if (!string.IsNullOrEmpty(checksum))
                {
                    session.SelectMethod(RecipeMethodId.Source);
                    session.SetField("SourceUri", uri);
                    session.SetField("SourceChecksum", checksum);
                    return RecipeMethodId.Source;
                }

                stdout.WriteLine();
                stdout.WriteLine("SourceChecksum이 없으면 source 방식 recipe를 완성할 수 없습니다.");
                stdout.WriteLine();
                stdout.WriteLine("선택:");
                stdout.WriteLine("[1] 계산 방법을 본다");
                stdout.WriteLine("[2] 직접 입력한다");
                stdout.WriteLine("[3] 다른 작성 방식으로 바꾼다");
                stdout.WriteLine("[4] 저장하지 않고 종료한다");
                stdout.WriteLine();
                stdout.WriteLine("선택:");

                while (true)
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        throw new RecipeCreateCancelledException();
                    }

                    var line = ReadTrimmedLine(stdin);
                    switch (line)
                    {
                        case "1":
                            PrintSourceChecksumGuidance(stdout, uri);
                            continue;
                        case "2":
                            stdout.WriteLine("SourceChecksum:");
                            var newChecksum = ReadTrimmedLine(stdin);
                            if (!string.IsNullOrEmpty(newChecksum))
                            {
                                session.SelectMethod(RecipeMethodId.Source);
                                session.SetField("SourceUri", uri);
                                session.SetField("SourceChecksum", newChecksum);
                                return RecipeMethodId.Source;
                            }
                            stdout.WriteLine("checksum이 비어 있습니다. 다시 시도합니다.");
                            continue;
                        case "3":
                            return PromptCluePicker(session, stdin, stdout, cancellation, digestResolver);
                        case "4":
                            return null;
                        default:
                            stdout.WriteLine("1–4 중에서 선택해 주세요.");
                            stdout.WriteLine("선택:");
                            continue;
                    }
                }

            }
        }

        // ── Section 12: Dockerfile 기반 흐름 ────────────────────────────────────
        private static RecipeMethodId? RunDockerfileFlow(
            RecipeAuthoringSession session,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation,
            IImageDigestResolver digestResolver)
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                stdout.WriteLine();
                stdout.WriteLine("Dockerfile 경로를 입력해 주세요.");
                stdout.WriteLine();
                stdout.WriteLine("예:");
                stdout.WriteLine("  ./Dockerfile");
                stdout.WriteLine();
                stdout.WriteLine("주의:");
                stdout.WriteLine("  Dockerfile 방식은 가장 자유롭지만 NodeKit이 자동으로 보장해주는 부분이 가장 적습니다.");
                stdout.WriteLine("  FROM 이미지가 digest로 고정되어 있지 않거나 latest 태그를 사용하면 validate에서 실패합니다.");
                stdout.WriteLine("  처음 사용하는 경우에는 package 또는 container 방식이 더 쉽습니다.");
                stdout.WriteLine();
                stdout.WriteLine("Dockerfile 경로:");

                var path = ReadTrimmedLine(stdin);
                if (string.IsNullOrEmpty(path))
                {
                    stdout.WriteLine("경로를 입력해 주세요.");
                    continue;
                }

                stdout.WriteLine();
                stdout.WriteLine("Dockerfile fallback 방식을 선택했습니다.");
                stdout.WriteLine();
                stdout.WriteLine("이 방식은 다음 책임이 사용자에게 있습니다.");
                stdout.WriteLine("  - Dockerfile의 모든 FROM 이미지 digest 고정");
                stdout.WriteLine("  - latest 태그 사용 금지");
                stdout.WriteLine("  - 외부 다운로드 URL의 재현성 관리");
                stdout.WriteLine("  - Dockerfile의 첫 FROM과 BaseImage 일치");
                stdout.WriteLine();
                stdout.WriteLine("처음 사용하는 경우에는 package 또는 container 방식을 먼저 고려하는 것을 권장합니다.");
                stdout.WriteLine();
                stdout.WriteLine("계속 진행할까요? [y/N]");

                var confirm = ReadTrimmedLine(stdin).ToLowerInvariant();
                if (confirm != "y")
                {
                    return PromptCluePicker(session, stdin, stdout, cancellation, digestResolver);
                }

                session.SelectMethod(RecipeMethodId.Dockerfile);
                session.AcceptDockerfileWarning();
                session.SetField("DockerfilePath", path);
                return RecipeMethodId.Dockerfile;
            }
        }

        // ── Section 13: 내부 저장소 기반 흐름 ───────────────────────────────────
        private static RecipeMethodId? RunMirrorFlow(
            RecipeAuthoringSession session,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation)
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                stdout.WriteLine();
                stdout.WriteLine("내부 저장소 주소를 입력해 주세요.");
                stdout.WriteLine();
                stdout.WriteLine("예:");
                stdout.WriteLine("  https://mirror.company.local/conda");
                stdout.WriteLine("  https://packages.school.local/conda");
                stdout.WriteLine();
                stdout.WriteLine("이 값을 사용하면:");
                stdout.WriteLine("  - mirror 방식 recipe를 만듭니다.");
                stdout.WriteLine("  - 이후 PackageMirrorUri를 입력하게 됩니다.");
                stdout.WriteLine("  - 다른 사용자가 같은 recipe를 실행하려면 동일한 내부 저장소에 접근할 수 있어야 합니다.");
                stdout.WriteLine();
                stdout.WriteLine("내부 저장소 주소:");

                var uri = ReadTrimmedLine(stdin);
                if (string.IsNullOrEmpty(uri))
                {
                    stdout.WriteLine("주소를 입력해 주세요.");
                    continue;
                }

                session.SelectMethod(RecipeMethodId.Mirror);
                session.SetField("MirrorUri", uri);
                return RecipeMethodId.Mirror;
            }
        }

        // ── Section 14 "아무것도 모름": safe-exit path ──────────────────────────
        private static RecipeMethodId? RunNoClueFlow(
            RecipeAuthoringSession session,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation,
            IImageDigestResolver digestResolver)
        {
            stdout.WriteLine();
            stdout.WriteLine("아직 recipe를 완성하기 위한 단서가 부족합니다.");
            stdout.WriteLine();
            stdout.WriteLine("현재 NodeKit CLI는 외부 검색이나 NodeVault 조회를 하지 않습니다.");
            stdout.WriteLine("따라서 recipe 생성을 완료하려면 최소한 다음 중 하나가 필요합니다.");
            stdout.WriteLine();
            stdout.WriteLine("  - conda/micromamba 설치 명령");
            stdout.WriteLine("  - 컨테이너 이미지 주소");
            stdout.WriteLine("  - 소스코드 주소와 checksum");
            stdout.WriteLine("  - Dockerfile");
            stdout.WriteLine("  - 내부 package mirror 주소");
            stdout.WriteLine();
            stdout.WriteLine("선택:");
            stdout.WriteLine("[1] 도구 이름으로 bioconda/BioContainers 확인 방법을 본다");
            stdout.WriteLine("[2] 설치 명령을 입력한다");
            stdout.WriteLine("[3] 컨테이너 이미지 주소를 입력한다");
            stdout.WriteLine("[4] 소스코드 주소를 입력한다");
            stdout.WriteLine("[5] Dockerfile 경로를 입력한다");
            stdout.WriteLine("[6] 저장하지 않고 종료한다");
            stdout.WriteLine();
            stdout.WriteLine("선택:");

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var line = ReadTrimmedLine(stdin);
                switch (line)
                {
                    case "1":
                        return RunToolNameFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "2":
                        return RunInstallCommandFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "3":
                        return RunContainerImageFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "4":
                        return RunSourceFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "5":
                        return RunDockerfileFlow(session, stdin, stdout, cancellation, digestResolver);
                    case "6":
                        return null;
                    default:
                        stdout.WriteLine("1–6 중에서 선택해 주세요.");
                        stdout.WriteLine("선택:");
                        break;
                }
            }
        }

        private static void PrintToolLookupGuidance(TextWriter output, string toolName)
        {
            output.WriteLine();
            output.WriteLine("다음 위치에서 도구를 확인해보세요.");
            output.WriteLine();
            output.WriteLine("  bioconda 패키지:");
            output.WriteLine($"    {BuildBiocondaUrl(toolName)}");
            output.WriteLine();
            output.WriteLine("  BioContainers 이미지:");
            output.WriteLine($"    {BuildBioContainersUrl(toolName)}");
            output.WriteLine();
            output.WriteLine("bioconda 페이지에서 conda install 명령어를 찾았다면 package 방식으로 진행할 수 있습니다.");
            output.WriteLine("BioContainers 페이지에서 이미지 주소를 찾았다면 container 방식으로 진행할 수 있습니다.");
        }

        private static string BuildBiocondaUrl(string toolName) =>
            "https://anaconda.org/bioconda/" + Uri.EscapeDataString(toolName.Trim());

        private static string BuildBioContainersUrl(string toolName) =>
            "https://quay.io/repository/biocontainers/" + Uri.EscapeDataString(toolName.Trim()) + "?tab=tags";

        private static void PrintSourceChecksumGuidance(TextWriter output, string sourceUri)
        {
            output.WriteLine("소스 코드 검증값이 필요합니다.");
            output.WriteLine();
            output.WriteLine("NodeKit은 같은 소스 코드로 다시 빌드할 수 있도록 sha256 checksum을 요구합니다.");
            output.WriteLine();
            output.WriteLine("소스 archive URL이 있다면 다음 명령으로 계산할 수 있습니다.");
            output.WriteLine();
            output.WriteLine($"  curl -fsSL \"{sourceUri}\" | sha256sum");
            output.WriteLine();
            output.WriteLine("출력 예:");
            output.WriteLine("  3f2a1b9c...  -");
            output.WriteLine();
            output.WriteLine("앞의 64자리 hex 값에 sha256: prefix를 붙여 입력하세요.");
            output.WriteLine("예:");
            output.WriteLine("  sha256:3f2a1b9c...");
            output.WriteLine();
        }

        private static string? TryResolveImageDigest(
            string imageUri,
            IImageDigestResolver digestResolver,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation)
        {
            if (cancellation.IsCancellationRequested)
            {
                throw new RecipeCreateCancelledException();
            }

            var result = digestResolver.ResolveAsync(imageUri, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Status == ImageDigestResolutionStatus.Resolved && !string.IsNullOrWhiteSpace(result.Digest))
            {
                return result.Digest;
            }

            stdout.WriteLine();
            stdout.WriteLine(DescribeDigestResolutionFailure(result));
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                stdout.WriteLine(result.Message);
            }

            stdout.WriteLine("이미지 registry에서 digest를 복사해 입력하세요.");
            return null;
        }

        private static string DescribeDigestResolutionFailure(ImageDigestResolutionResult result) => result.Status switch
        {
            ImageDigestResolutionStatus.NotFound => "이미지를 찾을 수 없습니다. 이미지 이름과 tag를 확인하세요.",
            ImageDigestResolutionStatus.AuthenticationRequired => "registry 인증이 필요합니다. 현재 CLI는 인증 조회를 지원하지 않습니다.",
            ImageDigestResolutionStatus.NetworkUnavailable => "네트워크 연결을 확인할 수 없습니다. 수동으로 digest를 입력하세요.",
            ImageDigestResolutionStatus.InvalidReference => "이미지 주소 형식이 올바르지 않습니다.",
            ImageDigestResolutionStatus.Unsupported => "현재 환경에서는 자동 조회를 사용할 수 없습니다.",
            ImageDigestResolutionStatus.Resolved => "이미지 digest를 확인했습니다.",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unsupported digest resolution status."),
        };

        private static string ReadTrimmedLine(TextReader stdin)
        {
            var line = (stdin.ReadLine() ?? string.Empty).Trim();
            RecipeCreateEscapeCommands.ThrowIfEscape(line);
            return line;
        }

        private static string ReadRawLine(TextReader stdin)
        {
            var line = stdin.ReadLine() ?? string.Empty;
            RecipeCreateEscapeCommands.ThrowIfEscape(line);
            return line;
        }
    }
}
