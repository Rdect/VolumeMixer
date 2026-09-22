$ErrorActionPreference = 'Stop'
$testOutput = Join-Path ([IO.Path]::GetTempPath()) ('VolumeMixer.Tests.' + [guid]::NewGuid().ToString('N') + '.exe')
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
try {
    & $compiler /nologo /target:exe /platform:x64 /warnaserror+ /main:RegressionTests "/out:$testOutput" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'VolumeMixer.cs') (Join-Path $PSScriptRoot 'tests\RegressionTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed' }
}
finally {
    if (Test-Path -LiteralPath $testOutput) { Remove-Item -LiteralPath $testOutput -Force }
}
