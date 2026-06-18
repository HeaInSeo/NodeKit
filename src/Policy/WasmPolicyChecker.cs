using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Wasmtime;

namespace NodeKit.Policy
{
    /// <summary>
    /// OPA WASM ABI를 통해 DockGuard 정책을 실행하는 IPolicyChecker 구현체.
    /// PolicyBundle의 .wasm 바이트를 Wasmtime으로 로드하고
    /// eval-context API를 통해 Dockerfile을 검사한다.
    /// </summary>
    internal sealed class WasmPolicyChecker : IPolicyChecker, IDisposable
    {
        private readonly Engine _engine;
        private readonly Module _module;
        private bool _disposed;

        // builtin ID → 이름 매핑 (builtins() 함수 호출 결과)
        private Dictionary<int, string>? _builtinMap;

        public WasmPolicyChecker(PolicyBundle bundle)
        {
            ArgumentNullException.ThrowIfNull(bundle);

            _engine = new Engine();
            _module = Module.FromBytes(_engine, "dockguard", bundle.WasmBytes);
        }

        /// <inheritdoc/>
        public PolicyResult Check(string dockerfileContent)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // builtinMap 구성 (최초 1회, 별도 store로 bootstrap)
            if (_builtinMap == null)
            {
                _builtinMap = BootstrapBuiltinMap();
            }

            using var store = new Store(_engine);
            var memory = new Memory(store, 2, null, false);

            using var linker = new Linker(_engine);
            linker.Define("env", "memory", memory);

            string? abortMessage = null;

            // opa_abort: WASM이 패닉 시 호출
            linker.DefineFunction("env", "opa_abort", (int addrI32) =>
            {
                abortMessage = memory.ReadNullTerminatedString(addrI32);
            });

            // opa_builtinN: 정책에서 사용하는 내장 함수들
            // 시그니처: (id: i32, ctx: i32, arg...) → i32  (ctx는 사용하지 않음)
            linker.DefineFunction("env", "opa_builtin0", (int id, int ctx1) => HandleBuiltin(memory, id));
            linker.DefineFunction("env", "opa_builtin1", (int id, int ctx1, int a1) => HandleBuiltin(memory, id, a1));
            linker.DefineFunction("env", "opa_builtin2", (int id, int ctx1, int a1, int a2) => HandleBuiltin(memory, id, a1, a2));
            linker.DefineFunction("env", "opa_builtin3", (int id, int ctx1, int a1, int a2, int a3) => HandleBuiltin(memory, id, a1, a2, a3));
            linker.DefineFunction("env", "opa_builtin4", (int id, int ctx1, int a1, int a2, int a3, int a4) => HandleBuiltin(memory, id, a1, a2, a3, a4));

            var instance = linker.Instantiate(store, _module);

            // --- Dockerfile → JSON input 직렬화 ---
            var instructions = DockerfileParser.Parse(dockerfileContent);
            var inputJson = OpaWasmHelpers.SerializeInstructions(instructions);

            // --- OPA eval-context API ---
            // 주의: eval(ctx) 는 void를 반환하는 구형 컨텍스트 API.
            // opa_eval(...)은 7개 파라미터를 받는 신형 API로 별도 호출 규약이 필요함.
            var opaJsonParse = instance.GetFunction<int, int, int>("opa_json_parse")!;
            var opaMalloc = instance.GetFunction<int, int>("opa_malloc")!;
            var opaJsonDump = instance.GetFunction<int, int>("opa_json_dump")!;
            var opaHeapPtrGet = instance.GetFunction<int>("opa_heap_ptr_get")!;
            var opaHeapPtrSet = instance.GetAction<int>("opa_heap_ptr_set")!;
            var opaEvalCtxNew = instance.GetFunction<int>("opa_eval_ctx_new")!;
            var opaEvalCtxSetInput = instance.GetAction<int, int>("opa_eval_ctx_set_input")!;
            var opaEvalCtxSetEntrypoint = instance.GetAction<int, int>("opa_eval_ctx_set_entrypoint")!;
            var evalFn = instance.GetFunction<int, int>("eval")!;     // int eval(ctx_addr)
            var opaEvalCtxGetResult = instance.GetFunction<int, int>("opa_eval_ctx_get_result")!;

            // 초기 heap pointer 저장
            var baseHeap = opaHeapPtrGet();

            // Input JSON → WASM 메모리
            var inputBytes = Encoding.UTF8.GetBytes(inputJson);
            var inputAddr = opaMalloc(inputBytes.Length);
            var span = memory.GetSpan(inputAddr, inputBytes.Length);
            inputBytes.CopyTo(span);

            // opa_json_parse: JSON 문자열 → OPA 값
            var inputVal = opaJsonParse(inputAddr, inputBytes.Length);
            if (inputVal == 0)
            {
                return new PolicyResult(new[] { new PolicyViolation("WASM-ERR", "입력 JSON 파싱 실패") });
            }

            // Eval context
            var ctx = opaEvalCtxNew();
            opaEvalCtxSetInput(ctx, inputVal);
            opaEvalCtxSetEntrypoint(ctx, 0); // 첫 번째 entrypoint

            var evalRc = evalFn(ctx);
            if (evalRc != 0 || abortMessage != null)
            {
                return new PolicyResult(
                    new[] { new PolicyViolation("WASM-ERR", $"eval 실패 (rc={evalRc}). {abortMessage}") });
            }

            // 결과 추출
            var resultVal = opaEvalCtxGetResult(ctx);
            var resultStrAddr = opaJsonDump(resultVal);
            var resultJson = memory.ReadNullTerminatedString(resultStrAddr);

            // heap 복구
            opaHeapPtrSet(baseHeap);

            return OpaWasmHelpers.ParseResult(resultJson);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_disposed)
            {
                _module.Dispose();
                _engine.Dispose();
                _disposed = true;
            }
        }

        // ─── 헬퍼 ────────────────────────────────────────────────────────────
        private int HandleBuiltin(Memory memory, int id, params int[] args)
        {
            if (_builtinMap == null || !_builtinMap.TryGetValue(id, out var name))
            {
                return 0; // 알 수 없는 builtin → null 반환
            }

            return name switch
            {
                "regex.match" when args.Length >= 2 => OpaWasmHelpers.BuiltinRegexMatch(memory, args[0], args[1]),
                "regex.is_valid" when args.Length >= 1 => OpaWasmHelpers.BuiltinRegexIsValid(memory, args[0]),
                _ => 0,
            };
        }

        private Dictionary<int, string> BootstrapBuiltinMap()
        {
            var map = new Dictionary<int, string>();
            try
            {
                using var store = new Store(_engine);
                var memory = new Memory(store, 2, null, false);
                using var linker = new Linker(_engine);
                linker.Define("env", "memory", memory);
                linker.DefineFunction("env", "opa_abort", (int addr) => { });
                linker.DefineFunction("env", "opa_builtin0", (int id, int ctx) => 0);
                linker.DefineFunction("env", "opa_builtin1", (int id, int ctx, int a1) => 0);
                linker.DefineFunction("env", "opa_builtin2", (int id, int ctx, int a1, int a2) => 0);
                linker.DefineFunction("env", "opa_builtin3", (int id, int ctx, int a1, int a2, int a3) => 0);
                linker.DefineFunction("env", "opa_builtin4", (int id, int ctx, int a1, int a2, int a3, int a4) => 0);

                var instance = linker.Instantiate(store, _module);
                var builtinsFn = instance.GetFunction<int>("builtins");
                if (builtinsFn == null)
                {
                    return map;
                }

                var builtinsPtr = builtinsFn();
                var opaJsonDump = instance.GetFunction<int, int>("opa_json_dump")!;
                var strAddr = opaJsonDump(builtinsPtr);
                var json = memory.ReadNullTerminatedString(strAddr);

                // json 형식: {"regex.match": 3, "regex.is_valid": 5, ...}
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    map[prop.Value.GetInt32()] = prop.Name;
                }
            }
#pragma warning disable CA1031
            catch
            {
                // ignore — empty map means all builtins return 0 (null)
            }
#pragma warning restore CA1031

            return map;
        }
    }
}
