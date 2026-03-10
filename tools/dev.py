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
TESTPROJECTS_DIR = REPO_ROOT / "tests"

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


def run(cmd, *, cwd=None, check=True, capture=False, timeout=None):
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
            timeout=timeout,
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
                cmd = ["dotnet", "test", str(TEST_PROJECT), "--verbosity", "minimal"]
                test_filter = getattr(args, "filter", None)
                if test_filter:
                    cmd += ["--filter", test_filter]
                run(cmd)
                success("Compiler tests passed")
            except subprocess.CalledProcessError:
                error("Compiler tests FAILED")
                failures += 1

    if run_runtime:
        rt_config = getattr(args, "config", "Release")
        header(f"Runtime tests (Google Test, {rt_config})")
        build_dir = RUNTIME_TESTS_DIR / "build"
        try:
            run(["cmake", "-B", str(build_dir), "-S", str(RUNTIME_TESTS_DIR),
                 "-G", DEFAULT_GENERATOR] + (["-A", "x64"] if IS_WINDOWS else []),
                capture=True)
            run(["cmake", "--build", str(build_dir), "--config", rt_config])
            run(["ctest", "--test-dir", str(build_dir), "-C", rt_config,
                 "--output-on-failure"])
            success("Runtime tests passed")
        except subprocess.CalledProcessError:
            error("Runtime tests FAILED")
            failures += 1

    if run_integ:
        header("Integration tests")
        ns = argparse.Namespace(
            prefix=getattr(args, "prefix", DEFAULT_PREFIX),
            config=getattr(args, "config", "Release"),
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
            csproj = TESTPROJECTS_DIR / name / f"{name}.csproj"
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


# ===== cmd_compile =====

def cmd_compile(args):
    """One-step compile: .csproj → C++ code → native executable."""
    # 1. Resolve input .csproj (same logic as cmd_codegen)
    if args.sample:
        name = args.sample
        if not name.endswith(".csproj") and "/" not in name and "\\" not in name:
            csproj = TESTPROJECTS_DIR / name / f"{name}.csproj"
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
    prefix = args.prefix
    project_name = csproj.stem

    # 2. Codegen
    header(f"Step 1/3: Codegen ({project_name}, {config})")
    try:
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(csproj), "-o", str(output), "-c", config])
    except subprocess.CalledProcessError:
        error("Codegen failed")
        return 1

    # 3. CMake configure
    build_dir = output / "build"
    header("Step 2/3: CMake configure")
    try:
        cmake_cmd = ["cmake", "-B", str(build_dir), "-S", str(output),
                     "-G", DEFAULT_GENERATOR,
                     f"-DCMAKE_PREFIX_PATH={prefix}"]
        if IS_WINDOWS and "Visual Studio" in DEFAULT_GENERATOR:
            cmake_cmd += ["-A", "x64"]
        run(cmake_cmd)
    except subprocess.CalledProcessError:
        error("CMake configure failed")
        return 1

    # 4. CMake build
    header(f"Step 3/3: CMake build ({config})")
    try:
        run(["cmake", "--build", str(build_dir), "--config", config])
    except subprocess.CalledProcessError:
        error("CMake build failed")
        return 1

    # 5. Find executable
    exe = _exe_path(build_dir, config, project_name)
    if exe.exists():
        success(f"\nExecutable: {exe}")
    else:
        # Library project — look for .lib/.a
        warn(f"\nNo executable found (library project?)")
        success(f"Build output: {build_dir / config}")

    # 6. Optional: run
    if getattr(args, "run_exe", False) and exe.exists():
        header(f"Running {project_name}")
        result = subprocess.run([str(exe)], check=False)
        if result.returncode != 0:
            error(f"Process exited with code {result.returncode}")
            return result.returncode

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


def _get_dotnet_output(csproj_path):
    """Run a C# project with 'dotnet run' and return its stdout (stripped)."""
    r = subprocess.run(
        ["dotnet", "run", "--project", str(csproj_path)],
        capture_output=True, text=True, check=False,
        encoding="utf-8", errors="replace",
        timeout=60,
    )
    if r.returncode != 0:
        raise RuntimeError(
            f"dotnet run failed (exit {r.returncode})\nstdout: {r.stdout}\nstderr: {r.stderr}")
    return r.stdout.strip()


def _compare_output_with_skip(got, expected, skip_lines=None):
    """Compare output line-by-line, skipping specified 0-based line indices.

    Use this for tests where specific lines have non-deterministic output
    (e.g., hash codes based on memory addresses).
    """
    got_lines = got.strip().split('\n')
    exp_lines = expected.strip().split('\n')
    if len(got_lines) != len(exp_lines):
        raise RuntimeError(
            f"Line count mismatch: got {len(got_lines)}, expected {len(exp_lines)}\n"
            f"  Got:\n{got}\n  Expected:\n{expected}")
    skip = skip_lines or set()
    mismatches = []
    for i, (g, e) in enumerate(zip(got_lines, exp_lines)):
        if i in skip:
            continue
        if g.strip() != e.strip():
            mismatches.append(f"  line {i+1}: got '{g.strip()}', expected '{e.strip()}'")
    if mismatches:
        raise RuntimeError("Output mismatch:\n" + "\n".join(mismatches))


def cmd_integration(args):
    """Run integration tests — active projects: HelloWorld, ArrayTest, FeatureTest.

    Other test projects are disabled and will be enabled phase-by-phase as issues are fixed.
    """
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

    hw_sample = TESTPROJECTS_DIR / "HelloWorld" / "HelloWorld.csproj"
    hw_output = temp_dir / "helloworld_output"
    hw_build = temp_dir / "helloworld_build"

    # Get .NET reference output first
    dotnet_hw_output = None

    def hw_dotnet_run():
        nonlocal dotnet_hw_output
        dotnet_hw_output = _get_dotnet_output(hw_sample)
        print(f"    .NET output: {repr(dotnet_hw_output[:200])}")

    def hw_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(hw_sample), "-o", str(hw_output)],
            capture=True)

    def hw_files_exist():
        for f in ["HelloWorld.h", "HelloWorld_data.cpp", "HelloWorld_stubs.cpp",
                   "main.cpp", "CMakeLists.txt"]:
            if not (hw_output / f).exists():
                raise RuntimeError(f"Missing: {f}")
        # At least one methods file must exist
        if not list(hw_output.glob("HelloWorld_methods_*.cpp")):
            raise RuntimeError("No HelloWorld_methods_*.cpp files found")

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
            raise RuntimeError(f"HelloWorld exited with code {r.returncode}\nstderr: {r.stderr}")
        got = r.stdout.strip()
        if got != dotnet_hw_output:
            raise RuntimeError(
                f"Output mismatch vs .NET:\n"
                f"  .NET output:\n{dotnet_hw_output}\n"
                f"  C++ output:\n{got}")

    runner.step("Get .NET reference output", hw_dotnet_run)
    runner.step("Codegen HelloWorld", hw_codegen)
    runner.step("Generated files exist (*.h, *.cpp, main.cpp, CMakeLists.txt)", hw_files_exist)
    runner.step("CMake configure", hw_cmake_configure)
    runner.step(f"CMake build ({config})", hw_cmake_build)
    runner.step("Run and compare C++ vs .NET output", hw_run_verify)

    # ===== Phase 2: ArrayTest =====
    header("Phase 2: ArrayTest (array operations)")

    at_sample = TESTPROJECTS_DIR / "ArrayTest" / "ArrayTest.csproj"
    at_output = temp_dir / "arraytest_output"
    at_build = temp_dir / "arraytest_build"

    dotnet_at_output = None

    def at_dotnet_run():
        nonlocal dotnet_at_output
        dotnet_at_output = _get_dotnet_output(at_sample)
        print(f"    .NET output: {repr(dotnet_at_output[:200])}")

    def at_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(at_sample), "-o", str(at_output)],
            capture=True)

    def at_files_exist():
        for f in ["ArrayTest.h", "ArrayTest_data.cpp", "ArrayTest_stubs.cpp",
                   "main.cpp", "CMakeLists.txt"]:
            if not (at_output / f).exists():
                raise RuntimeError(f"Missing: {f}")
        if not list(at_output.glob("ArrayTest_methods_*.cpp")):
            raise RuntimeError("No ArrayTest_methods_*.cpp files found")

    def at_cmake_configure():
        run(["cmake", "-B", str(at_build), "-S", str(at_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)

    def at_cmake_build():
        run(["cmake", "--build", str(at_build), "--config", config],
            capture=True)

    def at_run_verify():
        exe = _exe_path(at_build, config, "ArrayTest")
        if not exe.exists():
            raise RuntimeError(f"Executable not found: {exe}")
        r = subprocess.run([str(exe)], capture_output=True, text=True, check=False)
        if r.returncode != 0:
            raise RuntimeError(f"ArrayTest exited with code {r.returncode}\nstderr: {r.stderr}")
        got = r.stdout.strip()
        if got != dotnet_at_output:
            raise RuntimeError(
                f"Output mismatch vs .NET:\n"
                f"  .NET output:\n{dotnet_at_output}\n"
                f"  C++ output:\n{got}")

    runner.step("Get .NET reference output", at_dotnet_run)
    runner.step("Codegen ArrayTest", at_codegen)
    runner.step("Generated files exist (*.h, *.cpp, main.cpp, CMakeLists.txt)", at_files_exist)
    runner.step("CMake configure", at_cmake_configure)
    runner.step(f"CMake build ({config})", at_cmake_build)
    runner.step("Run and compare C++ vs .NET output", at_run_verify)

    # ===== Phase 3: FeatureTest (100+ language features) =====
    header("Phase 3: FeatureTest (100+ language features)")

    ft_sample = TESTPROJECTS_DIR / "FeatureTest" / "FeatureTest.csproj"
    ft_output = temp_dir / "featuretest_output"
    ft_build = ft_output / "build"
    dotnet_ft_output = ""

    def ft_dotnet_run():
        nonlocal dotnet_ft_output
        dotnet_ft_output = _get_dotnet_output(ft_sample)
        print(f"    .NET output: {repr(dotnet_ft_output[:200])}")

    def ft_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(ft_sample), "-o", str(ft_output)],
            capture=True)

    def ft_files_exist():
        for f in ["FeatureTest.h", "FeatureTest_data.cpp", "FeatureTest_stubs.cpp",
                   "main.cpp", "CMakeLists.txt"]:
            if not (ft_output / f).exists():
                raise RuntimeError(f"Missing: {f}")
        if not list(ft_output.glob("FeatureTest_methods_*.cpp")):
            raise RuntimeError("No FeatureTest_methods_*.cpp files found")

    def ft_cmake_configure():
        run(["cmake", "-B", str(ft_build), "-S", str(ft_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)

    def ft_cmake_build():
        run(["cmake", "--build", str(ft_build), "--config", config],
            capture=True)

    def ft_run_verify():
        exe = _exe_path(ft_build, config, "FeatureTest")
        if not exe.exists():
            raise RuntimeError(f"Executable not found: {exe}")
        r = subprocess.run([str(exe)], capture_output=True, text=True, check=False,
                           encoding="utf-8", errors="replace", timeout=60)
        if r.returncode != 0:
            raise RuntimeError(f"FeatureTest exited with code {r.returncode}\nstderr: {r.stderr}")
        got = r.stdout.strip()
        # Lines 41 and 100 (0-based: 40, 99) contain Object.GetHashCode() values
        # which are address-based and non-deterministic — skip them.
        _compare_output_with_skip(got, dotnet_ft_output, skip_lines={40, 99})

    runner.step("Get .NET reference output", ft_dotnet_run)
    runner.step("Codegen FeatureTest", ft_codegen)
    runner.step("Generated files exist", ft_files_exist)
    runner.step("CMake configure", ft_cmake_configure)
    runner.step(f"CMake build ({config})", ft_cmake_build)
    runner.step("Run and compare C++ vs .NET output", ft_run_verify)

    # ===== Phase 4: ArglistTest (varargs + TypedReference) =====
    header("Phase 4: ArglistTest (varargs, mkrefany, refanyval)")

    arg_sample = TESTPROJECTS_DIR / "ArglistTest" / "ArglistTest.csproj"
    arg_output = temp_dir / "arglist_output"
    arg_build = temp_dir / "arglist_build"

    dotnet_arg_output = None

    def arg_dotnet_run():
        nonlocal dotnet_arg_output
        dotnet_arg_output = _get_dotnet_output(arg_sample)
        print(f"    .NET output: {repr(dotnet_arg_output[:200])}")

    def arg_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(arg_sample), "-o", str(arg_output)],
            capture=True)

    def arg_files_exist():
        for f in ["ArglistTest.h", "ArglistTest_data.cpp", "ArglistTest_stubs.cpp",
                   "main.cpp", "CMakeLists.txt"]:
            if not (arg_output / f).exists():
                raise RuntimeError(f"Missing: {f}")

    def arg_cmake_configure():
        run(["cmake", "-B", str(arg_build), "-S", str(arg_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)

    def arg_cmake_build():
        run(["cmake", "--build", str(arg_build), "--config", config],
            capture=True)

    def arg_run_verify():
        exe = _exe_path(arg_build, config, "ArglistTest")
        if not exe.exists():
            raise RuntimeError(f"Executable not found: {exe}")
        r = subprocess.run([str(exe)], capture_output=True, text=True, check=False)
        if r.returncode != 0:
            raise RuntimeError(f"ArglistTest exited with code {r.returncode}\nstderr: {r.stderr}")
        got = r.stdout.strip()
        if got != dotnet_arg_output:
            raise RuntimeError(
                f"Output mismatch vs .NET:\n"
                f"  .NET output:\n{dotnet_arg_output}\n"
                f"  C++ output:\n{got}")

    runner.step("Get .NET reference output", arg_dotnet_run)
    runner.step("Codegen ArglistTest", arg_codegen)
    runner.step("Generated files exist", arg_files_exist)
    runner.step("CMake configure", arg_cmake_configure)
    runner.step(f"CMake build ({config})", arg_cmake_build)
    runner.step("Run and compare C++ vs .NET output", arg_run_verify)

    # ===== Phase 5: MultiAssemblyTest (cross-project, references MathLib) =====
    header("Phase 5: MultiAssemblyTest (cross-assembly references)")

    multi_sample = TESTPROJECTS_DIR / "MultiAssemblyTest" / "MultiAssemblyTest.csproj"
    multi_output = temp_dir / "multi_output"
    multi_build = temp_dir / "multi_build"

    dotnet_multi_output = None

    def multi_dotnet_run():
        nonlocal dotnet_multi_output
        dotnet_multi_output = _get_dotnet_output(multi_sample)
        print(f"    .NET output: {repr(dotnet_multi_output[:200])}")

    def multi_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(multi_sample), "-o", str(multi_output)],
            capture=True)

    def multi_files_exist():
        for f in ["MultiAssemblyTest.h", "MultiAssemblyTest_data.cpp",
                   "MultiAssemblyTest_stubs.cpp", "main.cpp", "CMakeLists.txt"]:
            if not (multi_output / f).exists():
                raise RuntimeError(f"Missing: {f}")

    def multi_cmake_configure():
        run(["cmake", "-B", str(multi_build), "-S", str(multi_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)

    def multi_cmake_build():
        run(["cmake", "--build", str(multi_build), "--config", config],
            capture=True)

    def multi_run_verify():
        exe = _exe_path(multi_build, config, "MultiAssemblyTest")
        if not exe.exists():
            raise RuntimeError(f"Executable not found: {exe}")
        r = subprocess.run([str(exe)], capture_output=True, text=True, check=False)
        if r.returncode != 0:
            raise RuntimeError(f"MultiAssemblyTest exited with code {r.returncode}\nstderr: {r.stderr}")
        got = r.stdout.strip()
        if got != dotnet_multi_output:
            raise RuntimeError(
                f"Output mismatch vs .NET:\n"
                f"  .NET output:\n{dotnet_multi_output}\n"
                f"  C++ output:\n{got}")

    runner.step("Get .NET reference output", multi_dotnet_run)
    runner.step("Codegen MultiAssemblyTest", multi_codegen)
    runner.step("Generated files exist", multi_files_exist)
    runner.step("CMake configure", multi_cmake_configure)
    runner.step(f"CMake build ({config})", multi_cmake_build)
    runner.step("Run and compare C++ vs .NET output", multi_run_verify)

    # ===== Phase 6: SystemIOTest (File/Path/Directory I/O) =====
    header("Phase 6: SystemIOTest (File, Path, Directory I/O)")

    sio_sample = TESTPROJECTS_DIR / "SystemIOTest" / "SystemIOTest.csproj"
    sio_output = temp_dir / "sio_output"
    sio_build = temp_dir / "sio_build"

    dotnet_sio_output = None

    def sio_dotnet_run():
        nonlocal dotnet_sio_output
        dotnet_sio_output = _get_dotnet_output(sio_sample)
        print(f"    .NET output: {repr(dotnet_sio_output[:200])}")

    def sio_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(sio_sample), "-o", str(sio_output)],
            capture=True)

    def sio_files_exist():
        for f in ["SystemIOTest.h", "SystemIOTest_data.cpp", "SystemIOTest_stubs.cpp",
                   "main.cpp", "CMakeLists.txt"]:
            if not (sio_output / f).exists():
                raise RuntimeError(f"Missing: {f}")

    def sio_cmake_configure():
        run(["cmake", "-B", str(sio_build), "-S", str(sio_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)

    def sio_cmake_build():
        run(["cmake", "--build", str(sio_build), "--config", config],
            capture=True)

    def sio_run_verify():
        exe = _exe_path(sio_build, config, "SystemIOTest")
        if not exe.exists():
            raise RuntimeError(f"Executable not found: {exe}")
        r = subprocess.run([str(exe)], capture_output=True, text=True, check=False)
        if r.returncode != 0:
            raise RuntimeError(f"SystemIOTest exited with code {r.returncode}\nstderr: {r.stderr}")
        got = r.stdout.strip()
        if got != dotnet_sio_output:
            raise RuntimeError(
                f"Output mismatch vs .NET:\n"
                f"  .NET output:\n{dotnet_sio_output}\n"
                f"  C++ output:\n{got}")

    runner.step("Get .NET reference output", sio_dotnet_run)
    runner.step("Codegen SystemIOTest", sio_codegen)
    runner.step("Generated files exist", sio_files_exist)
    runner.step("CMake configure", sio_cmake_configure)
    runner.step(f"CMake build ({config})", sio_cmake_build)
    runner.step("Run and compare C++ vs .NET output", sio_run_verify)

    # ===== Phase 7: FileStreamTest (FileStream, StreamReader/StreamWriter) =====
    header("Phase 7: FileStreamTest (FileStream, StreamReader, StreamWriter)")

    fst_sample = TESTPROJECTS_DIR / "FileStreamTest" / "FileStreamTest.csproj"
    fst_output = temp_dir / "fst_output"
    fst_build = temp_dir / "fst_build"

    dotnet_fst_output = None

    def fst_dotnet_run():
        nonlocal dotnet_fst_output
        dotnet_fst_output = _get_dotnet_output(fst_sample)
        print(f"    .NET output: {repr(dotnet_fst_output[:200])}")

    def fst_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(fst_sample), "-o", str(fst_output)],
            capture=True)

    def fst_files_exist():
        for f in ["FileStreamTest.h", "FileStreamTest_data.cpp", "FileStreamTest_stubs.cpp",
                   "main.cpp", "CMakeLists.txt"]:
            if not (fst_output / f).exists():
                raise RuntimeError(f"Missing: {f}")

    def fst_cmake_configure():
        run(["cmake", "-B", str(fst_build), "-S", str(fst_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)

    def fst_cmake_build():
        run(["cmake", "--build", str(fst_build), "--config", config],
            capture=True)

    def fst_run_verify():
        exe = _exe_path(fst_build, config, "FileStreamTest")
        if not exe.exists():
            raise RuntimeError(f"Executable not found: {exe}")
        r = subprocess.run([str(exe)], capture_output=True, text=True, check=False)
        if r.returncode != 0:
            raise RuntimeError(f"FileStreamTest exited with code {r.returncode}\nstderr: {r.stderr}")
        got = r.stdout.strip()
        if got != dotnet_fst_output:
            raise RuntimeError(
                f"Output mismatch vs .NET:\n"
                f"  .NET output:\n{dotnet_fst_output}\n"
                f"  C++ output:\n{got}")

    runner.step("Get .NET reference output", fst_dotnet_run)
    runner.step("Codegen FileStreamTest", fst_codegen)
    runner.step("Generated files exist", fst_files_exist)
    runner.step("CMake configure", fst_cmake_configure)
    runner.step(f"CMake build ({config})", fst_cmake_build)
    runner.step("Run and compare C++ vs .NET output", fst_run_verify)

    # ===== Phase 8: SocketTest (TCP sockets, DNS) =====
    header("Phase 8: SocketTest (TCP sockets, DNS)")

    sk_sample = TESTPROJECTS_DIR / "SocketTest" / "SocketTest.csproj"
    sk_output = temp_dir / "sockettest_output"
    sk_build = sk_output / "build"
    dotnet_sk_output = ""

    def sk_dotnet_run():
        nonlocal dotnet_sk_output
        dotnet_sk_output = _get_dotnet_output(sk_sample)
        print(f"    .NET output: {repr(dotnet_sk_output[:200])}")

    def sk_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(sk_sample), "-o", str(sk_output)],
            capture=True)

    def sk_files_exist():
        for f in ["SocketTest.h", "SocketTest_data.cpp", "SocketTest_stubs.cpp",
                   "main.cpp", "CMakeLists.txt"]:
            if not (sk_output / f).exists():
                raise RuntimeError(f"Missing: {f}")
        if not list(sk_output.glob("SocketTest_methods_*.cpp")):
            raise RuntimeError("No SocketTest_methods_*.cpp files found")

    def sk_cmake_configure():
        run(["cmake", "-B", str(sk_build), "-S", str(sk_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)

    def sk_cmake_build():
        run(["cmake", "--build", str(sk_build), "--config", config],
            capture=True)

    def sk_run_verify():
        exe = _exe_path(sk_build, config, "SocketTest")
        if not exe.exists():
            raise RuntimeError(f"Executable not found: {exe}")
        r = subprocess.run([str(exe)], capture_output=True, text=True, check=False,
                           encoding="utf-8", errors="replace", timeout=30)
        if r.returncode != 0:
            raise RuntimeError(f"SocketTest exited with code {r.returncode}\nstderr: {r.stderr}")
        got = r.stdout.strip()
        # Compare the stable parts of output. Skip:
        # - Line 12 (0-based: 11): port number varies per run
        # - Lines 21+ (0-based: 20+): DNS section differs (AOT DNS not yet supported)
        # - Last line "=== Done ===" must match
        got_lines = got.split('\n')
        exp_lines = dotnet_sk_output.split('\n')
        mismatches = []
        # Compare lines before port line
        for i in range(min(11, len(got_lines), len(exp_lines))):
            if got_lines[i].strip() != exp_lines[i].strip():
                mismatches.append(f"  line {i+1}: got '{got_lines[i].strip()}', expected '{exp_lines[i].strip()}'")
        # Compare lines after port line, before DNS section (lines 13-20, 0-based 12-19)
        for i in range(12, min(20, len(got_lines), len(exp_lines))):
            if got_lines[i].strip() != exp_lines[i].strip():
                mismatches.append(f"  line {i+1}: got '{got_lines[i].strip()}', expected '{exp_lines[i].strip()}'")
        # Verify last line is "=== Done ==="
        if got_lines[-1].strip() != "=== Done ===":
            mismatches.append(f"  last line: got '{got_lines[-1].strip()}', expected '=== Done ==='")
        if mismatches:
            raise RuntimeError("Output mismatch:\n" + "\n".join(mismatches))

    runner.step("Get .NET reference output", sk_dotnet_run)
    runner.step("Codegen SocketTest", sk_codegen)
    runner.step("Generated files exist", sk_files_exist)
    runner.step("CMake configure", sk_cmake_configure)
    runner.step(f"CMake build ({config})", sk_cmake_build)
    runner.step("Run and compare C++ vs .NET output", sk_run_verify)

    # ===== Phase 9: HttpGetTest (HTTP client, async/await) =====
    header("Phase 9: HttpGetTest (HTTP client, async/await)")

    hg_sample = TESTPROJECTS_DIR / "HttpGetTest" / "HttpGetTest.csproj"
    hg_output = temp_dir / "httpgettest_output"
    hg_build = hg_output / "build"
    dotnet_hg_output = ""

    def hg_dotnet_run():
        nonlocal dotnet_hg_output
        dotnet_hg_output = _get_dotnet_output(hg_sample)
        print(f"    .NET output: {repr(dotnet_hg_output[:200])}")

    def hg_codegen():
        run(["dotnet", "run", "--project", str(CLI_PROJECT), "--",
             "codegen", "-i", str(hg_sample), "-o", str(hg_output)],
            capture=True)

    def hg_files_exist():
        for f in ["HttpGetTest.h", "HttpGetTest_data.cpp", "HttpGetTest_stubs.cpp",
                   "main.cpp", "CMakeLists.txt"]:
            if not (hg_output / f).exists():
                raise RuntimeError(f"Missing: {f}")
        if not list(hg_output.glob("HttpGetTest_methods_*.cpp")):
            raise RuntimeError("No HttpGetTest_methods_*.cpp files found")

    def hg_cmake_configure():
        run(["cmake", "-B", str(hg_build), "-S", str(hg_output),
             "-G", generator, *cmake_arch,
             f"-DCMAKE_PREFIX_PATH={runtime_prefix}"],
            capture=True)

    def hg_cmake_build():
        run(["cmake", "--build", str(hg_build), "--config", config],
            capture=True)

    def hg_run_verify():
        exe = _exe_path(hg_build, config, "HttpGetTest")
        if not exe.exists():
            raise RuntimeError(f"Executable not found: {exe}")
        r = subprocess.run([str(exe)], capture_output=True, text=True, check=False,
                           encoding="utf-8", errors="replace", timeout=30)
        if r.returncode != 0:
            raise RuntimeError(f"HttpGetTest exited with code {r.returncode}\nstderr: {r.stderr}")
        got = r.stdout.strip()
        expected = dotnet_hg_output.strip()
        if got != expected:
            got_lines = got.split('\n')
            exp_lines = expected.split('\n')
            mismatches = []
            for i in range(max(len(got_lines), len(exp_lines))):
                g = got_lines[i].strip() if i < len(got_lines) else "<missing>"
                e = exp_lines[i].strip() if i < len(exp_lines) else "<missing>"
                if g != e:
                    mismatches.append(f"  line {i+1}: got '{g}', expected '{e}'")
            raise RuntimeError("Output mismatch:\n" + "\n".join(mismatches))

    runner.step("Get .NET reference output", hg_dotnet_run)
    runner.step("Codegen HttpGetTest", hg_codegen)
    runner.step("Generated files exist", hg_files_exist)
    runner.step("CMake configure", hg_cmake_configure)
    runner.step(f"CMake build ({config})", hg_cmake_build)
    runner.step("Run and compare C++ vs .NET output", hg_run_verify)

    # NOTE: Other test projects (Library, Debug, StringLiterals,
    # HttpTest, NuGetSimpleTest, JsonSGTest) are disabled.
    # Enable them phase-by-phase as issues are fixed.

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
        ("Test compiler",          "dotnet test",             lambda: cmd_test(argparse.Namespace(compiler=True, runtime=False, integration=False, all=False, config="Release", coverage=False))),
        ("Test runtime",           "ctest",                   lambda: cmd_test(argparse.Namespace(compiler=False, runtime=True, integration=False, all=False, config="Release", coverage=False))),
        ("Test all (unit)",        "compiler + runtime",      lambda: cmd_test(argparse.Namespace(compiler=False, runtime=False, integration=False, all=False, config="Release", coverage=False))),
        ("Test + coverage report", "HTML coverage report",    lambda: cmd_test(argparse.Namespace(compiler=True, runtime=False, integration=False, all=False, config="Release", coverage=True))),
        ("Integration tests",     "full pipeline test",      lambda: cmd_integration(argparse.Namespace(prefix=DEFAULT_PREFIX, config="Release", generator=DEFAULT_GENERATOR, keep_temp=False))),
        ("Install runtime",       f"cmake --install → {DEFAULT_PREFIX}", lambda: cmd_install(argparse.Namespace(prefix=DEFAULT_PREFIX, config="both"))),
        ("Codegen HelloWorld",     "quick codegen test",      lambda: cmd_codegen(argparse.Namespace(sample="HelloWorld", input=None, output="output", config="Release"))),
        ("Compile HelloWorld",     "codegen → cmake → build", lambda: cmd_compile(argparse.Namespace(sample="HelloWorld", input=None, output="output", config="Release", prefix=DEFAULT_PREFIX, run_exe=False))),
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
    p_test.add_argument("--config", default="Release", choices=["Debug", "Release"],
                        help="Build config for runtime/integration tests (default: Release)")
    p_test.add_argument("--coverage", action="store_true", help="Generate coverage report")
    p_test.add_argument("--filter", help="dotnet test --filter expression (compiler tests only)")

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

    # compile
    p_compile = subparsers.add_parser("compile", help="One-step compile: .csproj → native executable")
    p_compile.add_argument("sample", nargs="?", help="Sample name or .csproj path")
    p_compile.add_argument("-i", "--input", help="Input .csproj path")
    p_compile.add_argument("-o", "--output", default="output", help="Output directory (default: output)")
    p_compile.add_argument("-c", "--config", default="Release", choices=["Debug", "Release"])
    p_compile.add_argument("--prefix", default=DEFAULT_PREFIX, help=f"Runtime prefix (default: {DEFAULT_PREFIX})")
    p_compile.add_argument("--run", dest="run_exe", action="store_true", help="Run the executable after building")

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
    elif args.command == "compile":
        return cmd_compile(args)
    elif args.command == "integration":
        return cmd_integration(args)
    elif args.command == "setup":
        return cmd_setup(args)

    return 0


if __name__ == "__main__":
    sys.exit(main())
