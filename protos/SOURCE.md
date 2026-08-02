# 벤더링된 proto 출처

`protos/nodevault/v1/nodevault.proto`는 NodeVault 저장소에서 복사해 온 벤더 사본이다.
NodeKit은 이 사본으로 빌드하므로 NodeVault 체크아웃 없이 독립적으로 빌드된다.

| 항목 | 값 |
|------|-----|
| 출처 저장소 | `github.com/HeaInSeo/NodeVault` |
| 출처 경로 | `protos/nodevault/v1/nodevault.proto` |
| 기준 커밋 | `cd3f9e08a573573d23a46985fb76e588d56ac27e` |
| 복사 확인일 | 2026-08-02 (해당 커밋의 파일과 바이트 일치 확인) |

기준 커밋은 NodeVault 저장소 HEAD가 아니라 **그 파일을 마지막으로 변경한 커밋**이다
(`git -C <NodeVault> log -1 --format=%H -- protos/nodevault/v1/nodevault.proto`).

## 갱신 방법

NodeVault의 `protos/nodevault/v1/nodevault.proto`를 이 위치로 다시 복사하고, 위 표의
**기준 커밋**을 해당 파일을 마지막으로 변경한 커밋으로 함께 갱신한다.

## 드리프트 검증

현재 벤더 사본이 출처와 어긋났는지 자동으로 검증하는 CI는 **아직 없다**.
NodeVault 원본을 받아 비교하는 드리프트 검증은 별도 작업이며 canonical owner 확정
(`NodeVault#75`)과 함께 결정한다 — `5. proto-register` P-06·P-07 참조.
