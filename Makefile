.PHONY: policy build test all

OPA       ?= $(HOME)/bin/opa
DOCKGUARD := ../DockGuard/policy/dockerfile
ASSETS    := assets/policy

# DockGuard .rego → dockguard.wasm
policy:
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
