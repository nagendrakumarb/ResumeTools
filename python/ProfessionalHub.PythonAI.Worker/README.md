# Python AI Worker

## What is this project?

This optional local worker tries Python NLP or ML when a suitable .NET solution is not available.

Possible uses:

- Meaningful multi-word phrase extraction
- Semantic similarity experiments
- Requirement classification experiments
- Small ONNX model export

Example:

Instead of returning single words such as `typically` and `minimum`, a phrase extractor returns `REST API development` and keeps the original sentence as evidence.

This worker does not run on GitHub Pages. The Blazor project does not reference this Python project.

## Setup

```powershell
py -m venv .venv
.\.venv\Scripts\python -m pip install -r requirements.txt
.\.venv\Scripts\python generate_protos.py
.\.venv\Scripts\python server.py
```

## First tasks

1. Generate Python classes from the shared protobuf file.
2. Start a simple gRPC server.
3. Extract meaningful phrases with their source positions.
4. Return confidence, evidence, model version, and warnings.
5. Compare Python results with the .NET baseline.
6. Export only small, license-approved ONNX models.

## Learn in this order

1. [Python virtual environments](https://docs.python.org/3/library/venv.html)
2. [gRPC Python quick start](https://grpc.io/docs/languages/python/quickstart/)
3. [gRPC Python tutorial](https://grpc.io/docs/languages/python/basics/)
4. [spaCy linguistic features](https://spacy.io/usage/linguistic-features)
5. [scikit-learn text features](https://scikit-learn.org/stable/modules/feature_extraction.html#text-feature-extraction)
6. [Export traditional ML to ONNX](https://onnxruntime.ai/docs/tutorials/traditional-ml.html)

## Small exercise

Pass 20 job-description sentences to the worker. Return phrases and source positions. Manually confirm that no isolated boilerplate word is presented as a skill.

## Finished when

Python and .NET use the same contract, results are reproducible, licenses are recorded, and no Python runtime is required by the deployed website.

See the [beginner feature plan](../../src/App/wwwroot/docs/BEGINNER-AI-PLAN.md).
