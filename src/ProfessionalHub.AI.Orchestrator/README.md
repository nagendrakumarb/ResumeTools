# AI package orchestrator

This local program asks a gRPC model worker to build a small browser package, validates the files, and copies the approved package into the Blazor app.

It never runs on GitHub Pages and must never process or publish a visitor's resume.

## One complete run

1. Start the ML.NET, .NET AI, or Python gRPC worker required by the request.
2. Ensure the worker address in `ai-requests/*.request.json` is correct.
3. Run:

```powershell
dotnet run --project src/ProfessionalHub.AI.Orchestrator -c Release -- package `
  --request ai-requests/section-classifier.request.json `
  --output src/App/wwwroot/ai-packages
```

4. The orchestrator checks worker capability, requests training/export, rejects missing or oversized output, calculates SHA-256 hashes, and updates the package index.
5. Review and commit the generated files. GitHub Pages deploys them with the Blazor PWA.

## Automatic GitHub workflow

`.github/workflows/generate-ai-package.yml` runs only on a self-hosted Windows runner labelled `professionalhub-ai`. That computer must have the local gRPC worker running. The workflow creates a pull request; it does not publish directly to `main`.

The existing Pages workflow remains separate and deploys only after the generated-package pull request is reviewed and merged.
