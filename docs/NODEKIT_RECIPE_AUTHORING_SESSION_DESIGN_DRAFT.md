# NodeKit Recipe Authoring Session — DESIGN DRAFT (not implemented)

> **Status: design only, nothing in this document is implemented yet.**
> No `RecipeFieldDescriptor`, `RecipeFieldCatalog`, or `RecipeAuthoringSession`
> type exists in code. This draft is for review before any of it is built.
> It builds on the already-implemented model described in
> [`NODEKIT_CLI_RECIPE_SPEC_DRAFT.md`](NODEKIT_CLI_RECIPE_SPEC_DRAFT.md)
> (`RecipeDocument`, `RecipeValidator`, `RecipeRenderer`, the `validate`/`render`
> CLI commands) and does not change any of it.

## 0. Why this draft exists

`recipe.json` today is hand-authored JSON only — there is no scaffolding or
authoring command, only `nodekit validate` / `nodekit render` (see
[`NODEKIT_CLI_USAGE.md`](NODEKIT_CLI_USAGE.md) §2/§3). The direction under
discussion is to make recipe **authoring itself** — not just validation — a
first-class NodeKit surface, driven through three different front ends over
time:

1. **CLI wizard** (near-term): an interactive `nodekit` command that asks for
   a 종류(kind — `RecipeVariant`; see
   [`feedback_terminology_variant`](#) memory note: call these "종류" in
   conversation/docs prose, never "variant", since "variant" collides with
   genomics terminology) and then walks through that kind's fields one at a
   time. Some fields may be supplied non-interactively via flags.
2. **Future GUI**: the same authoring flow surfaced as Avalonia/ReactiveUI
   forms.
3. **Future MCP server**: the same authoring flow exposed as MCP tool calls,
   so an LLM can drive it from natural language.

If the field-by-field authoring logic is written directly as terminal
`Console.ReadLine()` prompts inside `NodeKit.Cli`, none of it transfers to (2)
or (3) — each front end would reimplement the same per-kind field knowledge.
This draft proposes a single, I/O-free authoring engine that all three front
ends drive identically.

## 1. Boundary check (CLAUDE.md §1/§6)

This is authoring logic for `RecipeDocument`, which CLAUDE.md §1 already
assigns to NodeKit ("ToolDefinition authoring (UI forms, field validation)").
Nothing here touches K8s, image builds, registry push, or gRPC submission.
The engine lives under `src/Authoring/Recipes/` alongside the existing
`RecipeDocument`/`RecipeRenderer` — pure POCO logic with zero NuGet
dependencies, so `NodeKit.Cli.csproj`'s existing source-link pattern (see
spec draft §6) can pick up the new files exactly like the 18 it already
links.

## 2. Core types

```text
src/Authoring/Recipes/
  RecipeFieldDescriptor.cs   (new)
  RecipeFieldCatalog.cs      (new)
  RecipeAuthoringSession.cs  (new)
```

### 2.1 `RecipeFieldDescriptor`

One entry per field, carrying enough metadata for any front end to render a
prompt and apply a value without knowing `RecipeDocument`'s shape in advance:

```csharp
internal enum RecipeFieldType { Scalar, StringList, InputList, OutputList }

internal sealed record RecipeFieldDescriptor(
    string Name,                                   // matches RecipeDocument property name
    RecipeFieldType Type,
    bool Required,
    string PromptKo,                                // prompt text shown to the author
    Action<RecipeDocument, object> Apply,
    Func<object, ValidationViolation?>? QuickValidate = null);
```

`QuickValidate` is an optional single-field check for immediate feedback
(e.g. `SourceChecksum` must match `^sha256:[0-9a-fA-F]{64}$`). It is **not**
a replacement for full validation — see §4.

### 2.2 `RecipeFieldCatalog`

A static, pure data table — the field lists from
[`NODEKIT_CLI_USAGE.md`](NODEKIT_CLI_USAGE.md) §3 ("공통 필드" / "종류별
추가 필드") expressed as descriptors instead of prose:

```csharp
internal static class RecipeFieldCatalog
{
    public static IReadOnlyList<RecipeFieldDescriptor> CommonFields { get; }
    public static IReadOnlyDictionary<RecipeVariant, IReadOnlyList<RecipeFieldDescriptor>> VariantFields { get; }
    public static IReadOnlyList<RecipeFieldDescriptor> FieldsFor(RecipeVariant variant);
}
```

`FieldsFor(variant)` returns `CommonFields` followed by that variant's extra
fields, in the same order the usage doc's tables already list them.

### 2.3 `RecipeAuthoringSession`

The state machine. No console I/O, no file I/O:

```csharp
internal sealed class RecipeAuthoringSession
{
    public bool IsVariantSelected { get; }
    public void SelectVariant(RecipeVariant variant);

    public RecipeFieldDescriptor? NextField();   // next unfilled field, null when none remain

    public IReadOnlyList<ValidationViolation> SetField(string fieldName, object value);

    // List-typed fields (Inputs/Outputs/Packages/Channels/SourceBuildCommands)
    // are filled by repeated AppendListItem calls, then closed off explicitly —
    // a single SetField call can't represent "add one more item?" loops.
    public IReadOnlyList<ValidationViolation> AppendListItem(string fieldName, object item);
    public void CompleteListField(string fieldName);

    public bool IsComplete { get; }
    public RecipeDocument Build();
}
```

`SetField`/`AppendListItem` run only that field's `QuickValidate` (if any)
and, on success, advance `NextField()`. They never run `RecipeValidator` or
the L1 chain — that stays a single final gate (§4).

## 3. List-typed fields

`Inputs`/`Outputs` are lists of small objects (`ToolInput { Name, Role,
Format, Shape }`, `ToolOutput { ..., Class }`), not scalars. Modeling each
item as its own mini field-set (rather than asking the author to type a
single JSON blob) needs the same descriptor idea one level down:

```csharp
internal static class ToolInputFieldCatalog
{
    public static IReadOnlyList<RecipeFieldDescriptor> Fields { get; } // Name, Role, Format, Shape
}
```

A front end fills one `ToolInput`'s fields via this sub-catalog, then calls
`session.AppendListItem("Inputs", builtInput)`, then either repeats (add
another input) or calls `session.CompleteListField("Inputs")` once the
author signals they're done. `StringList` fields (`Packages`, `Channels`,
`SourceBuildCommands`) follow the same append/complete shape but with a bare
`string` as the item type instead of a sub-catalog.

## 4. Validation boundary — unchanged final gate

The session's per-field `QuickValidate` exists only for immediate UX
feedback (e.g. reject an obviously malformed checksum before the author
moves on). It intentionally does **not** duplicate `RecipeValidator` or the
L1 chain. Once `IsComplete` is true:

```text
RecipeDocument doc = session.Build();
violations = ValidateRecipe(doc);   // existing helper: RecipeValidator + RecipeRenderer + L1 chain
```

If `violations` is non-empty, nothing is written — same fail-closed rule
`render` already follows (spec draft §5). This means the wizard can never
produce a `recipe.json` that `nodekit validate` would then reject; the two
code paths share one validation gate, not two.

## 5. Front ends

### 5.1 CLI wizard (near-term, this draft's actual deliverable)

Provisional command name `nodekit create <recipe.json>` (placeholder, not
decided — could also be `author`/`new`). Flow:

1. Print the six kinds with a one-line description each, read a selection,
   call `session.SelectVariant(...)`.
2. Apply any pre-supplied flags (e.g. `--tool-name bwa`) via `SetField`
   before the prompt loop starts — fields already set this way are skipped
   by `NextField()`, so flag-supplied fields and prompted fields share one
   code path. This is how "일부 필드만 비대화형" falls out of the design
   without a separate non-interactive mode.
3. Loop: `NextField()` → print `PromptKo` → read input → `SetField`/
   `AppendListItem`/`CompleteListField` as appropriate → repeat until
   `IsComplete`.
4. Run the final validation gate (§4). On success, write `recipe.json` to
   `--out` (or stdout for `-`, consistent with `render`). On failure, print
   violations to stderr and exit 1 without writing — same convention as
   `validate`/`render`.

### 5.2 Future GUI (not built in this pass)

A ReactiveUI ViewModel wraps `RecipeAuthoringSession`, exposing the current
`RecipeFieldDescriptor` as an observable property and binding form controls
by `RecipeFieldType`. No changes to the session API are anticipated for
this — flagged here only so the session's shape is reviewed with this
consumer in mind, not implemented now.

### 5.3 Future MCP server (not built in this pass)

Two MCP tools: one returns the current field's descriptor, the other applies
a value. The calling LLM maps natural language to field values; no NLU code
lives in NodeKit. Flagged for the same reason as 5.2 — review only.

## 6. Testing plan

`RecipeAuthoringSessionTests` (new, alongside `RecipeValidatorTests`/
`RecipeRendererTests`):

- One happy-path test per kind (6 total): select variant → walk
  `NextField()` in order → fill every field → `IsComplete == true` →
  `Build()` produces a document that passes `ValidateRecipe` with zero
  violations.
- Negative cases: `SetField`/`AppendListItem` with a `QuickValidate`-failing
  value (e.g. malformed `SourceChecksum`) returns a violation and does not
  advance `NextField()`.
- `RecipeFieldCatalogTests`: `FieldsFor` returns common fields before
  variant-specific fields, and the field sets match
  [`NODEKIT_CLI_USAGE.md`](NODEKIT_CLI_USAGE.md) §3's tables exactly (so the
  catalog and the usage doc don't silently drift apart).

## 7. Explicitly out of scope for the first implementation pass

- The GUI ViewModel and MCP server themselves (§5.2/§5.3) — only the session
  API shape is being reviewed with them in mind.
- Renaming `RecipeVariant`/`Variant` to anything containing "kind" — the C#
  symbol stays as-is; only conversational/doc prose uses "종류".
- Any change to `RecipeValidator`, `RecipeRenderer`, or the L1 validator
  chain — the session is a new caller of `ValidateRecipe`, not a
  replacement for any part of it.
- `nodekit submit` or any gRPC/NodeVault interaction — unaffected by this
  draft, still not specified (spec draft §6).

## 8. Open questions for review

1. Final command name — `create`, `author`, `new`, something else?
2. Should the wizard allow going back to a previously-filled field (e.g.
   "이전 필드로" / 재입력), or is forward-only with a final
   review-before-write step sufficient for v1?
3. Should `QuickValidate` cover anything beyond format checks already known
   today (checksum regex, non-empty required string), or stay minimal and
   let the final gate (§4) catch everything else?
4. Is `RecipeFieldType.InputList`/`OutputList` worth a generic
   sub-catalog mechanism (§3) now, or is hand-written field-by-field code
   for just these two cases acceptable for v1, with genericization deferred
   until a third nested-list field actually appears?

## 9. 한국어 요약

이 문서는 **설계만 있고 구현은 전혀 없는** 초안이다. `recipe.json`은 지금
수작업 JSON 작성만 가능하고(`nodekit validate`/`render`만 존재), 저작
자체를 CLI 마법사로 만들고 이후 GUI/MCP로 확장하는 방향을 검토 중이다.

핵심 제안: 필드 하나하나를 터미널 I/O로 직접 짜지 않고, I/O 없는 순수
상태 머신(`RecipeAuthoringSession`)과 그 뒤를 받치는 선언적 필드
테이블(`RecipeFieldCatalog`)을 만들어서 CLI 마법사/향후 GUI/향후 MCP
서버가 전부 같은 엔진을 쓰게 한다. 세션은 필드 단위 즉석 검사
(`QuickValidate`)만 하고, 최종 검증은 기존 `ValidateRecipe`(RecipeValidator
+ RecipeRenderer + L1 체인)를 그대로 한 번 더 돌리는 단일 게이트로 유지한다
— `render`가 이미 따르는 fail-closed 원칙과 동일.

리스트형 필드(`Inputs`/`Outputs`/`Packages`/`Channels`/
`SourceBuildCommands`)는 한 번에 `SetField`로 못 넣고
`AppendListItem`/`CompleteListField`로 "추가→추가→완료" 흐름을 따로
모델링한다.

이번 패스에서 실제로 만드는 건 CLI 마법사뿐이고, GUI ViewModel과 MCP
서버는 세션 API 모양만 검토 대상이며 구현하지 않는다.
`RecipeVariant`/`RecipeValidator`/`RecipeRenderer`/`nodekit submit`은
이 초안으로 전혀 변경되지 않는다.

8절의 4가지 질문(명령어 이름, 뒤로가기 지원 여부, QuickValidate 범위,
InputList/OutputList 제네릭화 시점)이 리뷰에서 결정되어야 구현을 시작할
수 있다.
