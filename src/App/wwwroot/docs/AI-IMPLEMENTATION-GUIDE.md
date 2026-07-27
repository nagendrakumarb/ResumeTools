# Professional Hub AI/ML implementation guide

This is the master learning and implementation checklist. Local workers create and validate artifacts; GitHub Pages deploys only Blazor WebAssembly and approved portable files under `wwwroot/ai-packages`.

## Delivery path

1. Collect and anonymize reviewed examples locally.
2. Implement and evaluate a task in a .NET or Python worker.
3. Export a deterministic JSON or browser-compatible ONNX package.
4. Validate its schema, metrics, license, version, and checksum in the orchestrator.
5. Promote only the approved package to `src/App/wwwroot/ai-packages`.
6. Load it with `ProfessionalHub.AI.PortableRuntime`.

Never commit resumes, API keys, raw datasets, virtual environments, generated Python protobuf files, or unapproved artifacts.

## TODO 1 — Stabilize the shared protobuf contract

**Problem:** C# and Python need one versioned request/result format.

**Learn first**

- [Proto3 messages, services, field numbers, optional fields, maps, and compatibility](https://protobuf.dev/programming-guides/proto3/)
- [ASP.NET Core gRPC services](https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore)
- [Python gRPC basics](https://grpc.io/docs/languages/python/basics/)

**Implement**

1. Add `contract_version`, `correlation_id`, typed options, typed metrics, warnings, evidence spans, and artifact metadata.
2. Regenerate C# and Python code.
3. Reserve every deleted field number and name.
4. Return `accepted=false` plus a reason for an unsupported task; reserve gRPC failures for transport/system failures.

**Exercise:** add an optional field and prove older serialized requests still parse.

**Done when:** the same request works with both workers and additive schema changes remain backward compatible.

## TODO 2 — Reproducible, privacy-safe datasets

**Problem:** training without stable labels, splits, and anonymization produces unreliable or unsafe models.

**Learn first**

- [ML.NET architecture, `MLContext`, `IDataView`, transforms, and models](https://learn.microsoft.com/en-us/dotnet/machine-learning/mldotnet-api)
- [Load data into ML.NET](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/load-data-ml-net)
- [Train and evaluate ML.NET models](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/train-machine-learning-model-ml-net)

**Implement**

1. Define JSONL schemas for inputs, labels, evidence spans, and reviewer decisions.
2. replace identity fields with stable anonymous tokens.
3. Deduplicate before seeded train/validation/test splitting.
4. Store dataset version, label-policy version, source type, checksum, seed, and date.

**Exercise:** run the pipeline twice and compare checksums and split membership.

**Done when:** splits are reproducible, label rules are documented, and a PII scan finds no identity data.

## TODO 3 — Requirement classification and boilerplate rejection

**Problem:** “typically”, “minimum”, “desired”, benefits, and legal text become false missing skills.

**Learn first**

- [Binary, multiclass, text classification, ranking, and anomaly tasks in ML.NET](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)
- [Prepare data for ML.NET](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/prepare-data-ml-net)
- [Train and evaluate a model](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/train-machine-learning-model-ml-net)

**Implement**

1. Split job text by heading, sentence, and bullet while retaining offsets.
2. Label required skill, preferred skill, experience, education, responsibility, benefit, legal boilerplate, company narrative, and irrelevant text.
3. Build a deterministic baseline.
4. Train a multiclass model and return label, confidence, span, and explanation.
5. Route low-confidence results to manual review.

**Exercise:** label 200 balanced spans and produce a confusion matrix.

**Done when:** required-skill precision is at least 0.90 and boilerplate recall at least 0.95 on held-out data.

## TODO 4 — Meaningful multi-word phrase extraction

**Problem:** isolated tokens generate grammar errors and meaningless suggestions.

**Learn first**

- [ML.NET text transformations](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/transforms)
- [spaCy linguistic features and noun chunks](https://spacy.io/usage/linguistic-features)
- [.NET string best practices](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings)

**Implement**

1. Extract noun/technology phrases with source offsets.
2. Generate n-grams, then reject stop phrases and boilerplate classes.
3. Canonicalize aliases while retaining original display text.
4. Rank by requirement class, specificity, section importance, and frequency.

**Exercise:** compare the top ten phrases with a manually reviewed list for 50 jobs.

**Done when:** at least 90% of top-ten phrases are meaningful and every phrase links to source evidence.

## TODO 5 — Evidence-aware contextual term placement

**Problem:** missing terms are appended to Skills and create unsupported, unnatural claims.

**Learn first**

- [Sentence similarity as an ML.NET scenario](https://learn.microsoft.com/en-us/dotnet/machine-learning/mldotnet-api)
- [WordprocessingML document structure](https://learn.microsoft.com/en-us/office/open-xml/word/structure-of-a-wordprocessingml-document)
- [Insert and work with Word paragraphs](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-insert-a-paragraph-into-a-word-processing-document)

**Implement**

1. Classify every resume block.
2. Retrieve existing evidence for each target phrase.
3. Return `Apply`, `Suggest`, or `Reject`.
4. Generate minimal edit plans referencing paragraph IDs and preserving run styles.
5. Require confirmation for suggested wording.
6. Never invent employers, dates, metrics, qualifications, outcomes, or technologies.

**Exercise:** place one phrase in Skills, one in Summary, one in Experience, and reject one unsupported phrase.

**Done when:** every insertion has evidence and destination references, and unaffected formatting remains unchanged.

## TODO 6 — Explainable, non-inflated match scoring

**Problem:** keyword injection can create 100% despite mismatched seniority, education, or responsibility.

**Learn first**

- [Choose ML.NET tasks and algorithms](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)
- [Evaluate ML.NET models](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/train-machine-learning-model-ml-net)
- [Calibrated binary-classification metrics](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.data.calibratedbinaryclassificationmetrics)

**Implement**

1. Define weights totaling 100 for requirement coverage, evidence, recency, seniority, responsibility, education, and semantic relevance.
2. Cap the total when an essential requirement is missing.
3. Award phrases only when linked to evidence.
4. Calibrate bands against independent human ratings.
5. Return all components, caps, uncertainty, and evidence.

**Exercise:** repeat a keyword 20 times and prove the score does not rise.

**Done when:** unsupported additions cannot increase the score and every point is explainable.

## TODO 7 — Formatting-aware resume section classification

**Problem:** misclassifying headings and body text damages fonts, bold hierarchy, and layout.

**Learn first**

- [WordprocessingML hierarchy](https://learn.microsoft.com/en-us/office/open-xml/word/structure-of-a-wordprocessingml-document)
- [Apply and understand Word paragraph styles](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-apply-a-style-to-a-paragraph-in-a-word-processing-document)
- [ML.NET multiclass classification](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)

**Implement**

1. Extract text, position, style, numbering, table location, font size, bold, spacing, and neighbors.
2. Label identity, contact, title, heading, employer, role, date, bullet, skill, education, project, and other.
3. Combine dictionaries with model results.
4. Preserve original XML references and flag low confidence.

**Exercise:** visualize classifications over two structurally different DOCX files.

**Done when:** heading/subheading recall is at least 0.98 and uncertain blocks are never reformatted automatically.

## TODO 8 — Lossless content-retention ranking and compaction

**Problem:** one/two-page enforcement can remove Projects or shrink headings instead of condensing low-value wording.

**Learn first**

- [ML.NET ranking tasks](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)
- [WordprocessingML document APIs](https://learn.microsoft.com/en-us/office/open-xml/word/word-processing)
- [Validate WordprocessingML documents](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-validate-a-word-processing-document)

**Implement**

1. Rank bullets by relevance, recency, uniqueness, impact, and section importance.
2. Apply phases: empty-paragraph cleanup, excessive-spacing normalization, wording condensation, compatible-bullet merging, then approval-gated omission.
3. Protect identity, contact, headings, recent employment, education, and unique projects.
4. Audit every change and retain the original package.
5. Determine rendered page count after every phase.

**Exercise:** compact both a two-column and a single-column four-page resume without deleting a section.

**Done when:** headings retain hierarchy, no section disappears without approval, and the audit reports changed paragraph IDs and saved space.

## TODO 9 — Technical and non-technical role classification

**Problem:** roles need stable grouping across engineering, education, healthcare, sales, marketing, administration, finance, and other families.

**Learn first**

- [ML.NET multiclass classification](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)
- [O*NET occupation taxonomy and web services](https://services.onetcenter.org/)
- [Prepare ML.NET data and labels](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/prepare-data-ml-net)

**Implement**

1. Define versioned family, specialization, and seniority IDs.
2. Map configured titles.
3. Classify unseen title plus description.
4. Return primary class, alternatives, and confidence.

**Exercise:** evaluate a balanced set spanning every role family.

**Done when:** macro-F1 is reported and uncertain roles become `Unclassified`, never random categories.

## TODO 10 — Relevant job ranking

**Problem:** .NET searches can surface unrelated Java or nonmatching roles.

**Learn first**

- [ML.NET learning-to-rank tasks](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)
- [Feature normalization and concatenation](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/train-machine-learning-model-ml-net)
- [ML.NET ranking metrics](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.data.rankingmetrics)

**Implement**

1. Build exact-title, technology, location, work-mode, seniority, freshness, and description-quality features.
2. Create query-grouped relevance grades.
3. Rank after provider retrieval and retain provider attribution.
4. Explain local threshold filtering separately from provider errors.

**Exercise:** build a judged list for ten searches and calculate NDCG.

**Done when:** unrelated stacks rank below matching roles and NDCG is tracked on held-out queries.

## TODO 11 — Cross-provider duplicate detection

**Problem:** one opening appears through multiple APIs with different IDs or text.

**Learn first**

- [.NET cryptographic hashing](https://learn.microsoft.com/en-us/dotnet/standard/security/cryptographic-services)
- [Clustering in ML.NET](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)
- [.NET URI normalization](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-uri)

**Implement**

1. Normalize provider ID, canonical URL, company, title, location, and date.
2. Match exact stable keys first.
3. Use weighted fuzzy similarity only without exact keys.
4. Retain every provider and application URL on the canonical record.

**Exercise:** label 100 candidate pairs including different openings at one company.

**Done when:** duplicate precision is at least 0.98 and distinct openings are not merged.

## TODO 12 — Suspicious-job signals

**Problem:** feeds can contain scams, malformed posts, expired links, or misleading redirects.

**Learn first**

- [ML.NET anomaly and binary-classification tasks](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)
- [Prevent unsafe redirects in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/preventing-open-redirects)
- [Google guidance for avoiding job scams](https://support.google.com/websearch/answer/106318)

**Implement**

1. Detect missing company identity, mismatched domains, payment requests, suspicious contacts, extreme claims, and broken URLs.
2. Train only after enough reviewed examples exist.
3. Return individual signals and confidence.
4. Never call an employer fraudulent or auto-block solely from a model score.

**Exercise:** test legitimate edge cases as aggressively as suspicious examples.

**Done when:** every warning exposes its signal and the false-positive rate is measured.

## TODO 13 — Achievement and bullet-quality scoring

**Problem:** bullets need truthful, dimension-specific feedback rather than a vague grade.

**Learn first**

- [Regression versus classification in ML.NET](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)
- [ML.NET text transforms](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/transforms)
- [ML.NET evaluation and error analysis](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/train-machine-learning-model-ml-net)

**Implement**

1. Define a rubric for action, task, context, outcome, metric, clarity, and truthfulness risk.
2. Extract deterministic signals first.
3. Train only after acceptable reviewer agreement.
4. Generate protected-fact edit templates, not invented achievements.

**Exercise:** have two reviewers independently score the same bullets and measure agreement.

**Done when:** per-dimension scores are shown and suggestions introduce no new facts.

## TODO 14 — Orchestration, backpressure, retries, and cancellation

**Problem:** fast/slow producers and consumers need bounded local coordination without an external message broker.

**Learn first**

- [.NET channels and backpressure](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
- [gRPC deadlines and cancellation](https://learn.microsoft.com/en-us/aspnet/core/grpc/deadlines-cancellation)
- [gRPC retry configuration](https://learn.microsoft.com/en-us/aspnet/core/grpc/retries)

**Implement**

1. Discover capabilities and route tasks.
2. Add bounded channels and per-worker concurrency.
3. Propagate deadlines and cancellation.
4. Log duration, result, checksum, and failure category.
5. Retry only safe idempotent operations.

**Exercise:** overload a slow worker and confirm bounded memory and cancellation.

**Done when:** slow workers cannot exhaust memory and retries cannot publish duplicate artifacts.

## TODO 15 — Portable artifact export and validation

**Problem:** local `model.zip`, Python pickle, or native runtimes cannot simply run on GitHub Pages.

**Learn first**

- [Save/load ML.NET models and separate training from prediction](https://learn.microsoft.com/en-us/dotnet/machine-learning/mldotnet-api)
- [Deploy traditional ML through ONNX Runtime](https://onnxruntime.ai/docs/tutorials/traditional-ml.html)
- [ONNX Runtime Web](https://onnxruntime.ai/docs/get-started/with-javascript/web.html)

**Implement**

1. Define manifest ID, version, task, format, checksum, schemas, preprocessing version, metrics, license, and minimum app version.
2. Export deterministic rules as JSON.
3. Export ONNX only after browser-operator validation.
4. Reject `model.zip`, pickle, native libraries, executable code, secrets, and training data.
5. Reproduce representative producer outputs before promotion.

**Exercise:** corrupt one package and attempt to promote it.

**Done when:** invalid packages fail closed and approved packages contain no PII or training data.

## TODO 16 — Blazor offline inference and UX

**Problem:** the browser must run approved features locally, safely, responsively, and offline.

**Learn first**

- [Blazor fundamentals and lifecycle](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/)
- [Call JavaScript from .NET in Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet)
- [Blazor Progressive Web Apps](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app)

**Implement**

1. Load and verify the package index.
2. Run deterministic JSON rules in C#.
3. Add ONNX Runtime Web only for a validated ONNX package.
4. Show progress, disable conflicting actions, and support cancellation.
5. Display package version, confidence, evidence, limitations, and review requirements.

**Exercise:** install the PWA, disconnect the network, reload, and execute each portable feature.

**Done when:** offline execution works, updates cannot mix incompatible versions, and resume text is never uploaded.

## Recommended order

1. TODOs 1–2.
2. TODOs 7, 3, and 4.
3. TODOs 5–6.
4. TODOs 13 and 8.
5. TODOs 9–12.
6. TODOs 14–16.

