# Compare ag4 vs ast-grep (sg) on a sample tree. Installs nothing by default.
param(
    [string]$Target = ".",
    [string]$Pattern = "System.out.println(`$A)",
    [string]$Lang = "java"
)

$ErrorActionPreference = "Stop"
Write-Host "Benchmark target=$Target pattern=$Pattern"

Measure-Command {
    dotnet run --project "$PSScriptRoot/../src/AstGrepAntlr.Cli" -c Release --no-build -- run -p $Pattern -l $Lang $Target | Out-Null
} | ForEach-Object { Write-Host ("ag4: {0:n2}s" -f $_.TotalSeconds) }

if (Get-Command sg -ErrorAction SilentlyContinue) {
    Measure-Command {
        sg run -p $Pattern -l $Lang $Target | Out-Null
    } | ForEach-Object { Write-Host ("ast-grep: {0:n2}s" -f $_.TotalSeconds) }
} else {
    Write-Host "ast-grep (sg) not found; skip comparison"
}
