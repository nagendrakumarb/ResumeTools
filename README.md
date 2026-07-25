# Professional Hub Resume Matcher

A .NET 9 Blazor WebAssembly PWA for private, offline resume-to-job matching on `professionalhub.co.in`.

## Run locally

```powershell
dotnet run --project src/App/ResumeTools.csproj
```

## Configure AdSense

Set `PublisherId` and `Slot` on `AdUnit` in `Pages/Home.razor`. AdSense loads only on the production custom domain and only when the values match Google's publisher/slot formats, so placeholder and localhost runs stay quiet.

## Architecture note

ML.NET and EF Core SQLite are server/desktop-oriented and are not supported reliably in browser WebAssembly. This app implements the same TF-IDF plus cosine algorithm in browser-compatible C# and persists non-sensitive analysis summaries in IndexedDB. Resume text is never persisted or uploaded. PDF and DOCX parsing uses PdfPig and Open XML respectively.

## GitHub Pages

Set repository **Settings → Pages → Source** to **GitHub Actions**, point the domain DNS records at GitHub Pages, and enable HTTPS after DNS validation. The workflow publishes the PWA and generates `404.html`, `.nojekyll`, and `CNAME`.
