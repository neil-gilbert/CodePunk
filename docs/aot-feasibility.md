# Native AOT & Trimming Feasibility

## Scope
Assess readiness of `CodePunk.Console` for Native AOT and aggressive trimming on .NET 9.

## Key Dependencies
| Component | Concern | Expected AOT/Trim Status | Mitigation |
|-----------|---------|---------------------------|------------|
| Spectre.Console | Reflection for type discovery minimal | Generally AOT-safe | Monitor for styling API usage |
| System.CommandLine (beta) | Parser model reflection | Mostly safe, low dynamic | Keep to standard binding patterns |
| Serilog + Settings.Configuration | Reflection over configuration + dynamic sink loading | Risky for trimming, AOT may strip sinks | Provide explicit code-based logger config in AOT mode |
| Serilog.Sinks.File/Console | File sink uses I/O, console uses coloring; both fine | AOT-safe if preserved | Preserve types via references |
| OpenTelemetry.* | Instrumentation & exporters may use reflection | Potential trimming warnings | Optionally disable OT in AOT or root required types |
| EFCore (Data project) | Dynamic model building & reflection | High trimming risk | Data layer not needed for basic CLI? Consider feature flag |

## Observed Dynamic Patterns
- Minimal dynamic type inspection: only occurrences of `GetType().Name` for diagnostics.
- No `Activator.CreateInstance`, `Assembly.Load`, or `Type.GetType` usages in console path.
- Serilog Configuration JSON triggers sink discovery via assembly scanning.

## Proposed AOT Strategy
1. Baseline: Ship non-AOT self-contained builds (already planned).
2. Introduce `AOT` build flavor:
   - Publish with: `dotnet publish -c Release -r <rid> /p:PublishAot=true /p:StripSymbols=true`.
   - Exclude or minimize Serilog JSON config usage (replace with explicit pipeline in code behind `#if AOT`).
   - Optionally disable OpenTelemetry (or supply minimal console exporter only) behind `AOT` symbol.
3. Trim Mode: Start with default (partial). Upgrade to full only after audit.
4. EFCore & Data: If CLI requires database, AOT may enlarge size; consider an `--offline` or mock path for AOT variant.

## Mitigations & Code Changes
- Add conditional compilation symbol via PropertyGroup Condition when `PublishAot` true: `<DefineConstants>$(DefineConstants);AOT</DefineConstants>`.
- In `Program.cs`, wrap Serilog configuration block; for AOT use explicit `LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();`.
- Provide environment variable toggle to disable OpenTelemetry: skip builder.Services.AddOpenTelemetry() in AOT.
- Rooting file (only if needed): create `ILLink.Descriptors.xml` listing critical Serilog sink types to preserve. Keep this minimal.

## Risk Matrix
| Risk | Impact | Likelihood | Action |
|------|--------|------------|--------|
| Serilog config trimming removes sinks | Logging partially missing | Medium | Code-based config in AOT |
| OpenTelemetry exporter trimmed | Metrics disabled silently | Medium | Explicit toggle or removal |
| Increased binary size for EFCore | Larger distribution | High | Consider excluding DB features in AOT |
| AOT publish failure on macOS arm64 | Build break | Low/Med | Start with linux-x64 first |

## Pilot Plan
1. Implement conditional code (logging + optional telemetry skip) – no functional change for standard builds.
2. Add separate `publish-aot` job for `linux-x64` only.
3. Validate runtime: run `--help`, `plan generate` basic command, measure startup time.
4. Expand matrix to win-x64, osx-arm64 after success.

## Acceptance Criteria
- AOT linux-x64 binary runs primary commands without exception.
- Size reduction vs IL-only self-contained OR startup improvement >30%.
- No trimming warnings (IL2026/ILxxxx) unaddressed in build logs.

## Next Steps
- Implement minimal trimming profile (non-AOT) with warnings surfaced.
- Introduce conditional logging pipeline.
- Add initial AOT workflow job (linux-x64).
