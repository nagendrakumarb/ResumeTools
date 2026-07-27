# AI Contracts

## What is this project?

This project defines the messages exchanged by the local .NET and Python workers.

Think of it as a form that everyone agrees to use.

Example request:

- Task: classify a job sentence
- Text: `Five years of C# experience is required`
- Request ID: `123`

Example response:

- Label: `RequiredSkill`
- Confidence: `0.96`
- Evidence: the original sentence

## Why do we need it?

Without one shared format, the .NET worker and Python worker may return different names or missing information.

## First tasks

1. Add a version to every request.
2. Add a request ID.
3. Add result confidence, evidence, warnings, and model version.
4. Use `accepted = false` for a normal rejected request.
5. Use a gRPC error only when communication or the worker fails.

## Learn with examples

- [Protocol Buffers beginner guide](https://protobuf.dev/getting-started/csharptutorial/)
- [Proto3 language guide](https://protobuf.dev/programming-guides/proto3/)
- [gRPC tutorial for .NET](https://learn.microsoft.com/en-us/aspnet/core/tutorials/grpc/grpc-start)
- [gRPC tutorial for Python](https://grpc.io/docs/languages/python/basics/)

## Small exercise

Add an optional `model_version` field. Send an old message without this field and confirm it still works.

## Finished when

The same request and response work in both .NET and Python without converting field names manually.

See the [beginner feature plan](../App/wwwroot/docs/BEGINNER-AI-PLAN.md).
