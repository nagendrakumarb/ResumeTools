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

## AdSense manual ad slot

The AdSense publisher ID is configured only on the content-rich
`wwwroot/resume-guide.html` page. It is deliberately absent from the interactive
ATS and job-match application because Google does not permit ads on screens that
have little publisher content or primarily perform a utility action.

The manual ad placement example is commented in `wwwroot/resume-guide.html`.

After AdSense approves the site:

1. Open **AdSense > Ads > By ad unit**.
2. Create a **Display ad**.
3. Copy its numeric `data-ad-slot` value.
4. Find `REPLACE_WITH_REAL_NUMERIC_AD_SLOT_ID` in `wwwroot/resume-guide.html`.
5. Replace it with the numeric value and remove the surrounding HTML comment.

Do not publish a literal placeholder such as `YOUR_AD_SLOT_ID`. Keep manual ads
away from upload, scan, download, job-selection, and application buttons. Do not
add the AdSense script or a manual ad unit back to the Blazor tool shell.
