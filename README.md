# Professional Hub

Professional Hub is a Blazor WebAssembly resume checker and job matcher.

## Run the website locally

```powershell
dotnet run --project src/App/ResumeTools.csproj
```

## Understand the AI plan

Start with the [beginner AI and machine-learning plan](src/App/wwwroot/docs/BEGINNER-AI-PLAN.md).

It clearly separates:

- features we can prepare locally and publish as small static files;
- features that still require a hosted server;
- the project responsible for each task;
- simple examples and direct learning links.

Use the [detailed implementation guide](src/App/wwwroot/docs/AI-IMPLEMENTATION-GUIDE.md) only after reading the beginner plan.

## Simple project map

| Project | Simple purpose |
|---|---|
| `src/App` | The Blazor website deployed to GitHub Pages |
| `ProfessionalHub.ResumeTools.Core` | Shared resume rules used by the website |
| `ProfessionalHub.AI.Contracts` | Common request and response language for local workers |
| `ProfessionalHub.AI.Orchestrator` | Sends local tasks to the correct worker and approves outputs |
| `ProfessionalHub.DotNetAI.Worker` | Builds rules and safely edits DOCX files |
| `ProfessionalHub.MLNet.Worker` | Trains and evaluates ML.NET models locally |
| `ProfessionalHub.PythonAI.Worker` | Tries Python models when .NET is not suitable |
| `ProfessionalHub.AI.PortableRuntime` | Reads approved JSON/ONNX files in browser-safe .NET code |

Only `src/App` is deployed. The other projects are local development tools.

## Important privacy rule

Never commit real resumes, API keys, raw private datasets, virtual environments, or unapproved models.
