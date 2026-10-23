# Format checks for staged files (Windows). Analyzers are covered by "build before commit".
$ErrorActionPreference = "Stop"
$root = git rev-parse --show-toplevel
Set-Location $root

$staged = git diff --cached --name-only --diff-filter=ACMR
if (-not $staged) {
    exit 0
}

$csFiles = @($staged | Where-Object { $_ -match '\.(cs|csproj|props|targets|xml)$' })
$prettierFiles = @($staged | Where-Object { $_ -match '\.(yml|yaml|json|md|mdx)$' })

if ($csFiles.Count -gt 0) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error "pre-commit: dotnet is required for CSharpier checks"
    }
    dotnet tool restore | Out-Null
    & dotnet csharpier check @csFiles
    if ($LASTEXITCODE -ne 0) {
        Write-Error "pre-commit: CSharpier check failed. Run: dotnet csharpier format ."
    }
}

if ($prettierFiles.Count -gt 0) {
    if (-not (Get-Command npx -ErrorAction SilentlyContinue)) {
        Write-Error "pre-commit: npx is required for Prettier checks"
    }
    & npx prettier --check @prettierFiles
    if ($LASTEXITCODE -ne 0) {
        Write-Error "pre-commit: Prettier check failed. Run: npx prettier --write ."
    }
}
