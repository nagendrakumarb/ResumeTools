# Resume Tools Core

## What is this project?

This library contains the shared resume rules used by the Blazor website.

Example:

ATS compatibility and Job Match both need to recognize resume sections and apply safe DOCX corrections. That logic belongs here once, instead of being copied into two pages.

This project must remain browser-compatible.

## Main responsibilities

- Resume and job data classes
- Token and phrase normalization
- TF-IDF and cosine matching
- Explainable score components
- Shared correction instructions
- Safe DOCX generation logic that the browser can use
- Adapters for approved JSON/model results

## First tasks

1. Create common result objects for evidence, score components, warnings, and edit audits.
2. Make ATS and Job Match use the same correction engine.
3. Add score limits so repetition cannot create a false 100%.
4. Add package adapters with a deterministic fallback.
5. Keep unsafe or unsupported suggestions as `Suggest` or `Reject`.

## Learn with examples

- [.NET library design guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/)
- [Text transforms and TF-IDF concepts](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/transforms)
- [Open XML Word structure](https://learn.microsoft.com/en-us/office/open-xml/word/structure-of-a-wordprocessingml-document)
- [System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)

## Small exercise

Run the same resume correction request through ATS and Job Match. Confirm both create the same edit instructions and audit messages.

## Finished when

There is one shared implementation for analysis and correction, every score has evidence, and missing AI packages do not break the application.

See the [beginner feature plan](../App/wwwroot/docs/BEGINNER-AI-PLAN.md).
