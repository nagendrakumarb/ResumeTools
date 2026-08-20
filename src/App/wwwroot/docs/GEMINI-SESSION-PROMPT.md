# Beginner AI and machine-learning plan

This page explains what we can build for Professional Hub and where each part runs.

## The simple idea

There are two separate systems:

1. **The local artifact factory** runs on the developer's computer. It learns from reviewed examples and creates small files such as `model.zip`, `.onnx`, or `.json`.
2. **The Blazor website** runs on GitHub Pages. It downloads approved small files and uses them in the browser. It does not run Python, gRPC servers, or ML.NET training.

Example:

1. We review 500 job-description sentences.
2. The ML.NET worker learns which sentences describe skills and which are legal or company text.
3. It saves a model.
4. We test and approve the model locally.
5. We export a browser-safe file to `src/App/wwwroot/ai-packages`.
6. GitHub Pages publishes that file with the Blazor app.
7. The browser uses it without uploading the user's resume.

Learn the basic ideas:

- [What is machine learning?](https://learn.microsoft.com/en-us/training/modules/fundamentals-machine-learning/)
- [ML.NET introduction](https://learn.microsoft.com/en-us/dotnet/machine-learning/)
- [Blazor WebAssembly](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models#blazor-webassembly)
- [GitHub Pages is static hosting](https://docs.github.com/en/pages/getting-started-with-github-pages/about-github-pages)
- [ONNX Runtime in a browser](https://onnxruntime.ai/docs/get-started/with-javascript/web.html)

## Features that work with our current static architecture

These features can be prepared locally and then used by the Blazor website.

### 1. Requirement sentence classification

**Purpose:** separate real job requirements from benefits, legal text, and company introductions.

**Example:** “Five years of C# experience” is a requirement. “We are an equal opportunity employer” is not a skill.

**Producer:** ML.NET or Python.

**Static output:** `model.zip` for local .NET use, or a small ONNX model for browser use.

**Learn:**

- [ML.NET classification tasks](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)
- [Text classification tutorial](https://learn.microsoft.com/en-us/dotnet/machine-learning/tutorials/sentiment-analysis)
- [scikit-learn text features](https://scikit-learn.org/stable/modules/feature_extraction.html#text-feature-extraction)

### 2. Resume section classification

**Purpose:** recognize Summary, Skills, Experience, Education, Projects, and other sections even when headings vary.

**Example:** “Career Profile” and “Professional Summary” should both be treated as Summary.

**Producer:** ML.NET.

**Static output:** `model.zip`, or browser-compatible exported rules/model.

**Learn:**

- [ML.NET multiclass classification](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks#multiclass-classification)
- [WordprocessingML document structure](https://learn.microsoft.com/en-us/office/open-xml/word/structure-of-a-wordprocessingml-document)

### 3. Role classification

**Purpose:** place jobs and resumes into understandable groups.

**Example:** “C# Developer” becomes Technical → Software Engineering → .NET. “School Teacher” becomes Non-technical → Education → Teaching.

**Producer:** ML.NET.

**Static output:** `model.zip` or a JSON taxonomy plus a small model.

**Learn:**

- [Multiclass classification](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks#multiclass-classification)
- [Prepare data for ML.NET](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/prepare-data-ml-net)

### 4. Bullet-quality scoring

**Purpose:** score whether a resume bullet clearly shows an action, work performed, and a result.

**Example:** “Built an API that reduced processing time by 30%” is stronger than “Responsible for APIs.”

**Producer:** ML.NET.

**Static output:** `model.zip` or JSON scoring weights.

**Learn:**

- [ML.NET regression](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks#regression)
- [Evaluate an ML.NET model](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/train-machine-learning-model-ml-net#evaluate-the-model)

### 5. Skill aliases

**Purpose:** understand that different spellings can mean the same skill.

**Example:** `.NET Core`, `dotnet`, and `ASP.NET Core` are related but must retain their precise meaning.

**Producer:** .NET rule generator.

**Static output:** versioned JSON.

**Learn:**

- [System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)
- [.NET string comparison](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings)

### 6. Role taxonomy

**Purpose:** provide Technical/Non-technical groups and smaller categories for the role selector.

**Example:** Marketing → Digital Marketing; Education → College Faculty; Technology → Cloud Engineering.

**Producer:** .NET generator.

**Static output:** versioned JSON.

**Learn:**

- [System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)
- [Options validation patterns](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)

### 7. Keyword importance

**Purpose:** give more weight to real skills and less weight to common words.

**Example:** `REST API` should matter more than `typically` or `desired`.

**Producer:** .NET or Python.

**Static output:** JSON weights and stop phrases.

**Learn:**

- [ML.NET text transforms](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/transforms)
- [TF-IDF explanation](https://scikit-learn.org/stable/modules/feature_extraction.html#tfidf-term-weighting)

### 8. Duplicate-job detection

**Purpose:** combine the same job returned by several providers.

**Example:** the same company, role, location, and description should appear once even if two APIs return it.

**Producer:** .NET.

**Static output:** JSON thresholds, normalization rules, or a small model.

**Learn:**

- [.NET hashing](https://learn.microsoft.com/en-us/dotnet/standard/security/cryptographic-services)
- [.NET string best practices](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings)

### 9. Job recommendation

**Purpose:** order fetched jobs by relevance to the selected resume.

**Example:** a senior .NET role should rank above an unrelated Java graduate role for an experienced .NET resume.

**Producer:** ML.NET.

**Static output:** ranking `model.zip` or browser-safe ranking weights.

**Learn:**

- [ML.NET ranking](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks#ranking)
- [Recommendation tasks](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks#recommendation)

### 10. Scam-job detection

**Purpose:** warn about suspicious wording or missing company information. It must warn, not make a legal accusation.

**Example:** payment requests, unrealistic income, and anonymous contact details increase the warning score.

**Producer:** ML.NET or Python.

**Static output:** model plus human-readable labels and warning rules.

**Learn:**

- [ML.NET anomaly detection](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks#anomaly-detection)
- [Model evaluation](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/train-machine-learning-model-ml-net#evaluate-the-model)

### 11. Semantic similarity

**Purpose:** compare meaning, not only exact words.

**Example:** “built REST services” can be related to “API development” even when the wording differs.

**Producer:** Python/ONNX or a compatible .NET model.

**Static output:** a small ONNX model. Large language models are not suitable for this static site.

**Learn:**

- [ONNX Runtime Web](https://onnxruntime.ai/docs/get-started/with-javascript/web.html)
- [ONNX model optimization](https://onnxruntime.ai/docs/performance/model-optimizations/)

### 12. Resume correction policy

**Purpose:** decide what the app may change automatically and what needs user confirmation.

**Example:** normalizing a heading can be automatic. Inventing experience must always be rejected.

**Producer:** .NET rule generator.

**Static output:** versioned JSON rules.

**Learn:**

- [System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)
- [Open XML validation](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-validate-a-word-processing-document)

### 13. Contextual term placement

**Purpose:** place a truthful missing phrase in the correct resume section and inside a meaningful sentence.

**Example:** `REST API` may belong in Skills or an evidenced Experience bullet. The word `typically` should not be added.

**Producer:** rules plus a classifier.

**Static output:** model plus JSON rules.

**Learn:**

- [Open XML paragraphs](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-insert-a-paragraph-into-a-word-processing-document)
- [ML.NET classification tasks](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks)

### 14. Page-compaction priorities

**Purpose:** reduce page count without deleting complete sections or destroying headings.

**Example:** first remove empty spacing, then condense repeated bullets, and only omit content after explicit user approval.

**Producer:** ML.NET ranker plus safe .NET rules.

**Static output:** model plus thresholds and protected-section rules.

**Learn:**

- [ML.NET ranking](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks#ranking)
- [WordprocessingML](https://learn.microsoft.com/en-us/office/open-xml/word/word-processing)

## Features that still need a hosted service

GitHub Pages cannot run these features by itself. A local training factory cannot make them permanently live unless the complete required model and data fit and run safely in the browser.

| Feature | Why static GitHub Pages is not enough | What would be required |
|---|---|---|
| ChatGPT-style generation using a secret API | Browser code exposes the API key | A secure Web API or serverless backend |
| Continuously updated cloud model | Static files change only after deployment | A hosted model service and deployment pipeline |
| Very large LLM | Download and browser memory are too large | GPU/CPU model hosting |
| Live centralized job crawling | Crawlers require scheduled execution, IP control, and storage | A hosted worker and database |
| Cross-user recommendation learning | User data must be collected and aggregated securely | Backend, consent, database, and training pipeline |
| Secure external-provider API keys | Any key shipped to a browser can be inspected | A backend secret store and proxy API |
| Server-side job application submission | Requires authenticated provider sessions and protected tokens | A secure backend supported by each provider |
| Email delivery | SMTP/API secrets cannot be kept in browser code | An email service called from a backend |
| Models too large for browser memory | The browser may freeze or fail to load | Hosted inference API |

Learn why:

- [GitHub Pages limits](https://docs.github.com/en/pages/getting-started-with-github-pages/about-github-pages)
- [Secure secrets in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [ASP.NET Core Web API](https://learn.microsoft.com/en-us/aspnet/core/web-api/)
- [gRPC services with ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore)

## Recommended learning and build order

1. Build JSON skill aliases and role taxonomy. These are easiest and work immediately in Blazor.
2. Build the requirement classifier so false missing words stop appearing.
3. Build resume-section classification.
4. Build meaningful phrase extraction.
5. Build contextual placement with strict evidence rules.
6. Replace the match percentage with explainable weighted components.
7. Add job ranking and duplicate detection.
8. Add safe page-compaction priorities.
9. Export one small ONNX model and prove it runs offline in the browser.
10. Add the remaining models only after each one passes its acceptance test.

The detailed engineering checklist is in [AI-IMPLEMENTATION-GUIDE.md](AI-IMPLEMENTATION-GUIDE.md).
