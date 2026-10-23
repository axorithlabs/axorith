# AGENTS.md

- Do not use Git commands to undo or discard changes. In particular, do not use `git checkout`, `git restore`, `git reset`, `git revert`, or any other Git command for rollback or deletion. Edit files directly and preserve unrelated changes.
- If a bug-fix attempt does not fix the bug, first remove only the code introduced by that failed attempt. Then try a different fix; do not layer it on top of the failed attempt. Preserve unrelated changes.
- Functional tests and benchmarks may be added and retained. Functional tests must run real production flows and assert observable effects (for example, persistence, process launch, blocking, or Host/Client sync). Benchmarks must measure real operations.
- Never replace the component whose behavior is under test with a mock/fake that hard-codes success; reject tautologies and implementation-detail checks.
- Tests may be created and run against real code to reproduce bugs, verify fixes, or investigate other software errors. After verification is complete, these tests MUST be deleted, together with fixtures and CI steps added solely for them.
- Preserve pre-existing tests unless an audit finds they have no behavioral value.
- When auditing tests, inspect every test file and test; remove tests with no behavioral value and add coverage for missing features.
- Do not create audit or report files (for example, `REPORT.md`, `AUDIT.md`, or similar) unless the user explicitly requests them.
- Keep build outputs and logs in the existing directories. Do not change output, log, or build directory settings or add task-specific build configurations/directories; use the existing `build/Debug` and `build/Release`, not paths such as `build/AuditBugs`.
- Write commit subjects in Conventional Commit format: `<type>(<scope>): <imperative summary>`. Omit the scope when it adds no useful context. Use `feat` for new functionality, `fix` for bug fixes, and `chore` for maintenance or documentation; use other standard types as appropriate.
- Do not create commits unless the user explicitly asks you to commit in the current conversation.
- Before running `dotnet build` or `dotnet test`, disable Avalonia build telemetry for that command/session by setting `AVALONIA_TELEMETRY_OPTOUT=1` (in PowerShell: `$env:AVALONIA_TELEMETRY_OPTOUT = '1'`). Do not change the user's persistent environment.
