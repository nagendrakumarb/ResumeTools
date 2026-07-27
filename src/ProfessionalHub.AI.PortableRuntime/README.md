# Portable AI Runtime

## What is this project?

This browser-safe library reads the small files produced by local workers.

Example:

- `skill-aliases.json` says `dotnet` is related to `.NET`.
- The Blazor app loads that file.
- This library validates it and provides a simple lookup result.

It may read:

- JSON rules and weights
- JSON taxonomies
- Small browser-compatible ONNX models

It must not contain ML.NET training, Python, a gRPC server, secrets, or private training data.

## First tasks

1. Read the package manifest.
2. Check its version and checksum.
3. Reject damaged or unknown packages.
4. Load JSON aliases, rules, weights, and taxonomies.
5. Later, add a small ONNX model only after a browser test succeeds.
6. Return evidence and package version with every result.

## Learn with examples

- [Read JSON with System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)
- [Compute hashes in .NET](https://learn.microsoft.com/en-us/dotnet/standard/security/cryptographic-services)
- [ONNX Runtime Web](https://onnxruntime.ai/docs/get-started/with-javascript/web.html)
- [Blazor WebAssembly performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance)

## Small exercise

Change one character in a JSON package after its checksum was created. Confirm this library refuses to load it and shows a helpful reason.

## Finished when

The Blazor app can safely use an approved package offline and can fall back to normal rules if the package is missing.

See the [beginner feature plan](../App/wwwroot/docs/BEGINNER-AI-PLAN.md).
