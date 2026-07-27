# ML.NET Worker

## What is this project?

This local worker learns patterns from reviewed examples using ML.NET.

It can train:

- Requirement sentence classification
- Resume section classification
- Role classification
- Bullet-quality scoring
- Job recommendation
- Scam-job warning
- Page-compaction priority

Example:

We label job sentences as `Required skill`, `Preferred skill`, `Benefit`, or `Legal text`. The worker trains a model and reports how often each label is predicted correctly.

This worker does not run in the Blazor browser or on GitHub Pages.

## First tasks

1. Define a simple input row and label for one feature.
2. Load reviewed data.
3. Split data into training, validation, and test sets.
4. Train a simple baseline.
5. Compare trainers.
6. Report a confusion matrix and per-label scores.
7. Save the model and a plain-English report.
8. Export browser-safe output only when possible.

## Learn in this order

1. [What ML.NET is](https://learn.microsoft.com/en-us/dotnet/machine-learning/)
2. [ML.NET API concepts](https://learn.microsoft.com/en-us/dotnet/machine-learning/mldotnet-api)
3. [Choose a machine-learning task](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)
4. [Load data](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/load-data-ml-net)
5. [Prepare data](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/prepare-data-ml-net)
6. [Train and evaluate](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/train-machine-learning-model-ml-net)
7. [Save and load a model](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/save-load-machine-learning-models-ml-net)

## First small exercise

Create 100 job sentences with two labels: `Requirement` and `NotRequirement`. Train a classifier, print the confusion matrix, and inspect every incorrect prediction.

## Finished when

Every model has:

- a dataset version;
- a label guide;
- a separate test set;
- per-label evaluation results;
- a confidence threshold;
- a plain-English limitation note.

See each feature, example, and link in the [beginner feature plan](../App/wwwroot/docs/BEGINNER-AI-PLAN.md).
