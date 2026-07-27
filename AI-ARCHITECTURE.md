# Professional Hub local AI artifact factory

The projects in this repository are separated into two deployment groups.

## GitHub Pages runtime

- `src/App`
- `src/ProfessionalHub.ResumeTools.Core`
- `src/ProfessionalHub.AI.PortableRuntime`
- validated files under `src/App/wwwroot/ai-packages`

The portable runtime contains no ML.NET, Python, gRPC, native model runtime, or secret credentials.

## Local development workers

- `src/ProfessionalHub.AI.Contracts`
- `src/ProfessionalHub.AI.Orchestrator`
- `src/ProfessionalHub.DotNetAI.Worker`
- `src/ProfessionalHub.MLNet.Worker`
- `python/ProfessionalHub.PythonAI.Worker`

These projects are committed for collaborative development but are not referenced by the Blazor application and are not published by the GitHub Pages workflow.

## Identified tasks

The shared contracts define interfaces for:

1. Requirement classification and boilerplate rejection.
2. Meaningful multi-word phrase extraction.
3. Evidence-aware contextual term placement.
4. Explainable and non-inflated job-match scoring.
5. Formatting-aware resume section classification.
6. Lossless content-retention ranking for compaction.
7. Technical and non-technical role classification.
8. Relevant job ranking.
9. Cross-provider duplicate-job detection.
10. Suspicious-job detection.
11. Achievement and bullet-quality scoring.

All current worker calls return successful placeholder results. Implementations and portable artifact promotion will be added incrementally.

## Source-control and deployment rule

The deployment workflow must continue to publish only:

```text
src/App/ResumeTools.csproj
```

Adding worker projects to the solution does not deploy or execute them on GitHub Pages.
