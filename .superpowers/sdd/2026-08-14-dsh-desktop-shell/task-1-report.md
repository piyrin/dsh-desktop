# Task 1 report: test harness, paths, and startup animation policy

## Implementation

- Added a dependency-free C# test runner with case-insensitive `--filter` support and `True`, `False`, and generic `Equal` assertions.
- Added the five specified startup-policy tests.
- Added `desktop/test.ps1`, compiling the test files and explicitly listed pure source files with the .NET Framework 4 compiler. It does not include `NoWindowHelper.cs`.
- Added `AppPaths.Create`, deriving local state, logs, cache, WebView2 data, and the published runtime bootstrap path without filesystem access.
- Added the four `OpenReason` values and the three-second startup animation policy.

## TDD evidence

RED command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
```

Result (expected): exit code `1`; compiler reported missing `E:\dsh\desktop\src\AppPaths.cs` and `E:\dsh\desktop\src\StartupPolicy.cs`, so the tests could not compile before production implementation.

GREEN command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop\test.ps1
```

Result: exit code `0`; `5 passed, 0 failed`.

## Files changed

- `desktop/src/AppPaths.cs`
- `desktop/src/StartupPolicy.cs`
- `desktop/tests/TestRunner.cs`
- `desktop/tests/StartupPolicyTests.cs`
- `desktop/test.ps1`

## Self-review

- The policy uses a strict `elapsed > 3 seconds` threshold, suppresses animation when the backend is ready, and suppresses tray/secondary activation animation.
- Path construction uses only `Path.Combine` and object initialization; it does not create directories or inspect the filesystem.
- The test script uses the required Framework64 compiler and forwards an optional filter.

## Concerns

- `AppPaths` path member visibility is `internal`, matching the dependency-free internal test assembly pattern; later production tasks should remain in the same assembly or adjust visibility deliberately.
