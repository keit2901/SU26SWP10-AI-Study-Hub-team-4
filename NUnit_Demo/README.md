# NUnit Demo - AI Study Hub

This folder is a **standalone NUnit demo** for the AI Study Hub project. It can be zipped and moved to another computer without copying the base project.

## What this demo proves

- NUnit test discovery and execution with `dotnet test`
- FluentAssertions assertion style, matching the main project tests
- Moq-based dependency mocking
- Async service testing with `Task`
- Parameterized tests with `[TestCase]`
- A small AI Study Hub-like search scenario: processed documents, user isolation, query validation, and ranked results

## Requirements on the target computer

The target computer only needs:

1. .NET 8 SDK installed
2. Internet access for the first `dotnet restore` so NuGet packages can be downloaded

It does **not** need the original AI Study Hub source code, database, Docker, Supabase, or any local secrets.

Check SDK:

```powershell
dotnet --version
```

## How to run

From this folder:

```powershell
.\run-demo.ps1
```

Or manually:

```powershell
dotnet restore .\AIStudyHub.NUnitDemo.sln
dotnet test .\AIStudyHub.NUnitDemo.sln --configuration Release --no-restore
```

Expected result:

```text
Passed!  - Failed: 0, Passed: 6, Skipped: 0
```

## Folder contents

```text
NUnit_Demo/
├─ AIStudyHub.NUnitDemo.sln
├─ README.md
├─ run-demo.ps1
├─ .gitignore
├─ AIStudyHub.NUnitDemo.Tests/
│  ├─ AIStudyHub.NUnitDemo.Tests.csproj
│  ├─ Domain/
│  │  └─ StudySearchService.cs
│  └─ Tests/
│     ├─ SmokeTests.cs
│     └─ StudySearchServiceTests.cs
└─ sample-output/
   └─ expected-test-summary.txt
```

## Why the zip stays small

The demo intentionally excludes generated files:

- `bin/`
- `obj/`
- `.vs/`
- `TestResults/`
- coverage files
- local NuGet package caches

The zip contains only source code and project files. NuGet packages are restored on the target machine.
