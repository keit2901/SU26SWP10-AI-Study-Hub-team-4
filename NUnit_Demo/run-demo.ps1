$ErrorActionPreference = "Stop"

Write-Host "== AI Study Hub standalone NUnit demo =="
Write-Host "Checking .NET SDK..."
dotnet --version

Write-Host "Restoring NuGet packages..."
dotnet restore "$PSScriptRoot\AIStudyHub.NUnitDemo.sln"

Write-Host "Running NUnit tests..."
dotnet test "$PSScriptRoot\AIStudyHub.NUnitDemo.sln" --configuration Release --no-restore

Write-Host "Demo completed successfully."
