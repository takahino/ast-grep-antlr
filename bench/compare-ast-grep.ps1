# Compare ag4 vs ast-grep (sg) on a sample tree. Prefer a published ag4 binary.
param(
    [string]$Target = ".",
    [string]$Pattern = "System.out.println(`$A)",
    [string]$Lang = "java",
    [ValidateSet("tree-sitter", "antlr", "")]
    [string]$Backend = "tree-sitter",
    [string]$Ag4Exe = ""
)

$ErrorActionPreference = "Stop"
Write-Host "Benchmark target=$Target pattern=$Pattern lang=$Lang backend=$Backend"

function Resolve-Ag4 {
    param([string]$Explicit)
    if ($Explicit -and (Test-Path $Explicit)) { return (Resolve-Path $Explicit).Path }
    $candidates = @(
        "$PSScriptRoot/../artifacts/win-x64/ag4.exe",
        "$PSScriptRoot/../artifacts/linux-x64/ag4",
        "$PSScriptRoot/../artifacts/osx-arm64/ag4",
        (Get-Command ag4 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
    ) | Where-Object { $_ -and (Test-Path $_) }
    if ($candidates) { return $candidates[0] }
    return $null
}

$ag4Path = Resolve-Ag4 -Explicit $Ag4Exe
$backendArgs = @()
if ($Backend) { $backendArgs = @("--backend", $Backend) }

if ($ag4Path) {
    Write-Host "Using ag4: $ag4Path"
    $ag4 = Measure-Command {
        & $ag4Path run -p $Pattern -l $Lang @backendArgs $Target | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { throw "ag4 run failed with exit code $LASTEXITCODE" }
} else {
    Write-Host "Published ag4 not found; using dotnet run (slower — publish first for fair timing)"
    $proj = "$PSScriptRoot/../src/AstGrepAntlr.Cli/AstGrepAntlr.Cli.csproj"
    $ag4 = Measure-Command {
        dotnet run --project $proj -c Release --no-build -- run -p $Pattern -l $Lang @backendArgs $Target | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { throw "ag4 run failed with exit code $LASTEXITCODE (build it first: dotnet build -c Release)" }
}
Write-Host ("ag4: {0:n2}s" -f $ag4.TotalSeconds)

if (Get-Command sg -ErrorAction SilentlyContinue) {
    $sg = Measure-Command {
        sg run -p $Pattern -l $Lang $Target | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { throw "ast-grep run failed with exit code $LASTEXITCODE" }
    Write-Host ("ast-grep: {0:n2}s" -f $sg.TotalSeconds)
} else {
    Write-Host "ast-grep (sg) not found; skip comparison"
}
