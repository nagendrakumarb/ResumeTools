# .NET AI Worker

## What is this project?

This local worker handles tasks that are easier and safer with ordinary .NET code.

Main jobs:

- Read DOCX structure without flattening it
- Create JSON skill aliases and role taxonomies
- Find duplicate jobs
- Build safe resume edit plans
- Compact a resume without deleting a complete section
- Export small JSON packages

Example:

Before changing a paragraph, the worker records its section, style, bold text, numbering, and position. This helps it keep headings and layout intact.

## First tasks

1. Read paragraphs, tables, styles, numbering, font size, bold state, and spacing.
2. Give every paragraph a stable ID and section name.
3. Create edit decisions: `Apply`, `Suggest`, or `Reject`.
4. Require supporting resume evidence before inserting a job term.
5. Compact in safe stages: spacing first, repeated wording second, user-approved omission last.
6. Export versioned JSON without personal data.

## Learn with examples

- [Structure of a Word document](https://learn.microsoft.com/en-us/office/open-xml/word/structure-of-a-wordprocessingml-document)
- [Work with WordprocessingML](https://learn.microsoft.com/en-us/office/open-xml/word/word-processing)
- [Insert a paragraph](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-insert-a-paragraph-into-a-word-processing-document)
- [Validate a Word document](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-validate-a-word-processing-document)
- [Write JSON in .NET](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)

## Small exercise

Open a DOCX with headings, tables, bullets, and bold employer names. Read and save it without changing anything. Compare the original and output visually and structurally.

## Finished when

No automatic operation removes a complete section, invents experience, or destroys heading and bold hierarchy.

See the [beginner feature plan](../App/wwwroot/docs/BEGINNER-AI-PLAN.md).
