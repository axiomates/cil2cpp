# CIL2CPP Integration Test - Full Pipeline
# Tests: C# .csproj → codegen → CMake configure → C++ build → run → verify output
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tests/integration/run_pipeline.ps1
#   powershell -ExecutionPolicy Bypass -File tests/integration/run_pipeline.ps1 -RuntimePrefix C:/cil2cpp_test
#
# Exit code: 0 = all passed, 1 = failure

param(
    [string]$RuntimePrefix = "C:/cil2cpp_test",
    [string]$Generator = "Visual Studio 17 2022",
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"
$script:TestCount = 0
$script:PassCount = 0
$script:FailCount = 0
$script:Failures = @()

$RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$TempBase = Join-Path ([System.IO.Path]::GetTempPath()) "cil2cpp_integration_$(Get-Random -Minimum 10000 -Maximum 99999)"

function Write-Header($msg) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host " $msg" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Test-Step($name, $scriptBlock) {
    $script:TestCount++
    Write-Host "  [$script:TestCount] $name ... " -NoNewline
    try {
        & $scriptBlock
        $script:PassCount++
        Write-Host "PASS" -ForegroundColor Green
    } catch {
        $script:FailCount++
        $script:Failures += "${name}: $_"
        Write-Host "FAIL" -ForegroundColor Red
        Write-Host "       $_" -ForegroundColor Red
    }
}

# ============================================================
Write-Header "CIL2CPP Integration Test"
Write-Host "  Repo:    $RepoRoot"
Write-Host "  Runtime: $RuntimePrefix"
Write-Host "  Config:  $Config"
Write-Host "  Temp:    $TempBase"

# ============================================================
Write-Header "Phase 0: Prerequisites"

Test-Step "dotnet SDK available" {
    $v = & dotnet --version 2>&1
    if ($LASTEXITCODE -ne 0) { throw "dotnet not found" }
    Write-Host "($v) " -NoNewline
}

Test-Step "CMake available" {
    $v = & cmake --version 2>&1 | Select-Object -First 1
    if ($LASTEXITCODE -ne 0) { throw "cmake not found" }
    Write-Host "($v) " -NoNewline
}

Test-Step "Runtime installed at $RuntimePrefix" {
    $configFile = Join-Path $RuntimePrefix "lib/cmake/cil2cpp/cil2cppConfig.cmake"
    if (-not (Test-Path $configFile)) { throw "cil2cppConfig.cmake not found at $configFile" }
}

# ============================================================
Write-Header "Phase 1: HelloWorld (executable with entry point)"

$HelloWorldSample = Join-Path $RepoRoot "compiler/samples/HelloWorld/HelloWorld.csproj"
$HelloWorldOutput = Join-Path $TempBase "helloworld_output"
$HelloWorldBuild  = Join-Path $TempBase "helloworld_build"

Test-Step "Codegen HelloWorld" {
    $result = & dotnet run --project (Join-Path $RepoRoot "compiler/CIL2CPP.CLI") -- codegen -i $HelloWorldSample -o $HelloWorldOutput 2>&1
    if ($LASTEXITCODE -ne 0) { throw "codegen failed: $result" }
}

Test-Step "Generated files exist (*.h, *.cpp, main.cpp, CMakeLists.txt)" {
    $files = @("HelloWorld.h", "HelloWorld.cpp", "main.cpp", "CMakeLists.txt")
    foreach ($f in $files) {
        $p = Join-Path $HelloWorldOutput $f
        if (-not (Test-Path $p)) { throw "Missing: $f" }
    }
}

Test-Step "CMake configure" {
    $result = & cmake -B $HelloWorldBuild -S $HelloWorldOutput -G $Generator -A x64 "-DCMAKE_PREFIX_PATH=$RuntimePrefix" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed: $result" }
}

Test-Step "CMake build ($Config)" {
    $result = & cmake --build $HelloWorldBuild --config $Config 2>&1
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed: $result" }
}

Test-Step "Run HelloWorld and verify output" {
    $exe = Join-Path $HelloWorldBuild "$Config/HelloWorld.exe"
    if (-not (Test-Path $exe)) { throw "Executable not found: $exe" }
    $output = & $exe 2>&1
    if ($LASTEXITCODE -ne 0) { throw "HelloWorld exited with code $LASTEXITCODE" }
    $outputStr = $output -join "`n"
    $expected = "Hello, CIL2CPP!`n30`n42"
    if ($outputStr.Trim() -ne $expected.Trim()) {
        throw "Output mismatch.`nExpected:`n$expected`nGot:`n$outputStr"
    }
}

# ============================================================
Write-Header "Phase 2: Library project (no entry point)"

$LibOutput = Join-Path $TempBase "lib_output"
$LibBuild  = Join-Path $TempBase "lib_build"
$LibSample = Join-Path $TempBase "lib_sample"

# Create a temporary class library project
Test-Step "Create temporary class library project" {
    New-Item -ItemType Directory -Path $LibSample -Force | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
</Project>
"@ | Set-Content (Join-Path $LibSample "MathLib.csproj")
    @"
public class MathHelper
{
    private int _value;

    public int Add(int a, int b) { return a + b; }
    public int Multiply(int a, int b) { return a * b; }

    public void SetValue(int v) { _value = v; }
    public int GetValue() { return _value; }
}
"@ | Set-Content (Join-Path $LibSample "MathHelper.cs")
}

Test-Step "Codegen library project" {
    $result = & dotnet run --project (Join-Path $RepoRoot "compiler/CIL2CPP.CLI") -- codegen -i (Join-Path $LibSample "MathLib.csproj") -o $LibOutput 2>&1
    if ($LASTEXITCODE -ne 0) { throw "codegen failed: $result" }
}

Test-Step "Library generates add_library (no main.cpp)" {
    $cmake = Get-Content (Join-Path $LibOutput "CMakeLists.txt") -Raw
    if ($cmake -notmatch "add_library") { throw "CMakeLists.txt missing add_library" }
    $mainPath = Join-Path $LibOutput "main.cpp"
    if (Test-Path $mainPath) { throw "Library should not have main.cpp" }
}

Test-Step "Library CMake configure + build" {
    $result = & cmake -B $LibBuild -S $LibOutput -G $Generator -A x64 "-DCMAKE_PREFIX_PATH=$RuntimePrefix" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed: $result" }
    $result = & cmake --build $LibBuild --config $Config 2>&1
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed: $result" }
}

# ============================================================
Write-Header "Phase 3: Debug configuration"

$DebugOutput = Join-Path $TempBase "debug_output"
$DebugBuild  = Join-Path $TempBase "debug_build"

Test-Step "Codegen HelloWorld in Debug mode" {
    $result = & dotnet run --project (Join-Path $RepoRoot "compiler/CIL2CPP.CLI") -- codegen -i $HelloWorldSample -o $DebugOutput -c Debug 2>&1
    if ($LASTEXITCODE -ne 0) { throw "codegen failed: $result" }
}

Test-Step "Debug output contains #line directives" {
    $source = Get-Content (Join-Path $DebugOutput "HelloWorld.cpp") -Raw
    if ($source -notmatch "#line") { throw "No #line directives found in Debug output" }
}

Test-Step "Debug output contains IL offset comments" {
    $source = Get-Content (Join-Path $DebugOutput "HelloWorld.cpp") -Raw
    if ($source -notmatch "/\* IL_") { throw "No IL offset comments found in Debug output" }
}

Test-Step "Debug build + run produces same output" {
    $result = & cmake -B $DebugBuild -S $DebugOutput -G $Generator -A x64 "-DCMAKE_PREFIX_PATH=$RuntimePrefix" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed: $result" }
    $result = & cmake --build $DebugBuild --config Debug 2>&1
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed: $result" }
    $exe = Join-Path $DebugBuild "Debug/HelloWorld.exe"
    $output = (& $exe 2>&1) -join "`n"
    $expected = "Hello, CIL2CPP!`n30`n42"
    if ($output.Trim() -ne $expected.Trim()) {
        throw "Debug output mismatch.`nExpected:`n$expected`nGot:`n$output"
    }
}

# ============================================================
Write-Header "Phase 4: String literals"

Test-Step "HelloWorld source contains string_literal calls" {
    $source = Get-Content (Join-Path $HelloWorldOutput "HelloWorld.cpp") -Raw
    if ($source -notmatch "string_literal") { throw "No string_literal calls found" }
    if ($source -notmatch "Hello, CIL2CPP!") { throw "String content not found" }
}

Test-Step "HelloWorld source contains __init_string_literals" {
    $header = Get-Content (Join-Path $HelloWorldOutput "HelloWorld.h") -Raw
    if ($header -notmatch "__init_string_literals") { throw "No __init_string_literals in header" }
}

# ============================================================
Write-Header "Cleanup"

try {
    Remove-Item -Recurse -Force $TempBase -ErrorAction SilentlyContinue
    Write-Host "  Cleaned up temp directory"
} catch {
    Write-Host "  Warning: Could not clean up $TempBase" -ForegroundColor Yellow
}

# ============================================================
Write-Header "Results"

Write-Host ""
Write-Host "  Total:  $script:TestCount" -ForegroundColor White
Write-Host "  Passed: $script:PassCount" -ForegroundColor Green
Write-Host "  Failed: $script:FailCount" -ForegroundColor $(if ($script:FailCount -gt 0) { "Red" } else { "Green" })

if ($script:Failures.Count -gt 0) {
    Write-Host "`n  Failures:" -ForegroundColor Red
    foreach ($f in $script:Failures) {
        Write-Host "    - $f" -ForegroundColor Red
    }
}

Write-Host ""

exit $script:FailCount
