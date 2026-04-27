.PHONY: policy build test all publish-linux

OPA          ?= $(HOME)/bin/opa
DOCKGUARD    ?= $(CURDIR)/../DockGuard/policy/dockerfile
ASSETS       := assets/policy
PUBLISH_OUT  := publish/linux-x64

# DockGuard .rego → dockguard.wasm
policy:
	@test -d "$(DOCKGUARD)" || (echo "DockGuard policy directory not found: $(DOCKGUARD)"; \
	  echo "Set DOCKGUARD=/path/to/DockGuard/policy/dockerfile and retry."; exit 1)
	@echo "==> opa build $(DOCKGUARD)"
	@mkdir -p $(ASSETS)
	@cd /tmp && \
	  $(OPA) build -t wasm -e dockerfile/multistage/deny $(DOCKGUARD) -o /tmp/_dg_bundle.tar.gz && \
	  tar xzf /tmp/_dg_bundle.tar.gz policy.wasm && \
	  cp policy.wasm $(CURDIR)/$(ASSETS)/dockguard.wasm && \
	  rm -f /tmp/_dg_bundle.tar.gz /tmp/policy.wasm /tmp/data.json /tmp/.manifest
	@echo "✅ $(ASSETS)/dockguard.wasm 생성 완료"

build: policy
	dotnet build NodeKit.sln

test:
	dotnet test NodeKit.sln

all: build test

# Ubuntu (linux-x64) 배포 빌드
# 출력: publish/linux-x64/  → rsync/scp로 대상 장비에 복사 후 ./NodeKit 실행
# .NET 런타임 불필요 (self-contained)
publish-linux:
	dotnet publish NodeKit.csproj \
	  -c Release \
	  -r linux-x64 \
	  --self-contained true \
	  -o $(PUBLISH_OUT)
	@echo ""
	@echo "✅ 배포 완료: $(PUBLISH_OUT)/"
	@echo ""
	@echo "  배포 방법:"
	@echo "    rsync -av --delete $(PUBLISH_OUT)/ user@host:~/nodekit/"
	@echo "    ssh user@host 'chmod +x ~/nodekit/NodeKit && ~/nodekit/NodeKit'"
