# Networking verification entry point

From the repository root, run:

```powershell
pwsh -NoProfile -File tools/net-test/verify.ps1
```

This runs the five standalone networking suites in separate fresh PowerShell 7 processes, retaining each JSON report and console log. No Unity editor, SDK installation, package restore, account or network is needed for the checks. The existing suites compile package-free C# as version 7.3 with warnings treated as errors.

| Suite | Coverage | Report |
| --- | --- | --- |
| Session rules | 14 rule/input/event/capacity groups | `session-rules.json` |
| Fault lab | 65 seeded session/event cases and expected unreliable-press diagnostic | `fault-lab.json` |
| Reliable actions + confirmation wire | 9 action groups, 8 wire groups, 65 fault cases | `reliable-actions.json` |
| Endurance | 10 recovery groups, 21 ten-minute matrix cases and two two-hour virtual cases | `endurance.json` |
| Event recovery | 11 boundary groups and 67 replay/overflow/lifecycle cases | `event-recovery.json` |

The default **Full** profile runs 220 fault/endurance cases across the four simulation suites. The session suite adds 14 rule/capacity groups. The current complete run reports 52 named check groups and 62,276 assertions in total. Assertion counts include repeated invariants, not independent test cases. Each simulation suite repeats its seeded results internally; the base session suite runs once.

For shorter development feedback:

```powershell
pwsh -NoProfile -File tools/net-test/verify.ps1 -Profile Quick
```

Quick runs all five suites, but reduces the endurance matrix to two virtual minutes and omits its two two-hour cases (218 simulation cases total). It is explicitly marked `fullCoverage: false`; it is not the full integration baseline.

Each invocation creates a new timestamp/nonce directory beneath ignored `tools/net-test/results/verification/`. `-OutputDirectory` selects another parent folder. Paths containing spaces and running from a different working directory are supported. No previous results are deleted or reused. The final console line gives the absolute `summary.json` path; sibling files contain per-suite reports and logs. Failures do not prevent later suites from running.

Success requires exit code zero **and** all five reports to validate: expected evidence type, check/case counts, nonzero assertion counts where present, empty failure lists, successful case flags, required repeat flags and explicit offline scope. Missing/malformed reports fail verification. Full/Quick endurance metadata must match the requested profile. Exit code one means verification failed; inspect the summary and the named suite log. Prerequisite/path failures can stop the command before a summary is written.

The summary includes SHA-256 hashes of its runner, suite scripts and C# inputs, plus each child report. It rejects source changes during the run. Hashes fingerprint on-disk bytes, so checkout line endings can change them without changing behavior. Reports/logs are local artifacts; the lightweight [recorded full summary](evidence/verification.json) is a snapshot, not a distribution of its sibling detailed reports. Rerun the command to produce a complete current report folder.

The runner preserves diagnostic findings even when tests pass: movement processed acknowledgments do not establish reliable action delivery; timeout is not proof of non-execution; same-generation host restart loses deduplication history; diagnostic checkpoints do not establish combat restoration. All results explicitly remain **offline C# evidence**, not Unity/socket/Internet/combat acceptance.

When changing the verification runner, its separate error-path checks can be run with:

```powershell
pwsh -NoProfile -File tools/net-test/verify-checks.ps1
```

Those checks launch the real runner against synthetic child reports in an ignored fixture checkout. They cover Full/Quick profiles, process failure, reported failure, missing/malformed/empty reports, missing repeat/scope flags, failed case flags, source changes, fresh output folders and continued execution after a failed suite. Synthetic fixture results are never networking evidence.

For review order, current API signatures, ownership and unresolved integration seams, begin with the [network integration handoff](../../docs/testing/network-integration-handoff.md).
