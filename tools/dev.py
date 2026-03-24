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
import time
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
DEFAULT_PREFIX = "C:/cil2cpp" if IS_WINDOWS else "/usr/local/cil2cpp"
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
            jobs=0,
            sequential=False,
            filter=None,
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

def count_cpp_lines(directory):
    """Count total lines in *.h and *.cpp files in directory."""
    total = 0
    d = Path(directory)
    if not d.exists():
        return 0
    for pattern in ("*.h", "*.cpp"):
        for f in d.glob(pattern):
            try:
                total += sum(1 for _ in open(f, errors="ignore"))
            except OSError:
                pass
    return total



def _exe_path(build_dir, config, name):
    """Get executable path for multi-config (VS) or single-config (Ninja/Make)."""
    multi = build_dir / config / f"{name}{EXE_EXT}"
    if multi.exists():
        return multi
    single = build_dir / f"{name}{EXE_EXT}"
    if single.exists():
        return single
    return multi  # default to multi-config path for error messages


_COMPILER_WARNING_RE = re.compile(
    r'.*\(\d+,\d+\): warning CS\d+:.*\[.*\.csproj\]$')

# C++ compiler/linker warning/error patterns in cmake --build output.
# Matches MSVC compiler (C4267, C2065, etc.) and linker (LNK4098, LNK2019, etc.).
# Excludes MSBuild infrastructure warnings (MSB8029 etc.) which are not code issues.
_CPP_DIAG_RE = re.compile(
    r'^.*:\s*(warning|error)\s+(?:C|LNK)\d+:', re.MULTILINE)


def _extract_cpp_diagnostics(stdout, stderr):
    """Extract C++ compiler warnings and errors from cmake build output."""
    combined = (stdout or "") + "\n" + (stderr or "")
    warnings = []
    errors = []
    for m in _CPP_DIAG_RE.finditer(combined):
        line = m.group(0)
        if m.group(1) == "warning":
            warnings.append(line)
        else:
            errors.append(line)
    return warnings, errors


def _print_cpp_diagnostics(warnings, errors):
    """Print C++ compiler diagnostics to the test report."""
    if warnings or errors:
        print()
        if errors:
            for line in errors:
                error(f"    {line}")
        if warnings:
            for line in warnings:
                warn(f"    {line}")


def _format_diagnostic_summary(warnings, errors):
    """Format a one-line summary like '2 warnings, 1 error'."""
    parts = []
    if warnings:
        parts.append(f"{len(warnings)} warning{'s' if len(warnings) != 1 else ''}")
    if errors:
        parts.append(f"{len(errors)} error{'s' if len(errors) != 1 else ''}")
    return ", ".join(parts) if parts else None


def _cmake_build_with_diagnostics(build_dir, config):
    """Run cmake --build and report C++ compiler warnings/errors.

    Returns a summary string for the test report (e.g., "2 warnings, 0 errors").
    Raises on build failure (non-zero exit code) with diagnostics printed first.
    """
    try:
        r = run(["cmake", "--build", str(build_dir), "--config", config],
                capture=True)
    except subprocess.CalledProcessError as e:
        # Build failed — extract and print diagnostics before re-raising
        warnings, errors = _extract_cpp_diagnostics(e.stdout, e.stderr)
        _print_cpp_diagnostics(warnings, errors)
        raise
    # Build succeeded — check for warnings
    warnings, errors = _extract_cpp_diagnostics(r.stdout, r.stderr)
    _print_cpp_diagnostics(warnings, errors)
    return _format_diagnostic_summary(warnings, errors)


def _get_dotnet_output(csproj_path):
    """Run a C# project with 'dotnet run' and return its stdout (stripped).

    Filters out compiler warning lines that 'dotnet run' emits to stdout
    during the implicit build step (e.g. 'warning CS8632: ...[path.csproj]').
    """
    r = subprocess.run(
        ["dotnet", "run", "--project", str(csproj_path)],
        capture_output=True, text=True, check=False,
        encoding="utf-8", errors="replace",
        timeout=60,
    )
    if r.returncode != 0:
        raise RuntimeError(
            f"dotnet run failed (exit {r.returncode})\nstdout: {r.stdout}\nstderr: {r.stderr}")
    lines = r.stdout.split('\n')
    filtered = [l for l in lines if not _COMPILER_WARNING_RE.match(l)]
    return '\n'.join(filtered).strip()


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
    """Run integration tests — parallel by default, sequential with --sequential.

    Uses data-driven test definitions from integration_defs.py and
    generic executor from integration_runner.py.
    """
    # Add tools/ to sys.path for integration_defs/integration_runner imports
    tools_dir = str(Path(__file__).resolve().parent)
    if tools_dir not in sys.path:
        sys.path.insert(0, tools_dir)
    from integration_defs import TESTS
    from integration_runner import (
        run_tests_parallel, run_tests_sequential, print_results,
    )

    runtime_prefix = args.prefix
    config = args.config
    generator = args.generator
    keep_temp = args.keep_temp
    jobs = getattr(args, "jobs", 0)
    sequential = getattr(args, "sequential", False)
    test_filter = getattr(args, "filter", None)

    if sequential:
        jobs = 1
    elif jobs <= 0:
        cpu = os.cpu_count() or 4
        jobs = max(2, min(cpu // 4, 4))

    cmake_arch = ["-A", "x64"] if "Visual Studio" in generator else []

    temp_dir = Path(tempfile.mkdtemp(prefix="cil2cpp_integration_"))

    header("CIL2CPP Integration Test")
    print(f"  Repo:    {REPO_ROOT}")
    print(f"  Runtime: {runtime_prefix}")
    print(f"  Config:  {config}")
    print(f"  Jobs:    {jobs}")
    print(f"  Temp:    {temp_dir}")

    # ===== Phase 0: Prerequisites =====
    header("Phase 0: Prerequisites")

    prereq_ok = True

    def _check_prereq(name, fn):
        nonlocal prereq_ok
        print(f"  {name} ... ", end="", flush=True)
        try:
            result = fn()
            success(f"OK" + (f" ({result})" if result else ""))
        except Exception as e:
            error(f"FAIL: {e}")
            prereq_ok = False

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

    _check_prereq("dotnet SDK available", check_dotnet)
    _check_prereq("CMake available", check_cmake)
    _check_prereq(f"Runtime installed at {runtime_prefix}", check_runtime)

    if not prereq_ok:
        error("\n  Prerequisites failed — aborting.")
        return 1

    # ===== Filter tests =====
    tests = TESTS
    if test_filter:
        tests = [t for t in TESTS if test_filter.lower() in t.name.lower()]
        if not tests:
            error(f"  No tests match filter: {test_filter}")
            return 1
        print(f"\n  Filter: {test_filter} ({len(tests)} test{'s' if len(tests) != 1 else ''})")

    # ===== Run tests =====
    wall_t0 = time.time()
    if jobs == 1:
        results = run_tests_sequential(
            tests, temp_dir, config, generator, cmake_arch, runtime_prefix)
    else:
        results = run_tests_parallel(
            tests, temp_dir, config, generator, cmake_arch,
            runtime_prefix, jobs)
    wall_elapsed = time.time() - wall_t0

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
    return print_results(results, wall_clock_seconds=wall_elapsed)

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
        ("Integration tests",     "full pipeline test",      lambda: cmd_integration(argparse.Namespace(prefix=DEFAULT_PREFIX, config="Release", generator=DEFAULT_GENERATOR, keep_temp=False, jobs=0, sequential=False, filter=None))),
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
    p_integ.add_argument("-j", "--jobs", type=int, default=0,
                         help="Parallel workers (0=auto, 1=sequential)")
    p_integ.add_argument("--sequential", action="store_true", help="Run tests sequentially (same as --jobs 1)")
    p_integ.add_argument("--filter", help="Run only tests matching pattern (e.g. 'Hello' or 'NuGet')")

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
