---
name: update-benchmarks
description: Use when capturing or refreshing benchmark results on any device, adding a benchmark class to Pixelbadger.Toolkit.Benchmarks, querying the results DB, or regenerating docs/benchmark-report.html.
---

# Update benchmarks

Every capture files a run under `benchmarks/results/<device>/<version>/` (append-only JSON). `docs/benchmark-report.html` is generated from that tree. All commands run from the repo root.

## Prerequisites

- .NET 10 SDK. In Claude Code cloud containers the dot.net installer host is blocked by the proxy; `apt-get install -y dotnet-sdk-10.0` (Ubuntu) works.
- `export TERM=xterm`.
- Release builds need a Six Labors licence: set `SixLaborsLicenseFile` to a `sixlabors.lic` path outside the repo. Never commit it (it is gitignored). Debug builds only warn, but BenchmarkDotNet requires Release. CI gets it from the `SIXLABORS_LICENSE` secret.

## Before capturing

- Commit your work first. A dirty tree gives the run file a `-dirty` suffix and `gitDirty=true`.
- The version folder comes from `<Version>` in `Pixelbadger.Toolkit/Pixelbadger.Toolkit.csproj`. Bump it first if the capture is for a new version.
- Keep the machine otherwise idle: no builds, tests or other benchmarks alongside.

## Commands

```bash
dotnet run -c Release --project Pixelbadger.Toolkit.Benchmarks -- run          # standard tier, MediumRun
dotnet run -c Release --project Pixelbadger.Toolkit.Benchmarks -- run --slow   # Paillier tier, ShortRun
```

Options: `--device <name>`, `--filter <glob>`, `--job short|medium|default`, `--results-dir <dir>`.

```bash
... -- validate            # prints OK, exit 0 when the tree is clean
... -- report              # rewrites docs/benchmark-report.html (byte-deterministic)
... -- query "<sql>"      # tab-separated output; SQL error -> stderr, exit 1
```

Query tables: `devices`, `runs` (id, path, device_slug, version, git_sha, git_dirty, captured_at_utc, tier, job, total_seconds), `results` (run_id, class, method, parameters, success, mean_ns, median_ns, std_dev_ns, error_ns, min_ns, max_ns, n, allocated_bytes, gen0, gen1, gen2).

Latest mean per method for one class, across versions, on one device:

```sql
select ru.version, r.method, round(r.mean_ns) as mean_ns
from results r join runs ru on ru.id = r.run_id
where ru.device_slug = 'linux-x64-intel-xeon-2-80ghz-4c-16gb' and r.class = 'AbjadifyBenchmarks'
  and ru.captured_at_utc = (select max(captured_at_utc) from runs x
       where x.device_slug = ru.device_slug and x.version = ru.version and x.tier = ru.tier)
order by r.method, ru.version
```
```text
version	method	mean_ns
8.2.0	Abjadify_Document	479876
8.2.0	Abjadify_Paragraph	23016
8.2.0	Abjadify_ShortText	4347
```

Slowest 10 benchmarks of the newest run:

```sql
select class, method, parameters, round(mean_ns) as mean_ns from results
where run_id = (select id from runs order by captured_at_utc desc limit 1)
order by mean_ns desc limit 10
```
```text
class	method	parameters	mean_ns
HomomorphicStringBenchmarks	GenerateKey		4751138508
HomomorphicStringBenchmarks	DecryptString_Long		3203681453
HomomorphicStringBenchmarks	SubstringEncrypted_FromMiddle		3037679671
```

Anything else passes straight to BenchmarkDotNet and records no results:

```bash
... -- --list flat
... -- --filter '*Abjadify*' --job dry
```

## Measured

Measured on `linux-x64-intel-xeon-2-80ghz-4c-16gb` (4 vCPU KVM, .NET 10.0.13, BDN 0.15.8). Add a row when you capture on another device.

| Device | Standard (MediumRun) | Slow (ShortRun) | Filtered ShortRun smoke | Peak RSS | Run file size |
|---|---|---|---|---|---|
| linux-x64-intel-xeon-2-80ghz-4c-16gb | 61 benchmarks, 41 min wall | 16 benchmarks, 10 min | ~1 min | ~250 MB | 28 KB / 7 KB |

## Cloud sessions

- Run the capture as a harness-tracked background command. A detached `nohup` capture died when the idle container was reclaimed.
- An interrupted run leaves no run file. A stray `*.tmp` is flagged by `validate` and must be deleted.

## Device identity

- Slug = `os-arch-cpu-<logical>c-<RAM GB>gb`, from physical RAM (GC's available-memory figure under-reports: 10 vs 16 GB here).
- A changed fingerprint exits 2: pick a new `--device` name.
- Cloud instance types with different RAM become different devices.

## After capturing

1. `-- validate`, then `-- report`.
2. Commit `benchmarks/results/**` and `docs/benchmark-report.html` together. CI fails on a stale report or an invalid results tree.
3. Results and report need no version bump.

## Adding a benchmark

- New class in `Pixelbadger.Toolkit.Benchmarks` with `[MemoryDiagnoser]`, public Toolkit APIs only, deterministic inputs, temp files cleaned up.
- No `[BenchmarkCategory]` unless it belongs in the slow tier (`"slow"`).
- Add the component basename to the `WATCHLIST` in `.github/workflows/pr-checks.yml`.
- Dry-run it with `-- --filter '*YourClass*' --job dry`.
- `[Params]` multiplies every method in the class: the old Paillier class ran 28 cases instead of 14 that way, 16 of the old suite's 20.5 ShortRun minutes. Split classes rather than share params.

## Retention

Append-only by design. Prune only by a deliberate `git rm` of old run files, then regenerate the report.

## Report page

It loads Chart.js 4.4.0 from jsdelivr; offline, the tables still render.
