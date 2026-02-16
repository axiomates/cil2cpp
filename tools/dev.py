#!/usr/bin/env python3
"""CIL2CPP Developer CLI - build, test, install, and code generation helper.

Usage:
    python tools/dev.py              # Interactive menu
    python tools/dev.py test --all   # Run all tests
    python tools/dev.py --help       # Show help
"""

import argparse
import os
import platform
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

# ===== Constants =====

REPO_ROOT = Path(__file__).resolve().parent.parent
COMPILER_DIR = REPO_ROOT / "compiler"
RUNTIME_DIR = REPO_ROOT / "runtime"
CLI_PROJECT = COMPILER_DIR / "CIL2CPP.CLI"
TEST_PROJECT = COMPILER_DIR / "CIL2CPP.Tests"
RUNTIME_TESTS_DIR = RUNTIME_DIR / "tests"
SAMPLES_DIR = COMPILER_DIR / "samples"

IS_WINDOWS = platform.system() == "Windows"
DEFAULT_PREFIX = "C:/cil2cpp_test" if IS_WINDOWS else "/usr/local/cil2cpp"
DEFAULT_GENERATOR = "Visual Studio 17 2022" if IS_WINDOWS else "Ninja"
EXE_EXT = ".exe" if IS_WINDOWS else ""

# Enable ANSI escape codes on Windows 10+
if IS_WINDOWS:
    os.system("")

USE_COLOR = sys.stdout.isatty() and os.environ.get("NO_COLOR") is None


# ===== Helpers =====

def _c(code, text):
    return f"\033[{code}m{text}\033[0m" if USE_COLOR else text


def header(msg):
    print(f"\n{'=' * 40}")
    print(f" {_c('36', msg)}")
    print(f"{'=' * 40}")


def success(msg):
    print(_c("32", msg))


def error(msg):
    print(_c("31", msg))


def warn(msg):
    print(_c("33", msg))


def run(cmd, *, cwd=None, check=True, capture=False):
    """Run a subprocess command. Returns CompletedProcess."""
    if isinstance(cmd, str):
        cmd = cmd.split()
    try:
        result = subprocess.run(
            cmd,
            cwd=cwd or REPO_ROOT,
            check=check,
            capture_output=capture,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        return result
    except subprocess.CalledProcessError as e:
        if capture:
            error(f"Command failed: {' '.join(str(c) for c in cmd)}")
            if e.stdout:
                print(e.stdout[-500:])
            if e.stderr:
                print(e.stderr[-500:])
        raise


def which_tool(name):
    return shutil.which(name)


# ===== cmd_build =====

def cmd_build(args):
    """Build compiler and/or runtime."""
    build_compiler = args.compiler or (not args.compiler and not args.runtime)
    build_runtime = args.runtime or (not args.compiler and not args.runtime)
    config = args.config

    if build_compiler:
        header("Building compiler")
        run(["dotnet", "build", str(COMPILER_DIR / "CIL2CPP.Core")])
        success("Compiler build succeeded")

    if build_runtime:
        header(f"Building runtime ({config})")
        build_dir = RUNTIME_DIR / "build"
        run(["cmake", "-B", str(build_dir), "-S", str(RUNTIME_DIR),
             "-G", DEFAULT_GENERATOR] + (["-A", "x64"] if IS_WINDOWS else []))
        run(["cmake", "--build", str(build_dir), "--config", config])
        success(f"Runtime build succeeded ({config})")

    return 0


# ===== cmd_test =====

def cmd_test(args):
    """Run tests."""
    run_compiler = args.compiler or args.all or (
        not args.compiler and not args.runtime and not args.integration)
    run_runtime = args.runtime or args.all or (
        not args.compiler and not args.runtime and not args.integration)
    run_integ = args.integration or args.all
    failures = 0

    if run_compiler:
        header("Compiler tests (xUnit)")
        if args.coverage:
            failures += _run_coverage()
        else:
            try:
                run(["dotnet", "test", str(TEST_PROJECT), "--verbosity", "minimal"])
                success("Compiler tests passed")
            except subprocess.CalledProcessError:
                error("Compiler tests FAILED")
                failures += 1

    if run_runtime:
        header("Runtime tests (Google Test)")
        build_dir = RUNTIME_TESTS_DIR / "build"
        try:
            run(["cmake", "-B", str(build_dir), "-S", str(RUNTIME_TESTS_DIR),
                 "-G", DEFAULT_GENERATOR] + (["-A", "x64"] if IS_WINDOWS else []),
                capture=True)
            run(["cmake", "--build", str(build_dir), "--config", "Debug"])
            run(["ctest", "--test-dir", str(build_dir), "-C", "Debug",
                 "--output-on-failure"])
            success("Runtime tests passed")
        except subprocess.CalledProcessError:
            error("Runtime tests FAILED")
            failures += 1

    if run_integ:
        header("Integration tests")
        ns = argparse.Namespace(
            prefix=getattr(args, "prefix", DEFAULT_PREFIX),
            config="Release",
            generator=DEFAULT_GENERATOR,
            keep_temp=False,
        )
        failures += cmd_integration(ns)

    return failures


def _run_coverage():
    """Run compiler + runtime tests with coverage and generate unified report."""
    results_dir = REPO_ROOT / "CoverageResults"
    if results_dir.exists():
        shutil.rmtree(results_dir)
    results_dir.mkdir(parents=True)

    coverage_xmls = []

    # ----- C# coverage (coverlet) -----
    header("C# coverage (coverlet)")
    cs_results = results_dir / "cs"
    try:
        run(["dotnet", "test", str(TEST_PROJECT),
             "--collect:XPlat Code Coverage",
             f"--results-directory:{cs_results}",
             "--verbosity", "minimal"])
    except subprocess.CalledProcessError:
        error("C# tests failed during coverage collection")
        return 1

    cs_xmls = list(cs_results.rglob("coverage.cobertura.xml"))
    if cs_xmls:
        coverage_xmls.extend(cs_xmls)
        success(f"  C# coverage: {cs_xmls[0]}")
    else:
        warn("  No C# coverage.cobertura.xml found")

    # ----- C++ coverage (OpenCppCoverage on Windows, lcov on Linux) -----
    header("C++ coverage")
    cpp_xml = results_dir / "cpp_coverage.cobertura.xml"

    if IS_WINDOWS:
        opencpp = _find_opencppcoverage()
        if not opencpp:
            warn("  OpenCppCoverage not found. Install with:")
            print("    winget install OpenCppCoverage.OpenCppCoverage")
            warn("  Skipping C++ coverage")
        else:
            # Build runtime tests in Debug (needs PDB for coverage)
            build_dir = RUNTIME_TESTS_DIR / "build"
            try:
                run(["cmake", "-B", str(build_dir), "-S", str(RUNTIME_TESTS_DIR),
                     "-G", DEFAULT_GENERATOR, "-A", "x64"], capture=True)
                run(["cmake", "--build", str(build_dir), "--config", "Debug"])
            except subprocess.CalledProcessError:
                error("  Failed to build runtime tests")
                return 1

            test_exe = build_dir / "Debug" / f"cil2cpp_tests{EXE_EXT}"
            if not test_exe.exists():
                error(f"  Test exe not found: {test_exe}")
            else:
                try:
                    run([str(opencpp),
                         "--modules", str(test_exe),
                         "--sources", str(RUNTIME_DIR / "src"),
                         "--sources", str(RUNTIME_DIR / "include"),
                         "--export_type", f"cobertura:{cpp_xml}",
                         "--quiet",
                         "--", str(test_exe)])
                    if cpp_xml.exists():
                        coverage_xmls.append(cpp_xml)
                        success(f"  C++ coverage: {cpp_xml}")
                except subprocess.CalledProcessError:
                    warn("  OpenCppCoverage failed (tests may still have passed)")
    else:
        # Linux: use lcov if available
        lcov = which_tool("lcov")
        genhtml = which_tool("genhtml")
        if not lcov:
            warn("  lcov not found. Install with: sudo apt install lcov")
            warn("  Skipping C++ coverage")
        else:
            build_dir = RUNTIME_TESTS_DIR / "build"
            try:
                run(["cmake", "-B", str(build_dir), "-S", str(RUNTIME_TESTS_DIR),
                     "-G", DEFAULT_GENERATOR, "-DENABLE_COVERAGE=ON"], capture=True)
                run(["cmake", "--build", str(build_dir), "--config", "Debug"])
                run(["ctest", "--test-dir", str(build_dir), "-C", "Debug"])
                # Generate lcov report → convert to cobertura
                run([lcov, "--capture", "--directory", str(build_dir),
                     "--output-file", str(results_dir / "coverage.info"),
                     "--ignore-errors", "mismatch"])
                run([lcov, "--remove", str(results_dir / "coverage.info"),
                     "/usr/*", "*/googletest/*", "*/tests/*", "*/.deps/*",
                     "--output-file", str(results_dir / "coverage_filtered.info")])
                # lcov2cobertura if available
                lcov2cob = which_tool("lcov_cobertura")
                if lcov2cob:
                    run([lcov2cob, str(results_dir / "coverage_filtered.info"),
                         "-o", str(cpp_xml)])
                    if cpp_xml.exists():
                        coverage_xmls.append(cpp_xml)
                        success(f"  C++ coverage: {cpp_xml}")
                else:
                    warn("  lcov_cobertura not found (pip install lcov_cobertura)")
                    warn("  C++ coverage collected but can't merge with C# report")
            except subprocess.CalledProcessError:
                warn("  C++ coverage collection failed")

    # ----- Merge & generate report -----
    if not coverage_xmls:
        error("No coverage data collected")
        return 1

    if not which_tool("reportgenerator"):
        warn("reportgenerator not found. Install with:")
        print("  dotnet tool install -g dotnet-reportgenerator-globaltool")
        for xml in coverage_xmls:
            print(f"  Coverage XML: {xml}")
        return 0

    header("Generating unified coverage report")
    report_dir = results_dir / "CoverageReport"
    reports_arg = ";".join(str(x) for x in coverage_xmls)
    run(["reportgenerator",
         f"-reports:{reports_arg}",
         f"-targetdir:{report_dir}",
         "-reporttypes:HtmlInline_AzurePipelines;TextSummary;Badges"])

    summary = report_dir / "Summary.txt"
    if summary.exists():
        print(f"\n{summary.read_text()}")

    index = report_dir / "index.html"
    if not index.exists():
        index = report_dir / "index.htm"
    success(f"HTML coverage report: {index}")

    import webbrowser
    webbrowser.open(index.as_uri())
    return 0


def _find_opencppcoverage():
    """Find OpenCppCoverage executable."""
    path = which_tool("OpenCppCoverage")
    if path:
        return path
    # Common install location
    default = Path("C:/Program Files/OpenCppCoverage/OpenCppCoverage.exe")
    if default.exists():
        return str(default)
    return None


# ===== cmd_install =====

def cmd_install(args):
    """Install runtime to prefix directory."""
    prefix = args.prefix
    configs = ["Debug", "Release"] if args.config == "both" else [args.config]
    build_dir = RUNTIME_DIR / "build"

    header(f"Installing runtime to {prefix}")

    run(["cmake", "-B", str(build_dir), "-S", str(RUNTIME_DIR),
         "-G", DEFAULT_GENERATOR] + (["-A", "x64"] if IS_WINDOWS else []))

    for config in configs:
        print(f"\n  Building {config}...")
        run(["cmake", "--build", str(build_dir), "--config", config])
        print(f"  Installing {config}...")
        run(["cmake", "--install", str(build_dir), "--config", config,
             "--prefix", prefix])

    success(f"Runtime installed to {prefix}")
    return 0


# ===== cmd_codegen =====

def cmd_codegen(args):
    """Generate C++ code from a C# project."""
    if args.sample:
        name = args.sample
        if not name.endswith(".csproj") and "/" not in name and "\\" not in name:
            csproj = SAMPLES_DIR / name / f"{name}.csproj"
        else:
            csproj = Path(name)
    elif args.input:
        csproj = Path(args.input)
    else:
        error("Specify a sample name or -i <path.csproj>")
        return 1

    if not csproj.exists():
        error(f"Not found: {csproj}")
        return 1

    output = Path(args.output)
    config = args.config

    header(f"Codegen: {csproj.name} ({config})")
    run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
         "codegen", "-i", str(csproj), "-o", str(output), "-c", config])
    success(f"Output: {output}")
    return 0


# ===== cmd_integration =====

class TestRunner:
    """Lightweight test runner for integration tests."""

    def __init__(self):
        self.test_count = 0
        self.pass_count = 0
        self.fail_count = 0
        self.failures = []

    def step(self, name, fn):
        self.test_count += 1
        print(f"  [{self.test_count}] {name} ... ", end="", flush=True)
        try:
            extra = fn()
            self.pass_count += 1
            if extra:
                print(f"({extra}) ", end="")
            success("PASS")
        except Exception as e:
            self.fail_count += 1
            self.failures.append(f"{name}: {e}")
            error("FAIL")
            print(f"       {e}")

    def summary(self):
        print(f"\n  Total:  {self.test_count}")
        success(f"  Passed: {self.pass_count}")
        if self.fail_count:
            error(f"  Failed: {self.fail_count}")
            print()
            error("  Failures:")
            for f in self.failures:
                error(f"    - {f}")
        else:
            success(f"  Failed: {self.fail_count}")
        print()
        return self.fail_count


def _exe_path(build_dir, config, name):
    """Get executable path for multi-config (VS) or single-config (Ninja/Make)."""
    multi = build_dir / config / f"{name}{EXE_EXT}"
    if multi.exists():
        return multi
    single = build_dir / f"{name}{EXE_EXT}"
    if single.exists():
        return single
    return multi  # default to multi-config path for error messages


def cmd_integration(args):
    """Run integration tests (Python port of run_pipeline.ps1)."""
    runtime_prefix = args.prefix
    config = args.config
    generator = args.generator
    keep_temp = args.keep_temp

    cmake_arch = ["-A", "x64"] if "Visual Studio" in generator else []

    temp_dir = Path(tempfile.mkdtemp(prefix="cil2cpp_integration_"))
    runner = TestRunner()

    header("CIL2CPP Integration Test")
    print(f"  Repo:    {REPO_ROOT}")
    print(f"  Runtime: {runtime_prefix}")
    print(f"  Config:  {config}")
    print(f"  Temp:    {temp_dir}")

    # ===== Phase 0: Prerequisites =====
    header("Phase 0: Prerequisites")

    def check_dotnet():
        r = run(["dotnet", "--version"], capture=True, check=False)
        if r.returncode != 0:
            raise RuntimeError("dotnet not found")
        return r.stdout.strip()

    def check_cmake():
        r = run(["cmake", "--version"], capture=True, check=False)
        if r.returncode != 0:
            raise RuntimeError("cmake not found")
        return r.stdout.strip().split("\n")[0]

    def check_runtime():
        cfg = Path(runtime_prefix) / "lib/cmake/cil2cpp/cil2cppConfig.cmake"
        if not cfg.exists():
            raise RuntimeError(f"cil2cppConfig.cmake not found at {cfg}")

    runner.step("dotnet SDK available", check_dotnet)
    runner.step("CMake available", check_cmake)
    runner.step(f"Runtime installed at {runtime_prefix}", check_runtime)

    # ===== Phase 1: HelloWorld =====
    header("Phase 1: HelloWorld (executable with entry point)")

    hw_sample = SAMPLES_DIR / "HelloWorld" / "HelloWorld.csproj"
    hw_output = temp_dir / "helloworld_output"
    hw_build = temp_dir / "helloworld_build"

    def hw_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(hw_sample), "-o", str(hw_output)],
            capture=True)

    def hw_files_exist():
        for f in ["HelloWorld.h", "HelloWorld.cpp", "main.cpp", "CMakeLists.txt"]:
            if not (hw_output / f).exists():
                raise RuntimeError(f"Missing: {f}")

    def hw_cmake_configure():
        run(["cmake", "-B", str(hw_build), "-S", str(hw_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)

    def hw_cmake_build():
        run(["cmake", "--build", str(hw_build), "--config", config],
            capture=True)

    def hw_run_verify():
        exe = _exe_path(hw_build, config, "HelloWorld")
        if not exe.exists():
            raise RuntimeError(f"Executable not found: {exe}")
        r = subprocess.run([str(exe)], capture_output=True, text=True, check=False)
        if r.returncode != 0:
            raise RuntimeError(f"HelloWorld exited with code {r.returncode}")
        got = r.stdout.strip()
        expected = "Hello, CIL2CPP!\n30\n42"
        if got != expected:
            raise RuntimeError(f"Output mismatch.\nExpected:\n{expected}\nGot:\n{got}")

    runner.step("Codegen HelloWorld", hw_codegen)
    runner.step("Generated files exist (*.h, *.cpp, main.cpp, CMakeLists.txt)", hw_files_exist)
    runner.step("CMake configure", hw_cmake_configure)
    runner.step(f"CMake build ({config})", hw_cmake_build)
    runner.step("Run HelloWorld and verify output", hw_run_verify)

    # ===== Phase 2: Library project =====
    header("Phase 2: Library project (no entry point)")

    lib_sample = temp_dir / "lib_sample"
    lib_output = temp_dir / "lib_output"
    lib_build = temp_dir / "lib_build"

    def lib_create():
        lib_sample.mkdir(parents=True, exist_ok=True)
        (lib_sample / "MathLib.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk">\n'
            "  <PropertyGroup>\n"
            "    <TargetFramework>net8.0</TargetFramework>\n"
            "    <OutputType>Library</OutputType>\n"
            "  </PropertyGroup>\n"
            "</Project>\n"
        )
        (lib_sample / "MathHelper.cs").write_text(
            "public class MathHelper\n"
            "{\n"
            "    private int _value;\n"
            "    public int Add(int a, int b) { return a + b; }\n"
            "    public int Multiply(int a, int b) { return a * b; }\n"
            "    public void SetValue(int v) { _value = v; }\n"
            "    public int GetValue() { return _value; }\n"
            "}\n"
        )

    def lib_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(lib_sample / "MathLib.csproj"),
             "-o", str(lib_output)],
            capture=True)

    def lib_verify_structure():
        cmake_txt = (lib_output / "CMakeLists.txt").read_text(encoding="utf-8", errors="replace")
        if "add_library" not in cmake_txt:
            raise RuntimeError("CMakeLists.txt missing add_library")
        if (lib_output / "main.cpp").exists():
            raise RuntimeError("Library should not have main.cpp")

    def lib_build_fn():
        run(["cmake", "-B", str(lib_build), "-S", str(lib_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)
        run(["cmake", "--build", str(lib_build), "--config", config],
            capture=True)

    runner.step("Create temporary class library project", lib_create)
    runner.step("Codegen library project", lib_codegen)
    runner.step("Library generates add_library (no main.cpp)", lib_verify_structure)
    runner.step("Library CMake configure + build", lib_build_fn)

    # ===== Phase 3: Debug configuration =====
    header("Phase 3: Debug configuration")

    dbg_output = temp_dir / "debug_output"
    dbg_build = temp_dir / "debug_build"

    def dbg_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(hw_sample), "-o", str(dbg_output), "-c", "Debug"],
            capture=True)

    def dbg_has_line_directives():
        src = (dbg_output / "HelloWorld.cpp").read_text(encoding="utf-8", errors="replace")
        if "#line" not in src:
            raise RuntimeError("No #line directives found in Debug output")

    def dbg_has_il_comments():
        src = (dbg_output / "HelloWorld.cpp").read_text(encoding="utf-8", errors="replace")
        if not re.search(r"/\* IL_", src):
            raise RuntimeError("No IL offset comments found in Debug output")

    def dbg_build_and_run():
        run(["cmake", "-B", str(dbg_build), "-S", str(dbg_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)
        run(["cmake", "--build", str(dbg_build), "--config", "Debug"],
            capture=True)
        exe = _exe_path(dbg_build, "Debug", "HelloWorld")
        r = subprocess.run([str(exe)], capture_output=True, text=True, check=False)
        got = r.stdout.strip()
        expected = "Hello, CIL2CPP!\n30\n42"
        if got != expected:
            raise RuntimeError(
                f"Debug output mismatch.\nExpected:\n{expected}\nGot:\n{got}")

    runner.step("Codegen HelloWorld in Debug mode", dbg_codegen)
    runner.step("Debug output contains #line directives", dbg_has_line_directives)
    runner.step("Debug output contains IL offset comments", dbg_has_il_comments)
    runner.step("Debug build + run produces same output", dbg_build_and_run)

    # ===== Phase 4: String literals =====
    header("Phase 4: String literals")

    def str_has_literal_calls():
        src = (hw_output / "HelloWorld.cpp").read_text(encoding="utf-8", errors="replace")
        if "string_literal" not in src:
            raise RuntimeError("No string_literal calls found")
        if "Hello, CIL2CPP!" not in src:
            raise RuntimeError("String content not found")

    def str_has_init_fn():
        hdr = (hw_output / "HelloWorld.h").read_text(encoding="utf-8", errors="replace")
        if "__init_string_literals" not in hdr:
            raise RuntimeError("No __init_string_literals in header")

    runner.step("HelloWorld source contains string_literal calls", str_has_literal_calls)
    runner.step("HelloWorld source contains __init_string_literals", str_has_init_fn)

    # ===== Phase 5: Cross-project codegen =====
    header("Phase 5: Cross-project codegen (MathLib + MultiAssemblyTest)")

    multi_sample = SAMPLES_DIR / "MultiAssemblyTest" / "MultiAssemblyTest.csproj"
    multi_output = temp_dir / "multi_output"

    def multi_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(multi_sample), "-o", str(multi_output)],
            capture=True)

    def multi_files_exist():
        for f in ["MultiAssemblyTest.h", "MultiAssemblyTest.cpp", "main.cpp", "CMakeLists.txt"]:
            if not (multi_output / f).exists():
                raise RuntimeError(f"Missing: {f}")

    def multi_header_has_mathlib_types():
        hdr = (multi_output / "MultiAssemblyTest.h").read_text(encoding="utf-8", errors="replace")
        if "MathLib_MathUtils" not in hdr:
            raise RuntimeError("MathUtils type not found in header")
        if "MathLib_Counter" not in hdr:
            raise RuntimeError("Counter type not found in header")

    def multi_source_has_cross_assembly_calls():
        src = (multi_output / "MultiAssemblyTest.cpp").read_text(encoding="utf-8", errors="replace")
        if "MathLib_MathUtils_Add" not in src:
            raise RuntimeError("Cross-assembly MathUtils_Add call not found")
        if "MathLib_Counter" not in src:
            raise RuntimeError("Cross-assembly Counter usage not found")

    def multi_source_has_entry_point():
        main = (multi_output / "main.cpp").read_text(encoding="utf-8", errors="replace")
        if "Program_Main" not in main:
            raise RuntimeError("Entry point not found in main.cpp")

    runner.step("Cross-project codegen", multi_codegen)
    runner.step("Generated files exist", multi_files_exist)
    runner.step("Header contains MathLib types", multi_header_has_mathlib_types)
    runner.step("Source has cross-assembly method calls", multi_source_has_cross_assembly_calls)
    runner.step("Main has entry point", multi_source_has_entry_point)

    # ===== Cleanup =====
    header("Cleanup")

    if keep_temp:
        print(f"  Keeping temp directory: {temp_dir}")
    else:
        try:
            shutil.rmtree(temp_dir)
            print("  Cleaned up temp directory")
        except Exception:
            warn(f"  Warning: Could not clean up {temp_dir}")

    # ===== Results =====
    header("Results")
    return runner.summary()


# ===== cmd_setup =====

def cmd_setup(args):
    """Check prerequisites and install optional dev dependencies."""
    header("Checking core prerequisites")
    ok_count = 0
    total_core = 0

    def _check(name, cmd, parse=None):
        nonlocal ok_count, total_core
        total_core += 1
        print(f"  {name:<25s}", end="", flush=True)
        path = which_tool(cmd[0])
        if not path:
            error("NOT FOUND")
            return False
        try:
            r = subprocess.run(cmd, capture_output=True, text=True, check=False)
            ver = parse(r.stdout) if parse else r.stdout.strip().split("\n")[0]
            ok_count += 1
            success(f"OK  ({ver})")
            return True
        except Exception:
            ok_count += 1
            success(f"OK  ({path})")
            return True

    _check("dotnet SDK", ["dotnet", "--version"])
    _check("CMake", ["cmake", "--version"], lambda s: s.strip().split("\n")[0])
    _check("Python", [sys.executable, "--version"])
    _check("Git", ["git", "--version"], lambda s: s.strip())

    if IS_WINDOWS:
        # cl.exe is only on PATH inside VS Developer Command Prompt.
        # Check for VS installation via vswhere instead.
        total_core += 1
        print(f"  {'MSVC (Visual Studio)':<25s}", end="", flush=True)
        vswhere = Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)")) / \
            "Microsoft Visual Studio/Installer/vswhere.exe"
        if vswhere.exists():
            r = subprocess.run(
                [str(vswhere), "-latest", "-property", "installationVersion"],
                capture_output=True, text=True, check=False)
            ver = r.stdout.strip()
            if ver:
                ok_count += 1
                success(f"OK  (VS {ver})")
            else:
                error("NOT FOUND (no VS installation detected)")
        elif which_tool("cl"):
            ok_count += 1
            success("OK  (cl.exe on PATH)")
        else:
            error("NOT FOUND")
    else:
        _check("C++ compiler (g++)", ["g++", "--version"], lambda s: s.strip().split("\n")[0])

    # ----- Optional dev tools -----
    header("Optional dev dependencies")
    install_count = 0

    # ReportGenerator (.NET global tool)
    print(f"  {'ReportGenerator':<25s}", end="", flush=True)
    if which_tool("reportgenerator"):
        success("OK  (already installed)")
    else:
        warn("NOT FOUND")
        print("    Installing via: dotnet tool install -g dotnet-reportgenerator-globaltool")
        try:
            run(["dotnet", "tool", "install", "-g",
                 "dotnet-reportgenerator-globaltool"], check=True)
            install_count += 1
            success("    Installed successfully")
        except subprocess.CalledProcessError:
            # May already be installed but not on PATH, or update needed
            try:
                run(["dotnet", "tool", "update", "-g",
                     "dotnet-reportgenerator-globaltool"], check=True)
                install_count += 1
                success("    Updated successfully")
            except subprocess.CalledProcessError:
                error("    Failed to install ReportGenerator")

    # OpenCppCoverage (Windows only)
    if IS_WINDOWS:
        print(f"  {'OpenCppCoverage':<25s}", end="", flush=True)
        if _find_opencppcoverage():
            success("OK  (already installed)")
        else:
            warn("NOT FOUND")
            print("    Installing via: winget install OpenCppCoverage.OpenCppCoverage")
            try:
                run(["winget", "install", "OpenCppCoverage.OpenCppCoverage",
                     "--accept-source-agreements", "--accept-package-agreements"],
                    check=True)
                install_count += 1
                success("    Installed successfully")
            except subprocess.CalledProcessError:
                error("    Failed to install OpenCppCoverage")
                print("    Manual install: https://github.com/OpenCppCoverage/OpenCppCoverage/releases")
    else:
        # Linux: lcov
        print(f"  {'lcov':<25s}", end="", flush=True)
        if which_tool("lcov"):
            success("OK  (already installed)")
        else:
            warn("NOT FOUND")
            print("    Install with: sudo apt install lcov  (or your distro's package manager)")

        print(f"  {'lcov_cobertura':<25s}", end="", flush=True)
        if which_tool("lcov_cobertura"):
            success("OK  (already installed)")
        else:
            warn("NOT FOUND")
            print("    Install with: pip install lcov_cobertura")

    # ----- Summary -----
    header("Setup summary")
    success(f"  Core tools: {ok_count}/{total_core} found")
    if install_count:
        success(f"  Installed {install_count} tool(s) this session")
    print()
    print("  If you just installed tools, you may need to restart your terminal")
    print("  for PATH changes to take effect.")
    return 0


# ===== Interactive Menu =====

def interactive_menu():
    """Show interactive menu when no arguments given."""
    menu = [
        ("Build compiler",         "dotnet build",            lambda: cmd_build(argparse.Namespace(compiler=True, runtime=False, config="Release"))),
        ("Build runtime",          "cmake --build",           lambda: cmd_build(argparse.Namespace(compiler=False, runtime=True, config="Release"))),
        ("Build all",              "compiler + runtime",      lambda: cmd_build(argparse.Namespace(compiler=False, runtime=False, config="Release"))),
        ("Test compiler",          "dotnet test (166 tests)", lambda: cmd_test(argparse.Namespace(compiler=True, runtime=False, integration=False, all=False, coverage=False))),
        ("Test runtime",           "ctest (110 tests)",       lambda: cmd_test(argparse.Namespace(compiler=False, runtime=True, integration=False, all=False, coverage=False))),
        ("Test all (unit)",        "compiler + runtime",      lambda: cmd_test(argparse.Namespace(compiler=False, runtime=False, integration=False, all=False, coverage=False))),
        ("Test + coverage report", "HTML coverage report",    lambda: cmd_test(argparse.Namespace(compiler=True, runtime=False, integration=False, all=False, coverage=True))),
        ("Integration tests",     "full pipeline test",      lambda: cmd_integration(argparse.Namespace(prefix=DEFAULT_PREFIX, config="Release", generator=DEFAULT_GENERATOR, keep_temp=False))),
        ("Install runtime",       f"cmake --install → {DEFAULT_PREFIX}", lambda: cmd_install(argparse.Namespace(prefix=DEFAULT_PREFIX, config="both"))),
        ("Codegen HelloWorld",     "quick codegen test",      lambda: cmd_codegen(argparse.Namespace(sample="HelloWorld", input=None, output="output", config="Release"))),
        ("Setup dev environment",  "check & install tools",   lambda: cmd_setup(argparse.Namespace())),
    ]

    while True:
        print(f"\n{_c('36', 'CIL2CPP Developer CLI')}")
        print("=" * 40)
        for i, (name, desc, _) in enumerate(menu, 1):
            print(f"  {i:2d}) {name:<25s} {_c('90', desc)}")
        print(f"   0) Exit")

        try:
            choice = input(f"\nChoice [0-{len(menu)}]: ").strip()
        except (EOFError, KeyboardInterrupt):
            print()
            return 0

        if choice == "0" or choice == "":
            return 0

        try:
            idx = int(choice) - 1
            if 0 <= idx < len(menu):
                result = menu[idx][2]()
                if result:
                    error(f"\nCommand exited with code {result}")
            else:
                warn("Invalid choice")
        except ValueError:
            warn("Invalid input")
        except subprocess.CalledProcessError:
            pass  # already printed by run()
        except KeyboardInterrupt:
            print("\n  Interrupted")


# ===== Main =====

def main():
    parser = argparse.ArgumentParser(
        prog="dev",
        description="CIL2CPP Developer CLI - build, test, install, codegen",
    )
    subparsers = parser.add_subparsers(dest="command")

    # build
    p_build = subparsers.add_parser("build", help="Build compiler and/or runtime")
    p_build.add_argument("--compiler", action="store_true", help="Build compiler only")
    p_build.add_argument("--runtime", action="store_true", help="Build runtime only")
    p_build.add_argument("--config", default="Release", choices=["Debug", "Release"])

    # test
    p_test = subparsers.add_parser("test", help="Run tests")
    p_test.add_argument("--compiler", action="store_true", help="Compiler tests only")
    p_test.add_argument("--runtime", action="store_true", help="Runtime tests only")
    p_test.add_argument("--integration", action="store_true", help="Integration tests only")
    p_test.add_argument("--all", action="store_true", help="All tests")
    p_test.add_argument("--coverage", action="store_true", help="Generate coverage report")

    # install
    p_install = subparsers.add_parser("install", help="Install runtime to prefix")
    p_install.add_argument("--prefix", default=DEFAULT_PREFIX, help=f"Install prefix (default: {DEFAULT_PREFIX})")
    p_install.add_argument("--config", default="both", choices=["Debug", "Release", "both"])

    # codegen
    p_codegen = subparsers.add_parser("codegen", help="Generate C++ from C# project")
    p_codegen.add_argument("sample", nargs="?", help="Sample name or .csproj path")
    p_codegen.add_argument("-i", "--input", help="Input .csproj path")
    p_codegen.add_argument("-o", "--output", default="output", help="Output directory")
    p_codegen.add_argument("-c", "--config", default="Release", choices=["Debug", "Release"])

    # integration
    p_integ = subparsers.add_parser("integration", help="Run integration tests")
    p_integ.add_argument("--prefix", default=DEFAULT_PREFIX, help=f"Runtime prefix (default: {DEFAULT_PREFIX})")
    p_integ.add_argument("--config", default="Release", choices=["Debug", "Release"])
    p_integ.add_argument("--generator", default=DEFAULT_GENERATOR, help=f"CMake generator (default: {DEFAULT_GENERATOR})")
    p_integ.add_argument("--keep-temp", action="store_true", help="Keep temp directory")

    # setup
    subparsers.add_parser("setup", help="Check prerequisites and install optional dev dependencies")

    args = parser.parse_args()

    if args.command is None:
        return interactive_menu() or 0
    elif args.command == "build":
        return cmd_build(args)
    elif args.command == "test":
        return cmd_test(args)
    elif args.command == "install":
        return cmd_install(args)
    elif args.command == "codegen":
        return cmd_codegen(args)
    elif args.command == "integration":
        return cmd_integration(args)
    elif args.command == "setup":
        return cmd_setup(args)

    return 0


if __name__ == "__main__":
    sys.exit(main())
