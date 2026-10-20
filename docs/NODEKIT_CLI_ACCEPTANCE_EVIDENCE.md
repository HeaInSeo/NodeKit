# NodeKit CLI v0.9.1 인수 판정 ID ↔ 저장소 증거 연결표

NodeKit CLI v0.9.1 자체 스프린트 계약의 **27개 인수 단위·156개 판정 ID**가 이 저장소의 어느 시험·CI 단계·문서에서
확인되는지 연결한다. 이 표는 위치를 알려 줄 뿐이며 그 자체로 PASS가 아니다. 각 단위의 인수는 아래 단위 표의
"닫는 검토 단계"의 독립 검토 기록과 같은 release의 CI 결과로 판정한다. 실행 결과(통과/실패/skip)는 CI artifact
`cli-test-report`(xunit)에 있다. 판정 ID별 기대 결과의 원문은 계약 문서에 있고 여기서는 입력·상황만 옮긴다.

작성 기준: main `327abe2f`의 `tests/` 시험 method 이름·주석에서 판정 ID를 찾고, 찾지 못한 ID만 손으로 연결했다.
뒤에 시험 이름이 바뀌면 이 표도 함께 고친다.

연결 종류:

- **직접(이름)**: 시험 method 이름에 판정 ID가 들어 있다.
- **직접(언급)**: 시험 파일의 주석/문자열이 판정 ID를 명시한다(method 이름에는 없다).
- **주제**: 판정 ID를 명시한 시험은 없고, 같은 동작을 다루는 시험을 이름으로 연결했다. 판정 ID별 증명이 아니라 위치 안내다.
- **CI·script / 문서**: 시험 method가 아니라 CI 단계·검사 script·문서가 근거다.

집계: 직접(이름) 74, 직접(언급) 54, 주제 13, CI·script 10, 문서 5 — 합계 156.

## 1. 인수 단위와 닫는 검토 단계

| 단위 | 판정 ID 수 | 닫는 검토 단계 |
|---|---|---|
| S1-01 | 7 | P01.support:R |
| S1-02 | 7 | P02.loader:R |
| S1-03 | 7 | P03.author:R |
| S1-04 | 6 | P03.author:R |
| S1-05 | 6 | P03.author:R |
| S1-06 | 7 | P02.writer_caller:R |
| S1-07 | 5 | P03.render:R |
| S2-01 | 6 | P05.wire:R |
| S2-02 | 8 | P04.integrate:R |
| S2-03 | 5 | P05.reentry:R |
| S2-04 | 6 | P05.reentry:R |
| S2-05 | 7 | P05.result:R |
| S3-01 | 3 | P01.contract:R |
| S3-02 | 6 | P06.base_script:R + P06.raw_typed:R |
| S3-03 | 5 | P06.base_script:R + P04.integrate:R |
| S3-04 | 5 | P06.raw_typed:R + P07.build:R |
| S3-05 | 5 | P07.build:R |
| S3-06 | 6 | P06.raw_typed:R |
| S3-07 | 7 | P08.registry:R |
| S3-08 | 7 | P08.timeout:R |
| S3-09 | 4 | P06.current:R + P07.build:R |
| S4-01 | 3 | P09.clean_build:R |
| S4-02 | 4 | P10.ci_verify:R |
| S4-03 | 7 | P02.late_caller:R + P06.current:R + P07.build:R |
| S4-04 | 7 | P10.portability:R |
| S4-05 | 5 | P10.ci_verify:R |
| S4-06 | 5 | P10.docs_final:R |

## 2. 판정 ID별 연결

경로는 `tests/` 기준이다.

| 판정 ID | 입력·상황 | 연결 | 근거 |
|---|---|---|---|
| S1-01-C01 | six public names 및 세 사용자 모드의 source trace | 직접(언급) | `NodeKit.Cli.Tests/SupportTableCellTests.cs` |
| S1-01-C02 | Guided mode1 → install-command 단서 → conda install -c bioconda bwa=0.7.17=h5bf99c6_8 -y → F-PACKAGE 필수값 | 직접(언급) | `NodeKit.Cli.Tests/SupportTableCellTests.cs` |
| S1-01-C03 | Quick mode2 → 추천 거절 → manual mirror 선택 → F-MIRROR 입력 | 직접(언급) | `NodeKit.Cli.Tests/ManualChoiceSummaryVisibilityTests.cs`, `NodeKit.Cli.Tests/SupportTableCellTests.cs` |
| S1-01-C04 | mode selector에서 3 선택 | 직접(언급) | `NodeKit.Cli.Tests/SupportTableCellTests.cs` |
| S1-01-C05 | recipe create out.json --non-interactive --method container; --field ImageRef=condaforge/miniforge3:24.3.0-0, --field ImageDigest=sha256:<64개의 a>, ToolName/ToolVersion/Script=F-CONTAINER 대응 값 | 직접(언급) | `NodeKit.Cli.Tests/SupportTableCellTests.cs` |
| S1-01-C06 | --non-interactive --method source-build-structured | 직접(언급) | `NodeKit.Cli.Tests/SupportTableCellTests.cs` |
| S1-01-C07 | 지원표 18셀을 parameterized reference fixture로 검사: container/package/mirror/source-structured/dockerfile의 Guided·Quick·비대화형 15셀 + legacy source의 Quick·비대화형 2셀 + source Guided 직접 미지원 1셀 | 직접(언급) | `NodeKit.Cli.Tests/SupportTableCellTests.cs` |
| S1-02-C01 | F-PACKAGE를 UTF-8 JSON으로 저장하고 validate | 직접(이름) | `NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S1_02_C01_ValidPackageRecipe_ValidatesThroughFullPipeline` |
| S1-02-C02 | recipe file 내용 '{broken json' | 직접(이름) | `NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S1_02_C02_BrokenJson_ReturnsTwoAndWritesNoRenderOutput` |
| S1-02-C03 | 유효 JSON에서 BuildKind 누락 | 직접(이름) | `NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S1_02_C03_MissingBuildKind_ReturnsTwoAndHintIncludesSourceBuildStructured` |
| S1-02-C04 | F-PACKAGE JSON의 BuildKind=999 | 직접(이름) | `NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S1_02_C04_UndefinedNumericBuildKind_ReturnsTwoWithoutRendererThrowOrRpc` |
| S1-02-C05 | F-SOURCE JSON의 SourceBuildCommands=[null] | 직접(이름) | `NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S1_02_C05_SourceBuildCommandsNullElement_ReturnsL1ViolationNotCrash` |
| S1-02-C06 | 존재하지 않는 Recipe 경로로 validate/render | 직접(이름) | `NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S1_02_C06_MissingRecipePath_ValidateAndRenderReturnTwoWithoutOutput` |
| S1-02-C07 | Recipe read가 UnauthorizedAccessException으로 실패하는 고정 IO 조건 | 직접(이름) | `NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S1_02_C07_RecipePathIsDirectory_ReturnsTwoWithPermissionDiagnostic`<br>`NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S1_02_C07_UnreadableRecipeFile_ReturnsTwoWithPermissionDiagnostic` |
| S1-03-C01 | conda install -c bioconda bwa=0.7.17=h5bf99c6_8 -y를 입력 parser에 전달 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_03_C01_ParserKeepsEveryEqualsOfTheFullPin` |
| S1-03-C02 | F-PACKAGE의 Packages=['bwa=0.7.17']; strict flag 없음 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_03_C02_VersionOnlyPin_DefaultValidateIsLocalOkOnly` |
| S1-03-C03 | 동일 version-only fixture에 --strict-reproducible | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_03_C03_VersionOnlyPin_StrictReproducibleIsInvalid` |
| S1-03-C04 | 지원 parser 밖의 apt install bwa 입력 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_03_C04_UnsupportedInstaller_FailsWithoutPackages` |
| S1-03-C05 | F-PACKAGE의 version-only pin에 후보 2개 응답; 사용자가 bwa=0.7.17=h5bf99c6_8 후보 선택 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_03_C05_SelectedCandidate_IsSavedAndRenderedVerbatim` |
| S1-03-C06 | candidate singleton FullPin='bwa=0.7.17;echo bad' 고정 응답 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_03_C06_MalformedCandidate_IsBlockedByFullL1AfterItIsApplied` |
| S1-03-C07 | ResolveRecipe RpcUnavailable 고정 응답; 기존 full pin 또는 version-only 원문 유지 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_03_C07_ResolveUnavailable_KeepsTheTypedPinAndPromisesNoSubmitReResolve` |
| S1-04-C01 | Guided mode1 → source URI 단서 + 유효 sha256 checksum + tool/script/commands | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_04_C01_StructuredSourceIsNotLabelledOptIn` |
| S1-04-C02 | --non-interactive --method source-structured로 F-STRUCTURED 작성 후 render | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_04_C02_NonInteractiveStructured_RendersTwoStageDockerfile` |
| S1-04-C03 | F-SOURCE에서 SourceChecksum='' | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_04_C03_EmptySourceChecksum_IsL1Src001` |
| S1-04-C04 | F-STRUCTURED에서 BuildProfile=advanced, BuildProfileImage='ubuntu:latest' | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_04_C04_AdvancedProfileWithTagOnlyImage_IsL1Rcp017` |
| S1-04-C05 | F-STRUCTURED에서 SourceBuildCommands의 첫 문자열을 make + LF(U+000A) + USER root로 지정한다. JSON fixture 표기: `["make USER root"]`. 직렬화된 역슬래시 n은 deserialize 후 실제 LF여야 한다. | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_04_C05_BuildCommandWithRealLineFeed_IsL1Rcp015` |
| S1-04-C06 | legacy --method source의 F-SOURCE에 BuildDependencies=['make','gcc'] 입력 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_04_C06_LegacySourceBuildDependencies_AdvisoryOnlyAndNotInstalled` |
| S1-05-C01 | --method dockerfile --non-interactive --accept-dockerfile-warning; F-DOCKERFILE Content만 제공, Path 없음 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C01_ContentOnly_SavesWithoutPath` |
| S1-05-C02 | 동일 flags; 임시 UTF-8 Dockerfile 경로만 제공(Content 입력 없음) | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C02_PathAndContentInput_ProduceTheSameRawSpec`<br>`NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C02_PathOnly_FreezesFileBytesIntoContent`<br>`NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C02_Utf8BomIsNotPartOfTheContent` |
| S1-05-C03 | DockerfilePath와 DockerfileContent를 동시에 제공 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C03_PathAndContentTogether_IsAnInputConflict` |
| S1-05-C04 | Path-only 입력의 파일이 존재하지 않음 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C04_DirectoryPath_IsReadFailure`<br>`NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C04_EmptyOrInvalidPath_IsReadFailureNotACrash`<br>`NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C04_MissingPath_IsReadFailureWithoutStackTrace`<br>`NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C04_NonUtf8File_IsReadFailure` |
| S1-05-C05 | Content-only에 FROM <F-PIN>만 있고 USER 없음 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C05_NoUserInstruction_IsBlockedByFullL1` |
| S1-05-C06 | Dockerfile authoring에서 BuildContext='./app' 비기본 local 경로 요청 | 직접(이름) | `NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C06_BuildContextHelpSaysNoLocalTransfer`<br>`NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C06_HandWrittenBuildContext_IsCheckedBySharedValidation`<br>`NodeKit.Cli.Tests/RecipeAuthorP03Tests.cs::S1_05_C06_NonDefaultBuildContext_IsUnsupported` |
| S1-06-C01 | Quick field 중 두 번째 scalar prompt에서 /back | 주제 | `NodeKit.Cli.Tests/RecipeCreateInteractiveTests.cs::BackCommand_AtFieldPrompt_RepromptsPreviousField` |
| S1-06-C02 | StringList에 두 package 입력 중 /back 후 이전 field 수정·재입력 | 주제 | `NodeKit.Cli.Tests/RecipeCreateInteractiveTests.cs::BackCommand_AtListFieldPrompt_ClearsStaleItemsAndRepromptsPreviousField` |
| S1-06-C03 | package session에 ToolName/ToolVersion/Packages를 입력하되 Build 이전에 /change-method → container 변경 승인 | 주제 | `NodeKit.Cli.Tests/RecipeCreateInteractiveTests.cs::ChangeMethodMidFieldEntry_PackageToSource_PreservesToolNameAndDiscardsPackageFields`<br>`NodeKit.Tests/Recipes/RecipeAuthoringSessionTests.cs::ChangeMethod_Proceed_DiscardsMethodSpecificFieldsAndSwitchesMethod` |
| S1-06-C04 | 저장 전 simulated Ctrl-C 또는 확인된 /cancel | 직접(언급) | `NodeKit.Cli.Tests/AtomicWriterCallerTests.cs` |
| S1-06-C05 | F-SOURCE checksum 누락으로 recovery 진입 → 해당 field 수정하여 유효 sha256 입력 | 주제 | `NodeKit.Tests/Recipes/RecipeAuthoringSessionTests.cs::BuildRecoveryPlan_ForMissingSourceChecksum_IncludesCurlSha256sumHint`<br>`NodeKit.Cli.Tests/RecipeCreateInteractiveTests.cs::CrossFieldImageDigestViolation_TriggersEditRelatedFieldsRecovery_FixesAndSaves` |
| S1-06-C06 | 최종 save confirmation에서 n → wizard 재시작 → 두 번째 정상 Recipe save | 주제 | `NodeKit.Cli.Tests/SavePathConfirmationTests.cs::NullOutPath_Step9_NRestarts_ThenSavesOnSecondAttempt` |
| S1-06-C07 | 유효 Recipe save의 output parent가 없거나 write가 UnauthorizedAccessException | 주제 | `NodeKit.Cli.Tests/AtomicWriterCallerTests.cs::InteractiveRecipeCreate_SaveFails_Exit2_KeepsExistingBytes_NoSavedMessage`<br>`NodeKit.Cli.Tests/AtomicWriterCallerTests.cs::Caller_ParentMissing_Exit2_NoFileNoDirectoryCreated` |
| S1-07-C01 | F-PACKAGE에 nodekit render --format build-request | 직접(이름) | `NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C01_BuildRequestFormat_WritesLegacyPreviewShape`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C01_DefaultFormatIsBuildRequest`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C01_HelpSaysBuildRequestIsNotTheSubmitInput` |
| S1-07-C02 | F-PACKAGE에 render --format raw-spec (compact) | 직접(이름) | `NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C02_OtherBuildKinds_UseSevenKeysAndKindOne`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C02_RawSpecCompact_HasExactSevenKeysInWireOrder`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C02_RawSpecFile_IsByteIdenticalToFactoryOutput`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C02_RawSpecFile_IsByteIdenticalToWhatSubmitSends`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C02_StdoutOut_PrintsTheSameRawSpecPlusNewline` |
| S1-07-C03 | F-PACKAGE Channels의 첫 문자열을 bioconda + LF(U+000A) + RUN echo bad로 지정해 render한다. JSON fixture 표기: `["bioconda RUN echo bad"]`. 실제 입력에는 LF가 들어간다. | 직접(이름) | `NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C03_ChannelWithLineFeed_ExitsOneWithNoOutput`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C03_ChannelWithLineFeed_KeepsAnExistingOutputFile`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C03_ChannelWithLineFeed_StdoutOutPrintsNothing` |
| S1-07-C04 | 정상 render --out 경로가 디렉터리 또는 없는 parent | 직접(이름) | `NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C04_OutPathIsDirectory_ExitsTwoWithoutSuccessText`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C04_OutPathParentMissing_ExitsTwoWithoutCreatingIt` |
| S1-07-C05 | local Recipe/ToolDefinition에 legacy Command/Inputs/Outputs/display fields를 채워 raw_spec 생성 | 직접(이름) | `NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_FactoryIgnoresLegacyToolDefinitionFields`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_HumanSubmitOutput_GivesBothDigestsForFunctionRecipe`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_InteractiveCreate_PointsCommandAndPortsToFunctionRecipe`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_JsonlSubmitOutput_GivesBothDigestsForFunctionRecipe`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_JsonlSubmitOutput_PreWatchFailureHasNoToolSpecDigest`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_JsonlSubmitOutput_ReportsToolSpecDigestOnResolutionStateRecord`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_LegacyFields_AreInBuildRequestButNotInRawSpec`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_NonInteractiveCommandField_WarnsThatItIsNotSent`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_NonInteractiveWithoutCommand_HasNoCommandWarning`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_RenderHelp_SaysLegacyFieldsAreNotInRawSpec`<br>`NodeKit.Cli.Tests/RecipeRenderP03Tests.cs::S1_07_C05_SubmitDoesNotSendLegacyFields` |
| S2-01-C01 | checksum 누락 Recipe와 version-only package Recipe의 기본/--strict-reproducible submit preflight | 직접(이름) | `NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs::S2_01_C01_MissingSourceChecksum_ExitsOneWithZeroRpc`<br>`NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs::S2_01_C01_VersionOnlyPackage_DefaultSubmit_SendsTheRenderedPayloadUnchanged`<br>`NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs::S2_01_C01_VersionOnlyPackage_StrictReproducible_ExitsOneWithZeroRpc` |
| S2-01-C02 | F-PACKAGE를 submit하고 ResolveToolSpec 요청 capture | 직접(이름) | `NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs::S2_01_C02_ResolveRequest_CarriesRecipeIdentityAndRenderedRawSpecBytes`<br>`NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs::S2_01_C02_ResolveRequest_IsNotTheLegacyBuildRequestPreview` |
| S2-01-C03 | ResolveToolSpec가 F-WIRE resolved_digest 응답; Submit가 F-WIRE build_id 응답 | 직접(이름) | `NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs::S2_01_C03_ResolvedDigestAndBuildId_FlowIntoSubmitAndWatch_InOrderOnceEach` |
| S2-01-C04 | ResolveToolSpec가 RpcException(Unavailable) 반환 | 직접(이름) | `NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs::S2_01_C04_ResolveUnavailable_CliReportsPreWatchUnknown_WithoutRetry`<br>`NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs::S2_01_C04_ResolveUnavailable_NoSubmitNoWatch_FailedWithDiagnostic` |
| S2-01-C05 | Resolve 성공 후 SubmitToolBuild 응답만 lost/error; build_id 없음 | 직접(이름) | `NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs::S2_01_C05_SubmitResponseLost_CliKeepsRemoteStateUnknown`<br>`NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs::S2_01_C05_SubmitResponseLost_NoWatch_NoBuildIdInvented` |
| S2-01-C06 | wire/golden/opt-in integration test source를 evidence map과 대조 | 직접(언급) | `NodeKit.Cli.Tests/ToolSpecWireP05Tests.cs` |
| S2-02-C01 | 정상 F-PACKAGE 신규 attempt; 고정 request_id provider=11111111-1111-1111-1111-111111111111 | 직접(이름) | `NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C01_BlankEnvelopeField_IsRefusedBeforePersist_ExitTwo_NoRpc`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C01_DurableRecordPrecedesEachSideEffectRpc_AndSubmitUsesStoredRequestId`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C01_MalformedSourceEntry_IsRefusedBeforePersist_ExitTwo_NoRpc` |
| S2-02-C02 | 같은 정상 attempt에 Submit가 build-fixture-001, Watch가 Succeeded+F-WIRE artifact 반환 | 직접(이름) | `NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C02_BuildIdAndTerminalObservation_AreReadableAfterHandleIsClosed`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C02_InterruptedStatus_PersistsFailedOutcome`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C02_SpecReferrerDigest_IsPreservedInTheTerminalReceipt`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C02_TerminalKindWithoutStatus_PersistsTheOutcome`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C02_WatchEventOfAnotherBuild_IsRejected_AndNotPersisted` |
| S2-02-C03 | Submit 이전 journal write/replace를 고정 실패시킴 | 직접(이름) | `NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C03_InFlightWriteFails_AfterResolve_ExitTwo_SubmitZero`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C03_PreparedWriteFails_ExitTwo_NoRpc_NoRecord` |
| S2-02-C04 | prepared 상태이며 이전 Submit 호출이 없음을 아는 동일 journal로 두 번째 CLI 실행 | 직접(이름) | `NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C04_SecondRunOnPreparedJournal_ReusesStoredIdAndPayload_AndRedoesInFlightTransition` |
| S2-02-C05 | receipt의 request_id를 유지한 채 raw_spec 또는 endpoint 변경 요청 | 직접(이름) | `NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C05_ChangedPayloadOrEndpointUnderStoredId_IsRefused_RecordUnchanged`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C05_CreatingOverAnExistingRecord_ExitsTwo_WithoutOverwriting`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C05_EditedRecipePreservesPastBasis_DivergenceIsReported_ReplayDoesNotRereadSource`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C05_EndpointMismatch_TakesPrecedenceOverResumeState_NoRpc`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C05_ReentryAgainstDifferentEndpoint_IsRefusedBeforeResolve_RecordUnchanged` |
| S2-02-C06 | 같은 journal을 두 writer가 동시에 열고 submit 시도 | 직접(이름) | `NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C06_HeldRecordLock_DoesNotBlockTheHoldersOwnWrites`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C06_SecondWriterOnSameRecord_IsLockedOut_WithoutSubmitting` |
| S2-02-C07 | 기존 prepared 기록을 바꾸는 동안 임시 파일 write/atomic replace 단계에서 장애 또는 process 종료 주입 | 직접(이름) | `NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C07_InFlightRecordWithoutBuildId_IsNotBlindlyResubmitted`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C07_InconsistentInFlightRecordOrSnapshot_IsIntegrityErrorExitTwo_NoRpc`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C07_InterruptedReplace_LeavesPreviousCompleteRecord_AndNothingIsSubmitted`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C07_PartialOrInconsistentRecord_IsIntegrityErrorExitTwo_NoRpc`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C07_RecordWithLaterPhaseFields_IsIntegrityErrorExitTwo_NoRpc`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C07_ReopenWithBlankEnvelopeField_IsIntegrityErrorExitTwo_NoRpc`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C07_ReopenWithMalformedSourceEntry_IsIntegrityErrorExitTwo_NoRpc`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C07_TerminalRecordWithoutObservation_IsIntegrityErrorExitTwo_NoRpc`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C07_TerminalRecordWithoutOutcome_IsIntegrityErrorExitTwo_NoRpc` |
| S2-02-C08 | Submit가 build_id를 반환했지만 receipt 저장 실패 또는 terminal 결과 저장 실패 | 직접(이름) | `NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C08_AckWriteFails_ExitTwo_ObservedBuildIdKeptInDiagnostic_NoNewSubmit`<br>`NodeKit.Cli.Tests/LocalOperationStoreTests.cs::S2_02_C08_TerminalWriteFails_ExitTwo_ObservedResultKept_DurableStaysAcknowledged` |
| S2-03-C01 | acknowledged receipt(build-fixture-001)로 재진입 watch 요청 | 직접(언급) | `NodeKit.Cli.Tests/ReceiptReentryP05Tests.cs` |
| S2-03-C02 | submit_in_flight/unknown receipt이고 build_id 없음; 재진입 요청 | 직접(언급) | `NodeKit.Cli.Tests/ReceiptReentryP05Tests.cs` |
| S2-03-C03 | 같은 build_id로 두 번 watch; fake가 과거와 동일한 progress event를 재전달 | 직접(언급) | `NodeKit.Cli.Tests/ReceiptReentryP05Tests.cs` |
| S2-03-C04 | saved acknowledged receipt로 독립 cancel 요청 | 직접(언급) | `NodeKit.Cli.Tests/ReceiptReentryP05Tests.cs` |
| S2-03-C05 | 잘린 JSON/unsupported schema/hash mismatch receipt로 watch 또는 cancel 재진입 | 직접(언급) | `NodeKit.Cli.Tests/ReceiptReentryP05Tests.cs` |
| S2-04-C01 | build-fixture-001 수신 후 사용자 cancel/OCE | 직접(언급) | `NodeKit.Cli.Tests/SubmitCancelTimeoutP05Tests.cs` |
| S2-04-C02 | build_id를 받기 전에 사용자 cancel | 직접(언급) | `NodeKit.Cli.Tests/SubmitCancelTimeoutP05Tests.cs` |
| S2-04-C03 | known ID cancel RPC가 token까지 응답하지 않는 hanging fake | 직접(언급) | `NodeKit.Cli.Tests/SubmitCancelTimeoutP05Tests.cs` |
| S2-04-C04 | --connect-timeout 1; build_id 전 단계가 hanging | 직접(언급) | `NodeKit.Cli.Tests/SubmitCancelTimeoutP05Tests.cs` |
| S2-04-C05 | --watch-timeout 1s; build_id 수신 후 관측만 hanging | 직접(언급) | `NodeKit.Cli.Tests/SubmitCancelTimeoutP05Tests.cs` |
| S2-04-C06 | connect timeout보다 늦게 끝나더라도 build_id를 먼저 수신하고, watch timeout 전 Succeeded 반환 | 직접(언급) | `NodeKit.Cli.Tests/SubmitCancelTimeoutP05Tests.cs` |
| S2-05-C01 | F-WIRE JobCreated→Pushing→Succeeded+image_digest로 --format jsonl | 직접(언급) | `NodeKit.Cli.Tests/SubmitResultP05Tests.cs` |
| S2-05-C02 | known build_id의 Watch event Status=Failed | 직접(언급) | `NodeKit.Cli.Tests/SubmitResultP05Tests.cs` |
| S2-05-C03 | SubmitToolBuild pre-watch 실패 이벤트; build_id 없음 | 직접(언급) | `NodeKit.Cli.Tests/SubmitCommandTests.cs`, `NodeKit.Cli.Tests/SubmitResultP05Tests.cs` |
| S2-05-C04 | JobCreated 후 stream EOF, terminal 없음 | 직접(언급) | `NodeKit.Cli.Tests/SubmitResultP05Tests.cs` |
| S2-05-C05 | --format jsonl; preflight parse 오류와 여러 progress/terminal 메시지에 따옴표·개행 포함 | 직접(언급) | `NodeKit.Cli.Tests/SubmitResultP05Tests.cs` |
| S2-05-C06 | legacy DigestAcquired→Succeeded(최종 image_digest 없음), 또는 ImageDigest+IntegrityHealth=Partial 고정 event | 직접(언급) | `NodeKit.Cli.Tests/SubmitResultP05Tests.cs` |
| S2-05-C07 | Watch progress의 Status='FutureState' 후 실제 Succeeded terminal | 직접(언급) | `NodeKit.Cli.Tests/SubmitResultP05Tests.cs` |
| S3-01-A | 최신 proto를 생성한다 | 직접(언급) | `NodeKit.Cli.Tests/ContractFixtureFreezeTests.cs`, `NodeKit.Cli.Tests/ToolFunctionCanonicalizationVersionWireTests.cs` |
| S3-01-B | first-image kind1 existing fixture를 직렬화한다 | 직접(언급) | `NodeKit.Cli.Tests/ContractFixtureFreezeTests.cs` |
| S3-01-C | ToolFunctionSpec 경계 문서를 검토한다 | 직접(언급) | `NodeKit.Cli.Tests/ContractFixtureFreezeTests.cs` |
| S3-02-A | baseImageDigest=A, secondImageDigest=B, baseResolvedToolSpecDigest=C1, secondBuildResolvedRequestDigest=C2; 네 값이 모두 다른 fixture | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_02_A_CanonicalizationVersionIsW2SetV1AndRequestIdIsPassedThrough`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_02_A_CompleteSameBuildReceiptIsObservedSameBuild`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_02_A_FourDistinctDigestsLandInTheirOwnRoles`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_02_A_ManualMixedOrIncompleteLinkIsManualUnverified` |
| S3-02-B | baseToolImageDigest='sha256:bad'인 loaded Recipe를 submit/render한다 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_02_B_InvalidBaseImageDigest_ReturnsExit1_BeforeReadingScript` |
| S3-02-C | base image digest full64hex의 A-F 대문자 표현 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_02_C_UpperCaseBaseDigestIsKeptVerbatim`<br>`NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_02_C_UpperCaseHexDigest_IsAcceptedVerbatim` |
| S3-02-D | registration에 second image digest 없음 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_02_D_MalformedSecondImageDigestIsRejected`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_02_D_MissingSecondImageDigestIsRejectedWithoutFallback` |
| S3-02-E | C1 또는 C2만 있고 exact base image A가 없는 입력; A/B만 있고 기반 ToolSpec C1이 없는 typed 등록 입력 변형 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_02_E_ABWithoutC1IsRejectedAndPointsToToolSpecDigestOption`<br>`NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_02_E_MissingBaseImage_ReturnsExit1_WithoutFallbackToToolSpecDigest` |
| S3-02-F | typed 제출의 ToolSpecDigest=C1에 sha256:64 hex(이미지 모양), 63자리, hex 이외 문자 또는 공백을 포함한 값을 제공 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_02_F_MalformedC1IsRejectedBeforeRegistration`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_02_F_ValidBareLowerHexC1IsPassedVerbatim` |
| S3-03-A | recipe=/tmp/project/recipe.json, ScriptPath='./sort.sh', 실행 CWD=/tmp/else | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_A_DifferentCwd_ReadsScriptNextToRecipe`<br>`NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_A_NestedAndParentRelativePaths_ResolveAgainstRecipeDirectory`<br>`NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_A_RelativeScriptPath_ResolvesAgainstRecipeDirectory` |
| S3-03-B | script 파일의 UTF-8 bytes는 #!/bin/sh + LF(U+000A) + echo 1 + LF(U+000A)다. JSON 문자열 표기: `"#!/bin/sh echo 1 "`. shell/source 파일에는 두 LF가 실제로 들어간다. | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_B_NonAsciiAndBomBytes_ArePreservedVerbatim`<br>`NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_B_ScriptContentIsExactSource_NotThePath` |
| S3-03-C | ScriptPath가 없는 파일/디렉터리/읽기 거부 경로 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_C_DirectoryPath_ReturnsExit2Diagnostic`<br>`NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_C_InvalidUtf8_ReturnsExit2Diagnostic`<br>`NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_C_MissingFile_ReturnsExit2Diagnostic`<br>`NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_C_MissingParentDirectory_ReturnsExit2Diagnostic`<br>`NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_C_PathWithInvalidCharacter_ReturnsExit2Diagnostic_WithoutException`<br>`NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_C_UnreadableFile_ReturnsExit2Diagnostic` |
| S3-03-D | script 내용 공백/개행뿐 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_D_BlankScript_ReturnsExit1` |
| S3-03-E | 신규 요청 전 script 변경 및 기존 receipt 이후 script/Recipe 변경·삭제 비교 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionSourceMaterializerP06Tests.cs::S3_03_E_FreshSnapshotIsFixed_AndNextFreshReadsCurrentSource` |
| S3-04-A | 정상 ToolFunctionRecipe+source를 raw_spec으로 직렬화 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_04_A_RawSpecHasExactlyFourKeysWithV1Values` |
| S3-04-B | Port/Resource/Display/FunctionId/Revision/ToolSpecDigest가 모두 채워진 Recipe | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_04_B_FullRecipeThroughMaterializerAddsNoTypedMetadataToRawSpec` |
| S3-04-C | actual ResolveToolSpec request를 fake server에서 캡처 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs`, `NodeKit.Cli.Tests/ToolFunctionSecondImageBuildP07Tests.cs` |
| S3-04-D | compact/pretty local preview를 생성 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_04_D_CompactAndIndentedPreviewParseToTheSameObject` |
| S3-04-E | kind='2', schema_version 오타, extra field, blank script인 payload fixture | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_04_E_SerializerBuildsNoPayloadForInvalidFields`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_04_E_ValidatorRejectsNonV1Payloads` |
| S3-05-A | local fake BuildService가 kind2 raw_spec 접수 뒤 Succeeded+ImageDigest=B 반환 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionSecondImageBuildP07Tests.cs` |
| S3-05-B | ResolveToolSpec에서 InvalidArgument 반환 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionSecondImageBuildP07Tests.cs` |
| S3-05-C | Watch terminal Failed 또는 Interrupted 반환 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionSecondImageBuildP07Tests.cs` |
| S3-05-D | Watch가 terminal 없이 끝남 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionSecondImageBuildP07Tests.cs` |
| S3-05-E | Succeeded지만 full second image digest 없음 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionSecondImageBuildP07Tests.cs` |
| S3-06-A | 모든 실행·표시·검증·환경 필드를 채운 persisted ToolFunctionRecipe의 save/load/typed mapping 및 input-only/output-only/zero-port·empty arguments의 유효 변형(기존 MissingExecutable/duplicate/orphan ExpectedResults 실패 유지) | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_A_ExistingValidatorFailuresAreKept`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_A_InputOnlyOutputOnlyZeroPortAndEmptyArgumentsStayValidAndMap`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_A_PersistedFullRecipeSurvivesSaveReloadValidateAndMapsEveryGroup` |
| S3-06-B | local Single/Multiple, String/Integer/Number/Boolean/Enum, Ephemeral/Cache/Checkpoint/SidecarOutput/SensitiveTemp, Basic/Enhanced/Full | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_B_CardinalityMapsByNameNotByNumber`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_B_IntermediateFilePolicyKindMapsByName`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_B_LocalEnumNamesAllHaveAProtoMapping`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_B_ObservationLevelMapsByName`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_B_ParameterTypeMapsByName` |
| S3-06-C | minimumObservationLevel 미지정 및 empty optional contract group | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_C_EmptyTimeoutGroupMapsLikeAbsentAfterSaveReload`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_C_PartialTimeoutKeepsTheUnsetHalfAtZero`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_C_UnsetOptionalsStayUnspecifiedWithoutInventedDefaults` |
| S3-06-D | output note가 없는 port 또는 duplicate/orphan output note | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_D_NoteOnADuplicatedOutputNameIsRejected`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_D_NoteOnAnInputPortIsAnOrphan`<br>`NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_D_OutputWithoutNoteHasNoPresentationEntry` |
| S3-06-E | local enum numeric99 또는 type 정의 밖 값 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_E_UnknownLocalEnumValueIsRejectedNotDefaulted` |
| S3-06-F | FunctionId/Revision/display 변화만 있는 same execution recipe | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionRawTypedP06Tests.cs::S3_06_F_FunctionIdRevisionAndDisplayChangesDoNotTouchSpec` |
| S3-07-A | 정상 typed request를 local fake registry에 보냄 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs` |
| S3-07-B | response={tool_function_digest:F,cas_hash:H,presentation_revision_id:P,canonicalization_version:'w2-set-v1'} | 주제 | `NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs::A_B_RegisteredAfterBuild_DurableBeforeRpc_ServerValuesVerbatim` |
| S3-07-C | response version 비어 있음 또는 지원하지 않는 version | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs` |
| S3-07-D | 등록 ACK 유실 후 replay; 저장 뒤 원본 Recipe/script 변경·삭제 변형 포함 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs` |
| S3-07-E | 다른 B1/B2 second image에 대해 server가 같은 F와 다른 H1/H2 반환 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs` |
| S3-07-F | typed registry에서 InvalidArgument/Unavailable 반환 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs` |
| S3-07-G | supported version 응답의 tool_function_digest 또는 cas_hash가 누락/손상; 정상 응답의 receipt write 실패 변형 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs` |
| S3-08-A | 정상 build/정상 versioned registration | 주제 | `NodeKit.Cli.Tests/ToolFunctionSecondImageBuildP07Tests.cs::A_SucceededWithFullB_Exit0_OrderedOnce_ExactRawSpec_C2AndBKeptSeparately`<br>`NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs::A_B_RegisteredAfterBuild_DurableBeforeRpc_ServerValuesVerbatim` |
| S3-08-B | 인자/파일 형식 오류 또는 확정 비즈니스 거부 | 주제 | `NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs::F_InvalidArgumentRejected_UnavailableUnknown`<br>`NodeKit.Cli.Tests/ToolFunctionRegistrationTimeoutP08Tests.cs::RegistrationTimeout_InvalidValue_Exit2_RpcZero` |
| S3-08-C | --connect-timeout을 설정한 뒤 build_id를 받기 전에 예산 만료 | 주제 | `NodeKit.Cli.Tests/SubmitCancelTimeoutP05Tests.cs::ConnectTimeout_SubmitHangs_Exit124_NoCancelRpc_RemoteUnknownNotDenied` |
| S3-08-D | --watch-timeout을 설정하고 build_id를 받은 뒤 watch 예산 만료 | 주제 | `NodeKit.Cli.Tests/SubmitCommandTests.cs::Submit_WatchTimeoutFires_AfterBuildIdReceived_ReturnsDistinctExitCodeAndDoesNotCancelServerBuild` |
| S3-08-E | Ctrl-C 후 서버 cancel RPC가 hang | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionRegistrationTimeoutP08Tests.cs` |
| S3-08-F | registration request 전달 후 응답 유실 | 주제 | `NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs::D_AckLostThenReplay_SameBytesAndId_SourceEditsIgnored_NoNewBuild`<br>`NodeKit.Cli.Tests/ToolFunctionRegistryLoopbackP10Tests.cs::Loopback_AckLostThenCliReplayOverSocket_SameBytes_NoNewBuild` |
| S3-08-G | structured mode의 normal/failure/drop 및 build-submit/watch/typed-registration 각각의 deadline·사용자 취소 변형 | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionRegistrationTimeoutP08Tests.cs` |
| S3-09-A | State=Ready이지만 Command.Executable='samtools ; bad'로 파일 편집 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionCurrentPreflightP06Tests.cs::S3_09_A_ReadyButEditedToInvalidCommand_RenderExit1WithViolationAndNoOutput` |
| S3-09-B | State=Ready에서 invalid exact base digest 또는 blank script source | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionCurrentPreflightP06Tests.cs::S3_09_B_ReadyWithInvalidBaseDigestOrScriptSource_RenderFailsWithoutOutput` |
| S3-09-C | State=Ready, 현재 모든 필드 유효; #103 input-only/output-only/zero-port·empty arguments 유효 변형도 포함 | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionCurrentPreflightP06Tests.cs::S3_09_C_FreshPreflightReadsTheCurrentScriptEveryTime`<br>`NodeKit.Cli.Tests/ToolFunctionCurrentPreflightP06Tests.cs::S3_09_C_PortShapeAndEmptyArgumentVariantsRender`<br>`NodeKit.Cli.Tests/ToolFunctionCurrentPreflightP06Tests.cs::S3_09_C_ValidReady_RenderWritesOutputAndPreflightFixesFreshBasis`<br>`NodeKit.Cli.Tests/ToolFunctionSecondImageBuildP07Tests.cs::S3_09_C_StoredEnvelopeAndSourceSnapshot_UnaffectedByLaterScriptEdit`<br>`NodeKit.Cli.Tests/ToolFunctionSecondImageBuildP07Tests.cs::S3_09_C_ValidPortShapeVariants_SendFreshRawSpec` |
| S3-09-D | State=Draft 또는 외부 파일의 State=Approved | 직접(이름) | `NodeKit.Cli.Tests/ToolFunctionCurrentPreflightP06Tests.cs::S3_09_D_NonReadyStateIsRejectedAndNotRewritten` |
| S4-01-A | clean NodeKit checkout에서 NodeVault/DockGuard sibling folder 없음 | CI·script | CI `CLI clean build and closure (S4-01)` → `scripts/verify-cli-clean-build.sh` (self-test `scripts/test_verify_cli_clean_build.py`) |
| S4-01-B | CLI project/runtime deps 검토 | CI·script | `scripts/check-cli-closure.py` (self-test `scripts/test_check_cli_closure.py`), CI `CLI clean build and closure (S4-01)` |
| S4-01-C | 벤더 proto 누락 상태로 clean CLI build | CI·script | `scripts/verify-cli-clean-build.sh` C 단계(`NKPROTO004`), CI `CLI clean build and closure (S4-01)` |
| S4-02-A | CLI Linux artifact 생성 | CI·script | `scripts/package-cli.sh`, CI `CLI package is reproducible` |
| S4-02-B | 동결된 linux-x64 후보 한 packaging profile의 runtime 필요조건과 package closure 검토 | CI·script | `release/cli-manifest.template.json` + `scripts/write-cli-manifest.py`(self-test `scripts/test_write_cli_manifest.py`); 실제 실행은 S4-04-A |
| S4-02-C | archive SHA256과 manifest/source version 점검 | CI·script | `scripts/verify-cli-package.py`(self-test `scripts/test_verify_cli_package.py`), CI `CLI package output is complete` |
| S4-02-D | fresh extraction 후 설치/실행 | CI·script | `scripts/verify-cli-package.py`(alias symlink·실행 권한), 저장소 밖 실행은 S4-04-E와 공유 |
| S4-03-A | create/render/validate에서 출력 경로 directory/missing parent/permission denied | 주제 | `NodeKit.Cli.Tests/AtomicWriterCallerTests.cs::Caller_TargetIsDirectory_Exit2_Unsupported_DirectoryUntouched`<br>`NodeKit.Cli.Tests/AtomicWriterCallerTests.cs::Caller_ParentMissing_Exit2_NoFileNoDirectoryCreated`<br>`NodeKit.Cli.Tests/AtomicWriterCallerTests.cs::Caller_DirectoryNotWritable_Exit2_KeepsExistingBytes` |
| S4-03-B | ToolFunctionRecipe input read denied 또는 malformed JSON 또는 literal null | 직접(이름) | `NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S4_03_B_FunctionRecipeMalformedOrLiteralNull_ReturnsTwo`<br>`NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S4_03_B_FunctionRecipeReadDenied_ReturnsTwo` |
| S4-03-C | InputPorts/OutputPorts/Parameters/FixtureReferences 배열 원소 null | 직접(이름) | `NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S4_03_C_FunctionRecipeArrayNullElement_ValidateReturnsOneWithFieldViolation`<br>`NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S4_03_C_NestedNullElement_ReportsDottedPath`<br>`NodeKit.Cli.Tests/AuthoringLoaderContractTests.cs::S4_03_C_ReadyFunctionRecipeWithNullPort_RenderReturnsOneAndKeepsOutput` |
| S4-03-D | nodekit function-recipe validate --help | 직접(이름) | `NodeKit.Cli.Tests/FunctionRecipeLateCallerP02Tests.cs::S4_03_D_ValidateHelpAfterPath_DoesNotReadOrRewriteTheFile`<br>`NodeKit.Cli.Tests/FunctionRecipeLateCallerP02Tests.cs::S4_03_D_ValidateHelp_PrintsUsageExit0WithoutOpeningAFile` |
| S4-03-E | function-recipe validate <file> --typo / submit <file> --typo | 직접(이름) | `NodeKit.Cli.Tests/FunctionRecipeLateCallerP02Tests.cs::S4_03_E_SubmitUnknownOption_Exit2RpcZeroAndFileUntouched`<br>`NodeKit.Cli.Tests/FunctionRecipeLateCallerP02Tests.cs::S4_03_E_ValidateUnknownOption_Exit2AndFileUntouched` |
| S4-03-F | function-recipe render를 invalid Ready input으로 실행 | 직접(이름) | `NodeKit.Cli.Tests/FunctionRecipeLateCallerP02Tests.cs::S4_03_F_RenderInvalidReady_Exit1NoNewOutputAndNoOverwrite` |
| S4-03-G | 기존 파일에 marker bytes를 둔 ToolSpec/ToolFunctionRecipe create·validate·render 저장 경로; write/flush/replace 전 장애·취소/중단과 replace 전후 process 종료 변형 | 직접(언급) | `NodeKit.Cli.Tests/AtomicFileWriterTests.cs`, `NodeKit.Cli.Tests/AtomicWriterCallerTests.cs` |
| S4-04-A | 배포 binary --help 및 각 function-recipe subcommand --help | 직접(언급) | `NodeKit.Cli.Tests/Fakes/PublishedCliPackage.cs`, `NodeKit.Cli.Tests/PublishedCliProcessTests.cs` |
| S4-04-B | valid local fixture로 non-interactive create→validate→render | 직접(언급) | `NodeKit.Cli.Tests/PublishedCliProcessTests.cs` |
| S4-04-C | redirected interactive authoring의 필수 입력 중 EOF/명시 취소와 최종 저장 확인 EOF 변형 | 직접(언급) | `NodeKit.Cli.Tests/PublishedCliProcessTests.cs`, `NodeKit.Cli.Tests/ToolFunctionRecipeCreateCommandTests.cs` |
| S4-04-D | invalid fixture/unknown command로 배포 binary 실행 | 직접(언급) | `NodeKit.Cli.Tests/PublishedCliProcessTests.cs` |
| S4-04-E | 설치 경로와 작업 CWD가 repository 밖 | 직접(언급) | `NodeKit.Cli.Tests/PublishedCliProcessTests.cs` |
| S4-04-F | 선택 profile의 실제 published executable을 repo 밖에서 subprocess 실행; --url 또는 NODEKIT_NODEVAULT_URL로 명시한 loopback endpoint의 고정 fake server와 receipt directory. ToolSpec 제출/watch·별도 process 재진입, ToolFunctionSpec image-build→versioned typed 등록 및 ACK 유실 replay 변형 | 직접(언급) | `NodeKit.Cli.Tests/Fakes/PublishedCliPackage.cs`, `NodeKit.Cli.Tests/PublishedCliProcessTests.cs`, `NodeKit.Cli.Tests/ToolFunctionRegistryPublishedP10Tests.cs` |
| S4-04-G | 작성 환경 A의 실제 CLI가 만든 기존 포맷 Recipe 파일을 원본 A가 접근 불가한 별도 clean 설치 환경 B로 복사. B의 다른 설치 경로·사용자 경로·CWD에서 재사용. ToolSpec 6 public fixture, ToolFunctionRecipe와 상대 script, 참조 누락·지원 schema 오류·source binding 변형. | 직접(언급) | `NodeKit.Cli.Tests/Fakes/PublishedCliPackage.cs`, `NodeKit.Cli.Tests/PublishedCliPortabilityTests.cs` |
| S4-05-A | main/PR CI에서 CLI scoped job 실행 | 직접(언급) | `NodeKit.Cli.Tests/HarborImageMapEnvironmentIsolationTests.cs` |
| S4-05-B | raw_spec.v1 mapper/proto enum/version response 의도적 regression | 직접(언급) | `NodeKit.Cli.Tests/ToolFunctionRegistryP08Tests.cs` |
| S4-05-C | publish artifact 또는 package smoke output 누락 | CI·script | CI `CLI package output checker self-test`·`CLI package output is complete`·`CLI test report gate` |
| S4-05-D | NodeVault canonical proto/version drift가 생긴 fixture | CI·script | CI `Proto provenance (pinned producer)`·`Proto provenance script self-test` (`scripts/verify-proto-provenance.py`) |
| S4-05-E | 최종 successful CI run 검토 | CI·script | CI `Build · Test` main push run과 artifact(`cli-test-report`, `published-cli-transcript`, `coverage`, `cli-package`) |
| S4-06-A | clean user가 docs local quickstart 명령만 따라감 | 문서 | [`NODEKIT_CLI_RELEASE.md`](NODEKIT_CLI_RELEASE.md) §3 빠른 시작 |
| S4-06-B | docs의 dependency/payload/status 설명을 current code와 비교 | 문서 | [`NODEKIT_CLI_USAGE.md`](NODEKIT_CLI_USAGE.md) §1·§9, [`NODEKIT_CLI_RELEASE.md`](NODEKIT_CLI_RELEASE.md) §6 |
| S4-06-C | A/B/C1/C2 및 반환 tool_function_digest/cas_hash/presentation_revision_id/version의 역할 설명을 검토 | 문서 | [`NODEKIT_CLI_USAGE.md`](NODEKIT_CLI_USAGE.md) §9 식별자 표, [`NODEKIT_CLI_RELEASE.md`](NODEKIT_CLI_RELEASE.md) §6 |
| S4-06-D | release checklist/evidence manifest review | 문서 | [`NODEKIT_CLI_RELEASE.md`](NODEKIT_CLI_RELEASE.md) §8 release checklist·evidence manifest |
| S4-06-E | CLI 단독 release completion claim review | 문서 | [`NODEKIT_CLI_RELEASE.md`](NODEKIT_CLI_RELEASE.md) §9, [`NODEKIT_CLI_USAGE.md`](NODEKIT_CLI_USAGE.md) §9 |
