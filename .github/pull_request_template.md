## What and why

<!-- What does this change, and what problem does it solve? Link the issue if there is one. -->

## How it was tested

- [ ] `dotnet build src/dotnet/Wayfinder.sln` with zero warnings
- [ ] `dotnet test src/dotnet/Wayfinder.sln` (Docker running for the integration tests)
- [ ] Ran it locally against the real data (describe what you tried)

## Checklist

- [ ] No secrets in code or appsettings (`pre-commit run --all-files` passes)
- [ ] A schema change comes with an EF Core migration
- [ ] A prompt change bumps the prompt version and updates the snapshot
- [ ] A change to retrieval or answers has a before/after `runEval` result in `docs/EVALUATION.md`
- [ ] Public API shape unchanged, or the response snapshots and API version are updated
