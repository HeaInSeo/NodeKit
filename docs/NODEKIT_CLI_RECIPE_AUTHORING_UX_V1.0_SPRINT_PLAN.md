# NodeKit CLI Recipe Authoring UX v1.0 Sprint Plan

상태: Active Planning
작성일: 2026-06-26
기준 문서: `docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0.md`
범위: `nodekit recipe create` authoring UX, interactive recovery 문구, digest resolver seam
비범위: NodeVault submit/build, 실제 Harbor/OCI 네트워크 resolver, draft 저장/resume, 필드 단위 완전 `/back`, DagEdit UI 통합

---

## 0. 작업 원칙

이 스프린트는 CLI authoring UX를 개선하지만 NodeKit의 플랫폼 책임 경계는 바꾸지 않는다.

```text
RecipeDocument → RecipeValidator → RecipeRenderer → ToolDefinition → legacy BuildRequest
```

유지할 계약:

```text
digest 없이 container recipe 통과 금지
checksum 없이 source recipe 통과 금지
패키지 버전 미고정 통과 금지
validate/render의 rule ID 출력 유지
RecipeAuthoringSession.Build() 내부 Resolve() 자동 호출 금지
production ToolSpecRequest/ResolveToolSpec/SubmitToolBuild 경로 추가 금지
```

구현 방향:

```text
기존 BeginnerGuideFlow 보강
기존 BuildRecoveryPlan 보강
새 interactive recovery formatter 추가 금지
실제 registry 네트워크 조회는 v1.0 완료 조건에서 제외
```

---

## Sprint 0 — 문서와 기준선 고정

목표:

```text
v1.0 개발 계약과 실제 코드 기준선을 맞추고, 이후 변경의 보호선을 명확히 둔다.
```

작업:

1. `docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0.md`를 Development Contract 상태로 유지한다.
2. 기존 v1.0 draft에 남아 있던 submit/build, draft 저장, Harbor resolver 필수 구현, 새 formatter 제안을 제거한다.
3. 현재 구현 상태를 `BeginnerGuideFlow`, `BuildRecoveryPlan`, `CliApp`, `RecipeValidationPipeline` 기준으로 대조한다.
4. 기존 테스트 위치와 현재 field mapping key를 확인한다.

완료 조건:

```text
문서가 현재 CLI 명령과 일치한다.
v1.0 완료 조건이 실제 네트워크 resolver를 요구하지 않는다.
interactive recovery는 기존 BuildRecoveryPlan을 기준으로 한다.
validate/render rule ID 유지 정책이 문서에 남아 있다.
```

검증:

```bash
git diff -- docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0.md
rg "BuildRecoveryPlan|BeginnerHint|RunNoClueFlow|RunToolNameFlow" src tests
```

현재 상태:

```text
완료 — 2026-06-26 문서 갱신됨.
```

---

## Sprint 1 — Tool Name Lookup UX

현재 상태:

```text
완료 — RunToolNameFlow URL 안내, RunNoClueFlow lookup 경로, transcript 테스트 추가.
```

목표:

```text
도구 이름만 아는 사용자가 bioconda/BioContainers에서 설치 명령이나 이미지 tag를 찾을 수 있게 한다.
```

대상 파일:

```text
src/NodeKit.Cli/BeginnerGuideFlow.cs
tests/NodeKit.Cli.Tests/BeginnerGuideFlowTests.cs
```

작업:

1. `RunToolNameFlow`에서 도구 이름을 trim하고 빈 입력이면 다시 묻는다.
2. 도구 이름 입력 후 다음 URL을 출력한다.

```text
https://anaconda.org/bioconda/<tool>
https://quay.io/repository/biocontainers/<tool>?tab=tags
```

3. URL path에 안전하게 들어가도록 도구 이름을 escape한다.
4. `RunNoClueFlow`에 도구 이름 lookup 경로를 추가한다.
5. 기존 설치 명령, 컨테이너 이미지, 소스 URL, Dockerfile, 저장하지 않고 종료 경로를 유지한다.

권장 helper:

```csharp
private static void PrintToolLookupGuidance(TextWriter output, string toolName)
private static string BuildBiocondaUrl(string toolName)
private static string BuildBioContainersUrl(string toolName)
```

테스트:

```text
RunToolNameFlow_PrintsBiocondaAndBioContainersUrls
RunToolNameFlow_EmptyToolName_AsksAgain
RunNoClueFlow_KeepsExistingRouteOptions
RunNoClueFlow_CanRouteToToolNameLookup
RunNoClueFlow_CanContinueToInstallCommandPath
RunNoClueFlow_CanContinueToContainerImagePath
RunNoClueFlow_Cancel_DoesNotWriteRecipe
```

완료 조건:

```text
외부 API 호출 없음.
기존 no-clue 경로 회귀 없음.
도구 이름만 알아도 구체적인 lookup URL이 출력됨.
```

검증:

```bash
dotnet test --project tests/NodeKit.Cli.Tests/NodeKit.Cli.Tests.csproj --filter BeginnerGuideFlowTests
```

---

## Sprint 2 — SourceChecksum Guidance

현재 상태:

```text
완료 — source flow와 recovery hint에 curl + sha256sum 안내 추가.
```

목표:

```text
source build 사용자가 checksum이 왜 필요하고 어떻게 계산하는지 즉시 알 수 있게 한다.
```

대상 파일:

```text
src/NodeKit.Cli/BeginnerGuideFlow.cs
src/Authoring/Recipes/RecipeAuthoringSession.cs
tests/NodeKit.Cli.Tests/BeginnerGuideFlowTests.cs
tests/NodeKit.Tests/Recipes/RecipeAuthoringSessionTests.cs
```

작업:

1. source flow에서 `SourceChecksum` 입력 전에 `curl -fsSL "<SourceUri>" | sha256sum` 안내를 출력한다.
2. checksum이 비어 있을 때 선택지를 다음으로 정리한다.

```text
[1] 계산 방법을 본다
[2] 직접 입력한다
[3] 다른 작성 방식으로 바꾼다
[4] 저장하지 않고 종료한다
```

3. "나중에 추가", "draft 저장", "checksum 없이 진행" 문구를 추가하지 않는다.
4. `BuildRecoveryPlan`의 SourceChecksum action hint에도 동일한 계산 안내를 반영한다.

테스트:

```text
SourceFlow_MissingChecksum_PrintsCurlSha256sumGuidance
SourceFlow_MissingChecksum_DoesNotOfferDraftSave
BuildRecoveryPlan_ForMissingSourceChecksum_IncludesCurlSha256sumHint
```

완료 조건:

```text
source flow와 recovery hint 양쪽에서 checksum 계산 방법을 안내한다.
checksum 없는 source recipe 통과 경로는 없다.
CLI가 curl을 직접 실행하지 않는다.
```

검증:

```bash
dotnet test --project tests/NodeKit.Cli.Tests/NodeKit.Cli.Tests.csproj --filter BeginnerGuideFlowTests
dotnet test --project tests/NodeKit.Tests/NodeKit.Tests.csproj --filter RecipeAuthoringSessionTests
```

---

## Sprint 3 — RecoveryPlan 문구 개선

현재 상태:

```text
완료 — image digest, source checksum, package version 전용 recovery action 문구 추가.
```

목표:

```text
interactive final validation recovery를 내부 field명 중심에서 사용자 행동 중심으로 바꾼다.
```

대상 파일:

```text
src/Authoring/Recipes/RecipeAuthoringSession.cs
src/NodeKit.Cli/RecipeCreateInteractiveRunner.cs
tests/NodeKit.Tests/Recipes/RecipeAuthoringSessionTests.cs
tests/NodeKit.Cli.Tests/RecipeCreateInteractiveTests.cs
tests/NodeKit.Cli.Tests/CliAppTests.cs
```

작업:

1. `BuildRecoveryPlan` 구조를 유지한다.
2. `ImageRef + ImageDigest` 조합에는 이미지 digest 전용 action 문구를 사용한다.
3. `SourceChecksum`에는 checksum 계산 action 문구를 사용한다.
4. `Packages`에는 패키지 버전 고정 action 문구를 사용한다.
5. `RelatedFields`는 기존 recovery 동작을 깨지 않도록 유지한다.
6. `validate`/`render`의 `<RuleId> (<Field>): <Message>` 출력은 유지한다.

목표 문구:

```text
이미지 digest 입력하기
소스 코드 검증값 입력하기
패키지 버전 고정하기
```

테스트:

```text
BuildRecoveryPlan_ForMissingImageDigest_IncludesBeginnerHint
BuildRecoveryPlan_ForMissingSourceChecksum_IncludesCurlSha256sumHint
BuildRecoveryPlan_ForUnpinnedPackage_IncludesBiocondaVersionHint
RunRecoveryLoop_PrintsBeginnerHint
CliApp_Validate_KeepsRuleIdInOutput
CliApp_Render_KeepsRuleIdInOutput
```

완료 조건:

```text
interactive recovery는 기존 RecipeValidationRecoveryPlan을 사용한다.
새 UserFacingViolation 또는 ViolationMessageFormatter를 만들지 않는다.
validate/render의 rule ID와 field 출력이 유지된다.
```

검증:

```bash
dotnet test --project tests/NodeKit.Tests/NodeKit.Tests.csproj --filter RecipeAuthoringSessionTests
dotnet test --project tests/NodeKit.Cli.Tests/NodeKit.Cli.Tests.csproj --filter "RecipeCreateInteractiveTests|CliAppTests"
```

---

## Sprint 4 — Digest Resolver Seam

현재 상태:

```text
완료 — IImageDigestResolver, ImageDigestResolutionResult, NullImageDigestResolver, fake resolver 테스트 추가.
```

목표:

```text
실제 registry 연동 없이도 향후 자동 digest 조회를 붙일 수 있는 seam과 UX 테스트를 만든다.
```

대상 파일:

```text
src/NodeKit.Cli/IImageDigestResolver.cs
src/NodeKit.Cli/NullImageDigestResolver.cs
src/NodeKit.Cli/BeginnerGuideFlow.cs
tests/NodeKit.Cli.Tests/BeginnerGuideFlowTests.cs
```

작업:

1. `ImageDigestResolutionStatus`를 추가한다.
2. `ImageDigestResolutionResult`를 추가한다.
3. `IImageDigestResolver`를 추가한다.
4. `NullImageDigestResolver`를 추가하고 기본값으로 사용한다.
5. `BeginnerGuideFlow.Run(...)` overload를 추가해 기존 호출부 compatibility를 유지한다.
6. resolver 성공 시 digest 사용 여부를 묻는다.
7. resolver 실패/unsupported 시 사람 말 안내 후 기존 수동 입력 경로로 이어진다.
8. 테스트 double로 성공/실패/거부 UX를 검증한다.

비구현:

```text
HarborImageDigestResolver
OciImageDigestResolver
SkopeoImageDigestResolver
Docker daemon resolver
```

테스트:

```text
ContainerImageFlow_WhenResolverReturnsDigest_AsksToUseDigest
ContainerImageFlow_WhenResolverUnsupported_FallsBackToManualDigestInput
ContainerImageFlow_WhenResolverFails_PrintsHumanReadableReason
ContainerImageFlow_WhenUserRejectsResolvedDigest_AsksManualDigest
```

완료 조건:

```text
네트워크 없는 환경에서 테스트가 안정적으로 통과한다.
resolver seam은 있지만 production registry lookup은 없다.
digest 없는 container recipe 통과 경로는 없다.
```

검증:

```bash
dotnet test --project tests/NodeKit.Cli.Tests/NodeKit.Cli.Tests.csproj --filter BeginnerGuideFlowTests
```

---

## Sprint 5 — 통합 검증과 회귀 확인

현재 상태:

```text
완료 — dotnet build NodeKit.sln warning 0, dotnet test --solution NodeKit.sln 403개 통과.
```

목표:

```text
CLI authoring UX 변경이 기존 legacy BuildRequest 경로와 commercial guardrail을 깨지 않았는지 확인한다.
```

작업:

1. 전체 테스트를 실행한다.
2. `validate`/`render` rule ID 출력 유지 테스트를 확인한다.
3. BuildKind null guard 테스트를 확인한다.
4. `RecipeAuthoringSession.Build()`가 `BuildKind`를 자동 resolve하지 않는 테스트를 확인한다.
5. `ImageReferenceNormalizer` digest conflict 테스트를 확인한다.
6. 신규 파일이 explicit NuGet version/lockfile 정책을 건드리지 않았는지 확인한다.
7. 새 CLI 문구에 submit/build/draft 저장 안내가 들어가지 않았는지 검색한다.

검증:

```bash
dotnet build NodeKit.sln
dotnet test NodeKit.sln
rg "nodekit submit|nodekit build-request submit|checksum 없이 진행|draft 저장|나중에 추가" src docs tests
```

완료 조건:

```text
dotnet build 통과
dotnet test 전체 통과
빌드 warning 증가 없음
legacy BuildRequest 경로 유지
NodeVault 신규 production API 경로 추가 없음
```

---

## 권장 작업 순서

```text
Sprint 0 완료
→ Sprint 1
→ Sprint 2
→ Sprint 3
→ Sprint 5 중간 검증
→ Sprint 4
→ Sprint 5 최종 검증
```

이 순서를 권장하는 이유는 Sprint 1~3만으로도 사용자 체감 UX가 크게 좋아지고, Sprint 4는 seam 추가로 private method signature와 테스트 transcript 변경 폭이 커질 수 있기 때문이다.

---

## 예상 UX 점수

| 단계 | 초보자/도메인 전문가 UX 예상 점수 |
|---|---:|
| 현재 구현 | 4.5~5/10 |
| Sprint 1 완료 | 5.5~6/10 |
| Sprint 2 완료 | 6~6.5/10 |
| Sprint 3 완료 | 6.5/10 |
| Sprint 4 완료 | 6.5~7/10 |
| 실제 registry resolver까지 v1.1에서 완료 | 7.5~8/10 |

v1.0은 "막힌 이유와 다음 행동을 알 수 있는 CLI"가 목표다. "CLI가 외부 값을 자동으로 찾아 실수를 대부분 흡수하는 UX"는 실제 registry/search/checksum helper가 들어가는 v1.1 이후 범위로 둔다.
