# Compare ag4 vs ast-grep (sg) on a sample tree. Installs nothing by default.
param(
    [string]$Target = ".",
    [string]$Pattern = "System.out.println(`$A)",
    [string]$Lang = "java"
)

$ErrorActionPreference = "Stop"
Write-Host "Benchmark target=$Target pattern=$Pattern"

# Native commands do not trip $ErrorActionPreference, so a failed run would otherwise be
# reported as a suspiciously fast time instead of an error.
$ag4 = Measure-Command {
    dotnet run --project "$PSScriptRoot/../src/AstGrepAntlr.Cli" -c Release --no-build -- run -p $Pattern -l $Lang $Target | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw "ag4 run failed with exit code $LASTEXITCODE (build it first: dotnet build -c Release)" }
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
