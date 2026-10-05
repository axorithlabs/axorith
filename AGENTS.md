# AGENTS.md

- Do not use Git commands to undo or discard changes. In particular, do not use `git checkout`, `git restore`, `git reset`, `git revert`, or any other Git command for rollback or deletion. Edit files directly and preserve unrelated changes.
- If a bug-fix attempt does not fix the bug, first remove only the code introduced by that failed attempt. Then try a different fix; do not layer it on top of the failed attempt. Preserve unrelated changes.
- Every feature except the Settings page must have at least one retained automated test that runs the real production flow and asserts its observable effect (for example, persistence, process launch, blocking, or Host/Client sync). Benchmarks must measure real operations.
- Never replace the component whose behavior is under test with a mock/fake that hard-codes success; reject tautologies and implementation-detail checks. Keep regression tests only when they protect a stable behavior contract, not as one-off checks for resolved bugs.
- For feature work, bug fixes, and software errors, write and run a minimal automated test against real code. Remove temporary tests used only to verify the change after verification; retain durable feature-coverage and useful regression tests. Preserve pre-existing tests unless the audit finds they have no behavioral value. Keep tests the user explicitly asks to add.
- When auditing tests, inspect every test file and test; remove tests with no behavioral value and add coverage for missing features.
- Write commit subjects in Conventional Commit format: `<type>(<scope>): <imperative summary>`. Omit the scope when it adds no useful context. Use `feat` for new functionality, `fix` for bug fixes, and `chore` for maintenance or documentation; use other standard types as appropriate.
- Do not create commits unless the user explicitly asks you to commit in the current conversation.
- Before running `dotnet build` or `dotnet test`, disable Avalonia build telemetry for that command/session by setting `AVALONIA_TELEMETRY_OPTOUT=1` (in PowerShell: `$env:AVALONIA_TELEMETRY_OPTOUT = '1'`). Do not change the user's persistent environment.
