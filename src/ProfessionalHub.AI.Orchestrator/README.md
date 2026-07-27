# AI Orchestrator

## What is this project?

This is the local coordinator. It receives a task, chooses a worker, waits for the answer, checks the result, and approves good artifact files.

Example:

1. Receive `Train requirement classifier`.
2. Send it to the ML.NET worker.
3. Receive a model and evaluation report.
4. Reject it if accuracy is too low.
5. Copy an approved browser file to the Blazor `ai-packages` folder.

It does not run on GitHub Pages.

## First tasks

1. Route each task to a worker that supports it.
2. Limit how many jobs can wait in memory.
3. Support cancellation and time limits.
4. Retry only safe operations.
5. Validate model version, size, checksum, and evaluation score.
6. Keep a local approval report.

## Learn with examples

- [Use gRPC clients in .NET](https://learn.microsoft.com/en-us/aspnet/core/grpc/clientfactory)
- [Producer/consumer work with .NET Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
- [gRPC cancellation and deadlines](https://learn.microsoft.com/en-us/aspnet/core/grpc/deadlines-cancellation)
- [gRPC retries](https://learn.microsoft.com/en-us/aspnet/core/grpc/retries)
- [SHA hashes in .NET](https://learn.microsoft.com/en-us/dotnet/standard/security/cryptographic-services)

## Small exercise

Start both local workers. Send one .NET task and one Python task. Stop one worker and confirm the orchestrator returns a clear message instead of crashing.

## Finished when

Only tested, versioned, checksum-verified artifacts can enter the Blazor `wwwroot/ai-packages` folder.

See the [beginner feature plan](../App/wwwroot/docs/BEGINNER-AI-PLAN.md).
