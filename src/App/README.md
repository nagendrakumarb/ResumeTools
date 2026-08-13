# Blazor website

This is the website users open at `professionalhub.co.in`.

## What runs here

- Resume upload and local parsing
- ATS checks and job matching
- Small JSON rules
- Small browser-compatible ONNX models
- Offline PWA features

The resume stays in the browser.

## What does not run here

- Python
- gRPC servers
- ML.NET training
- Large AI models
- Secure API-key operations

GitHub Pages only serves static files. The local workers prepare files; this app only reads approved files from `wwwroot/ai-packages`.

## Start learning here

Read [Beginner AI and ML plan](wwwroot/docs/BEGINNER-AI-PLAN.md). It explains every planned feature in simple English, with examples and direct learning links.

After that, use [Detailed implementation guide](wwwroot/docs/AI-IMPLEMENTATION-GUIDE.md) while writing code.

## First small exercise

1. Add a JSON file containing two skill aliases.
2. Load it with `HttpClient`.
3. Show its version in the UI.
4. Disconnect the internet and confirm the installed PWA can still use it.

Learn:

- [Blazor WebAssembly](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models#blazor-webassembly)
- [Call files/APIs with HttpClient](https://learn.microsoft.com/en-us/aspnet/core/blazor/call-web-api)
- [Blazor PWA](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app)
- [ONNX Runtime Web](https://onnxruntime.ai/docs/get-started/with-javascript/web.html)

Do not add references from this project to the gRPC workers, ML.NET worker, or Python worker.

## Advertising policy safeguard

No Google ad code is included in this project while the site is under AdSense
review. This prevents ads from being served on the interactive resume analyzer,
search results, loading screens, errors, policy pages, or other utility pages.

Before enabling ads in the future, complete a route-by-route policy review. Ads
must remain disabled on every application and utility screen. Only consider a
manual placement on a standalone, substantial editorial page after approval; do
not use Auto Ads for this application.
