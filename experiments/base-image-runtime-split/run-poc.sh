#!/usr/bin/env bash
set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="${ROOT_DIR}/out"
WORK_DIR="${ROOT_DIR}/work"
rm -rf "${OUT_DIR}" "${WORK_DIR}"
mkdir -p "${OUT_DIR}" "${WORK_DIR}"

RESULTS="${OUT_DIR}/results.tsv"
printf 'engine\ttool\tpackage\truntime\tbuild\trun_status\texit_code\tversion_match\tshell_present\tpackage_manager_present\trisky_helper_present\tnote\n' > "${RESULTS}"

log() { printf '[poc] %s\n' "$*"; }

resolve_pinned_image() {
  local ref="$1"
  log "pulling ${ref}" >&2
  if ! docker pull "${ref}" >&2; then
    return 1
  fi
  local pinned
  pinned="$(docker image inspect --format '{{index .RepoDigests 0}}' "${ref}" 2>/dev/null || true)"
  if [[ -z "${pinned}" || "${pinned}" == '<no value>' ]]; then
    return 1
  fi
  printf '%s' "${pinned}"
}

log 'Resolving all external base-image tags to immutable repo digests before building.'
MINIFORGE_PINNED="$(resolve_pinned_image 'condaforge/miniforge3:24.3.0-0')" || {
  echo 'failed to resolve Miniforge image' >&2; exit 2;
}
MICROMAMBA_PINNED="$(resolve_pinned_image 'mambaorg/micromamba:1.5.8')" || {
  echo 'failed to resolve Micromamba image' >&2; exit 2;
}
DEBIAN_PINNED="$(resolve_pinned_image 'debian:bookworm-slim')" || {
  echo 'failed to resolve Debian runtime image' >&2; exit 2;
}
DISTROLESS_PINNED="$(resolve_pinned_image 'gcr.io/distroless/base-debian12:nonroot')" || {
  echo 'failed to resolve Distroless runtime image' >&2; exit 2;
}

cat > "${OUT_DIR}/base-image-digests.txt" <<EOF
miniforge=${MINIFORGE_PINNED}
micromamba=${MICROMAMBA_PINNED}
debian_runtime=${DEBIAN_PINNED}
distroless_runtime=${DISTROLESS_PINNED}
EOF

make_dockerfile() {
  local engine="$1"
  local file="$2"
  if [[ "${engine}" == 'micromamba' ]]; then
    cat > "${file}" <<EOF
FROM ${MICROMAMBA_PINNED} AS builder
USER root
ARG PACKAGE
ENV MAMBA_ROOT_PREFIX=/opt/nodekit/mamba-root
RUN mkdir -p /opt/nodekit && \
    micromamba create -y -p /opt/nodekit/env -c conda-forge -c bioconda "\${PACKAGE}" && \
    micromamba list -p /opt/nodekit/env --explicit > /opt/nodekit/explicit.txt && \
    micromamba clean --all --yes

FROM ${DEBIAN_PINNED} AS runtime-debian
COPY --from=builder /opt/nodekit/env /opt/nodekit/env
USER 65532:65532

FROM ${DISTROLESS_PINNED} AS runtime-distroless
COPY --from=builder /opt/nodekit/env /opt/nodekit/env

FROM scratch AS runtime-scratch
COPY --from=builder /opt/nodekit/env /opt/nodekit/env
USER 65532:65532
EOF
  else
    cat > "${file}" <<EOF
FROM ${MINIFORGE_PINNED} AS builder
ARG PACKAGE
RUN mkdir -p /opt/nodekit && \
    conda create -y -p /opt/nodekit/env --override-channels -c conda-forge -c bioconda "\${PACKAGE}" && \
    conda list -p /opt/nodekit/env --explicit > /opt/nodekit/explicit.txt && \
    conda clean --all --yes

FROM ${DEBIAN_PINNED} AS runtime-debian
COPY --from=builder /opt/nodekit/env /opt/nodekit/env
USER 65532:65532

FROM ${DISTROLESS_PINNED} AS runtime-distroless
COPY --from=builder /opt/nodekit/env /opt/nodekit/env

FROM scratch AS runtime-scratch
COPY --from=builder /opt/nodekit/env /opt/nodekit/env
USER 65532:65532
EOF
  fi
}

tool_contract() {
  local tool="$1"
  case "${tool}" in
    bwa)
      PACKAGE='bwa=0.7.17'
      EXECUTABLE='/opt/nodekit/env/bin/bwa'
      EXPECTED_RC='1'
      EXPECTED_REGEX='Version: 0\.7\.17'
      ARGS=()
      ;;
    samtools)
      PACKAGE='samtools=1.20'
      EXECUTABLE='/opt/nodekit/env/bin/samtools'
      EXPECTED_RC='0'
      EXPECTED_REGEX='samtools 1\.20'
      ARGS=('--version')
      ;;
    bcftools)
      PACKAGE='bcftools=1.20'
      EXECUTABLE='/opt/nodekit/env/bin/bcftools'
      EXPECTED_RC='0'
      EXPECTED_REGEX='bcftools 1\.20'
      ARGS=('--version')
      ;;
    *) return 1 ;;
  esac
}

inspect_rootfs() {
  local image="$1"
  local stem="$2"
  local cid tarfile listing
  tarfile="${OUT_DIR}/${stem}.rootfs.tar"
  listing="${OUT_DIR}/${stem}.rootfs.txt"
  cid="$(docker create "${image}" /__nodekit_export_only__ 2>/dev/null)" || return 1
  docker export -o "${tarfile}" "${cid}" >/dev/null
  docker rm "${cid}" >/dev/null
  tar -tf "${tarfile}" > "${listing}"
  rm -f "${tarfile}"

  SHELL_PRESENT='NO'
  if grep -Eq '(^|/)(sh|bash|dash)$' "${listing}"; then SHELL_PRESENT='YES'; fi

  PACKAGE_MANAGER_PRESENT='NO'
  if grep -Eq '(^|/)(conda|mamba|micromamba)$' "${listing}"; then PACKAGE_MANAGER_PRESENT='YES'; fi

  RISKY_HELPER_PRESENT='NO'
  if grep -Eq '(^|/)(curl|wget|git|ssh|scp|apt|apt-get|apk|yum|dnf|gcc|g\+\+|clang|make|cmake)$' "${listing}"; then
    RISKY_HELPER_PRESENT='YES'
  fi
}

run_probe() {
  local image="$1"
  local log_file="$2"
  local rc
  set +e
  docker run --rm --entrypoint "${EXECUTABLE}" "${image}" "${ARGS[@]}" >"${log_file}" 2>&1
  rc=$?
  set -e 2>/dev/null || true
  RUN_RC="${rc}"
  VERSION_MATCH='NO'
  if grep -Eq "${EXPECTED_REGEX}" "${log_file}"; then VERSION_MATCH='YES'; fi
  RUN_STATUS='FAIL'
  if [[ "${RUN_RC}" == "${EXPECTED_RC}" && "${VERSION_MATCH}" == 'YES' ]]; then RUN_STATUS='PASS'; fi
}

for engine in micromamba conda; do
  for tool in bwa samtools bcftools; do
    tool_contract "${tool}"
    combo="${engine}-${tool}"
    dockerfile="${WORK_DIR}/Dockerfile.${combo}"
    make_dockerfile "${engine}" "${dockerfile}"

    builder_image="nodekit-poc:${combo}-builder"
    builder_log="${OUT_DIR}/${combo}.builder-build.log"
    log "building ${combo} builder (${PACKAGE})"
    if ! docker build --progress=plain --target builder --build-arg "PACKAGE=${PACKAGE}" -f "${dockerfile}" -t "${builder_image}" "${WORK_DIR}" >"${builder_log}" 2>&1; then
      for runtime in debian distroless scratch; do
        printf '%s\t%s\t%s\t%s\tFAIL\tNA\tNA\tNA\tNA\tNA\tNA\tbuilder build failed; see %s\n' \
          "${engine}" "${tool}" "${PACKAGE}" "${runtime}" "$(basename "${builder_log}")" >> "${RESULTS}"
      done
      continue
    fi

    # Preserve solver-selected exact package/build URLs as evidence.
    cid="$(docker create "${builder_image}" /__nodekit_export_only__)"
    docker cp "${cid}:/opt/nodekit/explicit.txt" "${OUT_DIR}/${combo}.explicit.txt" >/dev/null 2>&1 || true
    docker rm "${cid}" >/dev/null

    for runtime in debian distroless scratch; do
      target="runtime-${runtime}"
      runtime_image="nodekit-poc:${combo}-${runtime}"
      runtime_build_log="${OUT_DIR}/${combo}.${runtime}.runtime-build.log"
      probe_log="${OUT_DIR}/${combo}.${runtime}.probe.log"
      stem="${combo}.${runtime}"

      log "building ${combo} -> ${runtime}"
      if ! docker build --progress=plain --target "${target}" --build-arg "PACKAGE=${PACKAGE}" -f "${dockerfile}" -t "${runtime_image}" "${WORK_DIR}" >"${runtime_build_log}" 2>&1; then
        printf '%s\t%s\t%s\t%s\tFAIL\tNA\tNA\tNA\tNA\tNA\tNA\truntime build failed; see %s\n' \
          "${engine}" "${tool}" "${PACKAGE}" "${runtime}" "$(basename "${runtime_build_log}")" >> "${RESULTS}"
        continue
      fi

      SHELL_PRESENT='UNKNOWN'
      PACKAGE_MANAGER_PRESENT='UNKNOWN'
      RISKY_HELPER_PRESENT='UNKNOWN'
      if ! inspect_rootfs "${runtime_image}" "${stem}"; then
        SHELL_PRESENT='INSPECT_FAIL'
        PACKAGE_MANAGER_PRESENT='INSPECT_FAIL'
        RISKY_HELPER_PRESENT='INSPECT_FAIL'
      fi

      RUN_RC='NA'
      VERSION_MATCH='NO'
      RUN_STATUS='FAIL'
      run_probe "${runtime_image}" "${probe_log}"

      note='direct executable+argv probe'
      if [[ "${runtime}" == 'scratch' && "${RUN_STATUS}" == 'FAIL' ]]; then
        note='informative scratch failure; likely intrinsic loader/libc requirement'
      fi

      printf '%s\t%s\t%s\t%s\tPASS\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
        "${engine}" "${tool}" "${PACKAGE}" "${runtime}" "${RUN_STATUS}" "${RUN_RC}" "${VERSION_MATCH}" \
        "${SHELL_PRESENT}" "${PACKAGE_MANAGER_PRESENT}" "${RISKY_HELPER_PRESENT}" "${note}" >> "${RESULTS}"
    done
  done
done

python3 - "${RESULTS}" "${OUT_DIR}/REPORT.md" <<'PY'
import csv, sys
from pathlib import Path
src, dst = map(Path, sys.argv[1:])
rows = list(csv.DictReader(src.open(), delimiter='\t'))

lines = [
    '# Base Tool Image builder → runtime split POC',
    '',
    'This report is experimental evidence, not a production contract.',
    '',
    '| Engine | Tool | Runtime | Build | Direct tool run | RC | Version | Shell in rootfs | Package manager in rootfs | Risky helper in rootfs | Note |',
    '|---|---|---|---|---|---:|---|---|---|---|---|',
]
for r in rows:
    vals = [r['engine'], r['tool'], r['runtime'], r['build'], r['run_status'], r['exit_code'],
            r['version_match'], r['shell_present'], r['package_manager_present'], r['risky_helper_present'], r['note']]
    vals = [v.replace('|', '\\|') for v in vals]
    lines.append('| ' + ' | '.join(vals) + ' |')

non_scratch = [r for r in rows if r['runtime'] in ('debian', 'distroless')]
distroless = [r for r in rows if r['runtime'] == 'distroless']
scratch = [r for r in rows if r['runtime'] == 'scratch']

all_runtime_runs = bool(non_scratch) and all(r['build'] == 'PASS' and r['run_status'] == 'PASS' for r in non_scratch)
all_pm_removed = bool(non_scratch) and all(r['package_manager_present'] == 'NO' for r in non_scratch)
all_distroless_shellless = bool(distroless) and all(r['shell_present'] == 'NO' for r in distroless)
all_distroless_runs = bool(distroless) and all(r['run_status'] == 'PASS' for r in distroless)
scratch_failures = sum(r['run_status'] == 'FAIL' for r in scratch)

lines += [
    '',
    '## Evidence summary',
    '',
    f'- Debian + Distroless direct tool probes all pass: **{all_runtime_runs}**',
    f'- Package manager binaries absent from Debian + Distroless final rootfs: **{all_pm_removed}**',
    f'- Distroless final rootfs is shell-less for every tested case: **{all_distroless_shellless}**',
    f'- Tools still run directly in shell-less Distroless images: **{all_distroless_runs}**',
    f'- Scratch direct-run failures: **{scratch_failures}/{len(scratch)}** (informative baseline, not automatically a defect)',
    '',
    '## Interpretation guardrail',
    '',
    '- A PASS proves only that the pinned tool can be installed in a builder, copied at the same prefix, and minimally invoked in the selected runtime base.',
    '- It does **not** prove real fixture/input/output functional correctness; that belongs to ToolFunctionSpec validation.',
    '- The final production runtime profile still requires policy/ABI/relocation decisions and broader tool coverage.',
]

dst.write_text('\n'.join(lines) + '\n')
PY

cat "${OUT_DIR}/REPORT.md"

# The POC intentionally reports experimental failures in artifacts instead of
# hiding them behind a failing workflow. Infrastructure setup errors above still
# exit non-zero; per-combination compatibility results are evidence.
exit 0
