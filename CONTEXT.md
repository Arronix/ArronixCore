# CONTEXT

## Project
- Name: `ArronixCore`
- Root: `/Users/adam/Code/GitHub/Arronix/ArronixCore`
- Current branch: `copilot/fix-b574755e-682c-4311-9729-43483d33eaf6`
- Upstream divergence at snapshot time: ahead by 1 commit
- Strategic goal: extract a shared core platform for `*.arr` apps, then load app-specific behavior (Sonarr/Radarr/Lidarr/etc.) as installable extensions instead of each app re-implementing the full engine.

## Architecture Snapshot
- Large .NET solution with core modules under `src/`.
- Legacy namespaces and module names still include `NzbDrone.*` / `Sonarr.*` while Arronix rebranding is in progress.
- Major shared foundation area: `src/NzbDrone.Common`.
- Domain/business logic: `src/NzbDrone.Core`.
- API and web host layers: `src/Sonarr.Api.V3`, `src/Sonarr.Api.V5`, `src/Sonarr.Http`, `src/NzbDrone.Host`.
- Test projects include `NzbDrone.Core.Test`, `NzbDrone.Common.Test`, and integration/API tests.
- Intended target architecture:
  - Stable shared engine in Arronix core packages.
  - Extension points for app-specific domains/workflows.
  - Installable `*.arr` product modules (Sonarr/Radarr/Lidarr/etc.) that plug into the shared runtime.

## Working State Snapshot (2026-03-16)

### Repository Cleanliness
- Working tree and index have been reset to a clean baseline.
- No staged or unstaged source changes are pending.

### Current Branch State
- Branch remains `copilot/fix-b574755e-682c-4311-9729-43483d33eaf6`.
- Local branch is ahead of upstream by 1 commit at snapshot time.

### Intentional Baseline
- Keep the codebase stable while clarifying architecture and extension boundaries.
- Prefer small, scoped changes that explicitly support extraction of reusable platform components and app-specific extension modules.

## Observations / Ambiguities To Confirm
- `src/NzbDrone.Core.Test/DecisionEngineTests/UpgradeAllowedSpecificationFixture .cs` includes a literal space before `.cs` in filename.
- Product branding/build identity is being adjusted (`Sonarr` references still coexist with `Arronix` metadata).
- Serializer transition status across modules should be planned deliberately (mixed Newtonsoft/System.Text.Json usage exists in the wider solution).
