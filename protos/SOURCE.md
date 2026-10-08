# 벤더링된 proto 출처

`protos/nodevault/v1/nodevault.proto`는 NodeVault 저장소에서 복사해 온 벤더 사본이다.
NodeKit은 이 사본으로 빌드하므로 NodeVault 체크아웃 없이 독립적으로 빌드된다.

**기계 판독 정본은 [`provenance.json`](provenance.json)이다.** 출처 저장소·경로·불변 커밋,
producer git blob/SHA-256, consumer SHA-256, 생성기(Grpc.Tools)·런타임(Google.Protobuf) 버전과
이 proto를 컴파일하는 프로젝트 목록을 담는다. 아래 표는 사람용 요약이며 두 값이 어긋나면 CI가 실패한다.

| 항목 | 값 |
|------|-----|
| 출처 저장소 | `github.com/HeaInSeo/NodeVault` |
| 출처 경로 | `protos/nodevault/v1/nodevault.proto` |
| 기준 커밋 | `912b23c333ca17b80f4ea3f910681209467264a6` |
| 복사 확인일 | 2026-09-28 (해당 커밋의 파일과 바이트 일치 확인) |

## 호환성 메모 (912b23c)

이 기준 커밋에서 `RegisteredToolDefinition`의 `inputs`(12)·`outputs`(13)·`display`(14)·`command`(17)가
`[deprecated = true]`가 되었고 `ToolFunctionSpec`/`ToolFunctionPresentation` 계열 메시지와
`RegisterToolFunction` RPC가 추가되었다. NodeKit은 기존 label/category 표시를 위해 legacy `display`를
`src/Grpc/GrpcToolRegistryClient.cs`의 **두 call-site에서만** CS0612 억제로 계속 읽는다.
W4 `ToolFunctionPresentation` projection이 이 읽기를 대체하면 두 억제를 제거한다.

기준 커밋은 NodeVault 저장소 HEAD가 아니라 **그 파일을 마지막으로 변경한 커밋**이다
(`git -C <NodeVault> log -1 --format=%H -- protos/nodevault/v1/nodevault.proto`).

## 갱신 방법

한 커밋에서 함께 바꾼다(원자적 pin 갱신).

1. NodeVault의 `protos/nodevault/v1/nodevault.proto`를 기준 커밋에서 이 위치로 다시 복사한다.
2. `provenance.json`의 `producer.revision`(전체 40자리 커밋), `producer.gitBlobSha1`,
   `producer.sha256`, `consumerSha256`를 갱신한다
   (`git hash-object <파일>`, `sha256sum <파일>`). 벤더 사본은 producer와 바이트가 같아야 한다.
3. 위 표의 **기준 커밋**을 같은 값으로 갱신한다.
4. `python3 scripts/verify-proto-provenance.py --fetch-producer`로 확인한다.

## 검증

- **필수 CI** (`verify.yml`): `scripts/verify-proto-provenance.py --fetch-producer`가 consumer 바이트 ↔
  manifest ↔ 고정 커밋의 producer 바이트를 대조하고, 모든 `<Protobuf Include>`가 manifest에
  선언됐는지와 생성기 버전 일치를 확인한다. producer는 **고정 커밋 URL로만** 받는다. NodeVault
  `main`이 움직여도 바뀌지 않은 NodeKit 커밋의 판정은 그대로다. 네트워크 실패는 실패로 처리한다.
- **공식 빌드 가드** (`Directory.Build.targets`): CI 환경·`ContinuousIntegrationBuild=true`·Release 구성에서는
  컴파일되는 proto의 SHA-256이 manifest의 `consumerSha256`과 같아야 한다. `/p:ApiProtosRoot=`로
  선언되지 않은 바이트를 넣으면 Protobuf 컴파일 전에 `NKPROTO002`로 실패한다. 로컬 Debug 빌드는 막지 않는다.
  manifest 경로는 `/p:`로 바꿀 수 없다. `sources[]` 밖에 `consumerSha256`이 더 있으면 `NKPROTO003`으로 실패한다.
- manifest 스키마는 닫혀 있다. 알 수 없는 키, 중복 `consumerSha256`, 검증된 source에 속하지 않은
  `"consumerSha256": "<hex>"` 텍스트는 필수 CI에서 실패한다. 가드는 이 텍스트만 보므로 검증된 digest만 증명이 된다.
- 최신 NodeVault `main`과의 호환성 관찰은 필수 판정이 아니다(필요하면 별도 비필수 작업으로 둔다).
