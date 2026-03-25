"""Integration test runner — generic executor with parallel/sequential modes.

Replaces the 2000-line copy-pasted cmd_integration() with a data-driven pipeline.
Each test runs the same 6-step sequence using its TestDefinition configuration.
"""

import os
import shutil
import subprocess
import sys
import time
import threading
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

from integration_defs import (
    TestDefinition, TestResult, StepResult,
    compare_socket_output,
)

# Import helpers from dev.py — these are stable utility functions
# that we reuse rather than duplicate.
_dev_module = None

def _get_dev():
    """Lazy-import dev module to avoid circular imports."""
    global _dev_module
    if _dev_module is None:
        import importlib.util
        spec = importlib.util.spec_from_file_location(
            "dev", Path(__file__).parent / "dev.py")
        _dev_module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(_dev_module)
    return _dev_module


# ===== Shared constants (duplicated from dev.py to avoid import overhead) =====

import platform
import re

_REPO_ROOT = Path(__file__).resolve().parent.parent
_CLI_PROJECT = _REPO_ROOT / "compiler" / "CIL2CPP.CLI"
_TESTPROJECTS_DIR = _REPO_ROOT / "tests"
_IS_WINDOWS = platform.system() == "Windows"
_EXE_EXT = ".exe" if _IS_WINDOWS else ""

_COMPILER_WARNING_RE = re.compile(
    r'.*\(\d+,\d+\): warning CS\d+:.*\[.*\.csproj\]$')

_CPP_DIAG_RE = re.compile(
    r'^.*:\s*(warning|error)\s+(?:C|LNK)\d+:', re.MULTILINE)

USE_COLOR = sys.stdout.isatty() and os.environ.get("NO_COLOR") is None


def _c(code, text):
    return f"\033[{code}m{text}\033[0m" if USE_COLOR else text


# ===== Utility functions (self-contained versions) =====

def _run_subprocess(cmd, *, cwd=None, check=True, capture=True, timeout=None):
    """Run a subprocess command. Returns CompletedProcess."""
    try:
        return subprocess.run(
            cmd, cwd=cwd or _REPO_ROOT, check=check,
            capture_output=capture, text=True,
            encoding="utf-8", errors="replace", timeout=timeout)
    except subprocess.CalledProcessError as e:
        if capture and e.stderr:
            pass  # caller handles error reporting
        raise


def _exe_path(build_dir, config, name):
    """Get executable path for multi-config (VS) or single-config (Ninja/Make)."""
    multi = build_dir / config / f"{name}{_EXE_EXT}"
    if multi.exists():
        return multi
    single = build_dir / f"{name}{_EXE_EXT}"
    if single.exists():
        return single
    return multi


def _count_cpp_lines(directory):
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


def _get_dotnet_output(csproj_path):
    """Run a C# project with 'dotnet run' and return its stdout (stripped)."""
    r = subprocess.run(
        ["dotnet", "run", "--project", str(csproj_path)],
        capture_output=True, text=True, check=False,
        encoding="utf-8", errors="replace", timeout=60)
    if r.returncode != 0:
        raise RuntimeError(
            f"dotnet run failed (exit {r.returncode})\n"
            f"stdout: {r.stdout}\nstderr: {r.stderr}")
    lines = r.stdout.split('\n')
    filtered = [l for l in lines if not _COMPILER_WARNING_RE.match(l)]
    return '\n'.join(filtered).strip()


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


def _format_diagnostic_summary(warnings, errors):
    """Format a one-line summary like '2 warnings, 1 error'."""
    parts = []
    if warnings:
        parts.append(f"{len(warnings)} warning{'s' if len(warnings) != 1 else ''}")
    if errors:
        parts.append(f"{len(errors)} error{'s' if len(errors) != 1 else ''}")
    return ", ".join(parts) if parts else ""


def _cmake_build_with_diagnostics(build_dir, config):
    """Run cmake --build and return diagnostic summary string."""
    try:
        r = _run_subprocess(
            ["cmake", "--build", str(build_dir), "--config", config])
    except subprocess.CalledProcessError as e:
        warnings, errors = _extract_cpp_diagnostics(e.stdout, e.stderr)
        diag = _format_diagnostic_summary(warnings, errors)
        msg = f"CMake build failed"
        if diag:
            msg += f" ({diag})"
        if errors:
            msg += "\n" + "\n".join(f"    {l}" for l in errors[:10])
        raise RuntimeError(msg)
    warnings, errors = _extract_cpp_diagnostics(r.stdout, r.stderr)
    return _format_diagnostic_summary(warnings, errors)


def _compare_output_with_skip(got, expected, skip_lines=None):
    """Compare output line-by-line, skipping specified 0-based line indices."""
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
            mismatches.append(
                f"  line {i+1}: got '{g.strip()}', expected '{e.strip()}'")
    if mismatches:
        raise RuntimeError("Output mismatch:\n" + "\n".join(mismatches))


def _compare_line_by_line(got, expected):
    """Default comparison — line-by-line diff for better error messages."""
    if got == expected:
        return
    got_lines = got.split('\n')
    exp_lines = expected.split('\n')
    mismatches = []
    for i in range(max(len(got_lines), len(exp_lines))):
        g = got_lines[i].strip() if i < len(got_lines) else "<missing>"
        e = exp_lines[i].strip() if i < len(exp_lines) else "<missing>"
        if g != e:
            mismatches.append(f"  line {i+1}: got '{g}', expected '{e}'")
    if mismatches:
        raise RuntimeError("Output mismatch:\n" + "\n".join(mismatches))


# ===== Generic 6-step test executor =====

# Step name patterns → metric keys (same as TestRunner._STEP_CATEGORIES)
_STEP_METRICS = {
    "dotnet": "dotnet_s",
    "codegen": "codegen_s",
    "files": "files_s",
    "configure": "configure_s",
    "build": "build_s",
    "run": "run_s",
}


def run_single_test(defn, temp_dir, config, generator, cmake_arch,
                    runtime_prefix):
    """Execute a single integration test through the 6-step pipeline.

    Returns a TestResult with per-step timing and pass/fail status.
    All output is buffered — no print() calls during execution.
    """
    name = defn.name
    csproj_path = _TESTPROJECTS_DIR / defn.csproj_dir / f"{defn.csproj_dir}.csproj"

    # Derive output and build directories
    dir_slug = name.lower().replace(" ", "_")
    output_dir = temp_dir / f"{dir_slug}_output"
    if defn.build_dir_nested:
        build_dir = output_dir / "build"
    else:
        build_dir = temp_dir / f"{dir_slug}_build"

    result = TestResult(name=name, phase_num=defn.phase_num)
    dotnet_output = ""

    def _step(step_key, step_name, fn):
        """Execute a step, record timing and result."""
        t0 = time.time()
        try:
            extra = fn()
            elapsed = time.time() - t0
            result.steps.append(StepResult(
                name=step_name, passed=True, elapsed=elapsed,
                extra=str(extra) if extra else ""))
            if step_key in _STEP_METRICS:
                result.metrics[_STEP_METRICS[step_key]] = elapsed
        except Exception as e:
            elapsed = time.time() - t0
            result.steps.append(StepResult(
                name=step_name, passed=False, elapsed=elapsed,
                error_msg=str(e)))
            if step_key in _STEP_METRICS:
                result.metrics[_STEP_METRICS[step_key]] = elapsed

    # Step 1: Get .NET reference output
    def step_dotnet():
        nonlocal dotnet_output
        if defn.pre_run_cleanup:
            cleanup_dir = Path(os.environ.get('TEMP', '/tmp')) / defn.pre_run_cleanup
            if cleanup_dir.exists():
                shutil.rmtree(cleanup_dir, ignore_errors=True)
        dotnet_output = _get_dotnet_output(csproj_path)

    _step("dotnet", f"Get .NET reference output", step_dotnet)

    # Step 2: Codegen
    def step_codegen():
        cmd = ["dotnet", "run", "--no-build", "--project", str(_CLI_PROJECT), "--",
               "codegen", "-i", str(csproj_path), "-o", str(output_dir)]
        if defn.codegen_config != "Release":
            cmd += ["-c", defn.codegen_config]
        _run_subprocess(cmd)

    _step("codegen", f"Codegen {name}", step_codegen)

    # Step 3: Verify generated files exist
    def step_files():
        expected_files = [
            f"{defn.csproj_dir}.h",
            f"{defn.csproj_dir}_data.cpp",
            f"{defn.csproj_dir}_stubs.cpp",
            "main.cpp",
            "CMakeLists.txt",
        ]
        for f in expected_files:
            if not (output_dir / f).exists():
                raise RuntimeError(f"Missing: {f}")
        if defn.check_methods_glob:
            if not list(output_dir.glob(f"{defn.csproj_dir}_methods_*.cpp")):
                raise RuntimeError(
                    f"No {defn.csproj_dir}_methods_*.cpp files found")
        lines = _count_cpp_lines(output_dir)
        result.metrics["lines"] = lines

    _step("files", "Generated files exist", step_files)

    # Step 4: CMake configure
    def step_configure():
        _run_subprocess([
            "cmake", "-B", str(build_dir), "-S", str(output_dir),
            "-G", generator, *cmake_arch,
            f"-DCMAKE_PREFIX_PATH={runtime_prefix}"])

    _step("configure", "CMake configure", step_configure)

    # Step 5: CMake build
    def step_build():
        return _cmake_build_with_diagnostics(build_dir, config)

    _step("build", f"CMake build ({config})", step_build)

    # Step 6: Run and compare output
    def step_run():
        exe = _exe_path(build_dir, config, defn.exe_name)
        if not exe.exists():
            raise RuntimeError(f"Executable not found: {exe}")

        # Pre-run cleanup (DirTest: clean temp dir left by .NET run)
        if defn.pre_run_cleanup:
            cleanup_dir = Path(os.environ.get('TEMP', '/tmp')) / defn.pre_run_cleanup
            if cleanup_dir.exists():
                shutil.rmtree(cleanup_dir, ignore_errors=True)

        run_kwargs = dict(
            capture_output=True, text=True, check=False,
            encoding="utf-8", errors="replace")
        if defn.run_timeout:
            run_kwargs["timeout"] = defn.run_timeout

        r = subprocess.run([str(exe)], **run_kwargs)
        if r.returncode != 0:
            raise RuntimeError(
                f"{name} exited with code {r.returncode}\nstderr: {r.stderr}")

        got = r.stdout.strip()
        expected = dotnet_output.strip()

        # Use appropriate comparator
        if defn.custom_compare == "socket":
            compare_socket_output(got, expected)
        elif defn.skip_lines:
            _compare_output_with_skip(got, expected, skip_lines=defn.skip_lines)
        else:
            _compare_line_by_line(got, expected)

    _step("run", "Run and compare C++ vs .NET output", step_run)

    return result


# ===== Parallel runner =====

def run_tests_parallel(tests, temp_dir, config, generator, cmake_arch,
                       runtime_prefix, jobs):
    """Run tests in parallel using ThreadPoolExecutor.

    Tests are sorted longest-first for optimal bin packing.
    Progress is printed as each test completes.
    Returns results in phase-number order.
    """
    sorted_tests = sorted(tests, key=lambda t: t.expected_seconds, reverse=True)

    completed = 0
    lock = threading.Lock()
    total = len(tests)

    print(f"  Running {total} tests with {jobs} parallel workers...\n")

    results = []
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        futures = {
            pool.submit(run_single_test, t, temp_dir, config, generator,
                        cmake_arch, runtime_prefix): t
            for t in sorted_tests
        }
        for future in as_completed(futures):
            defn = futures[future]
            try:
                result = future.result()
            except Exception as e:
                # Shouldn't happen — run_single_test catches internally
                result = TestResult(name=defn.name, phase_num=defn.phase_num)
                result.steps.append(StepResult(
                    name="fatal", passed=False, elapsed=0,
                    error_msg=str(e)))
            results.append(result)

            with lock:
                completed += 1
                status = _c("32", "PASS") if result.passed else _c("31", "FAIL")
                total_s = result.total_seconds
                print(f"  [{completed:>2}/{total}] {result.name:<25} "
                      f"{status} ({total_s:.1f}s)")

    # Sort by phase number for ordered output
    results.sort(key=lambda r: r.phase_num)
    return results


# ===== Sequential runner =====

def run_tests_sequential(tests, temp_dir, config, generator, cmake_arch,
                         runtime_prefix):
    """Run tests sequentially with live output (same experience as original).

    Returns results in phase-number order.
    """
    results = []
    test_count = 0

    for defn in tests:
        # Print phase header
        print(f"\n{'=' * 40}")
        print(f" {_c('36', f'Phase {defn.phase_num}: {defn.name}')}")
        print(f"{'=' * 40}")

        result = run_single_test(defn, temp_dir, config, generator,
                                 cmake_arch, runtime_prefix)
        results.append(result)

        # Print step results (mimics original TestRunner.step output)
        for step in result.steps:
            test_count += 1
            parts = []
            if step.extra:
                parts.append(step.extra)
            parts.append(f"{step.elapsed:.1f}s")
            detail = f"({', '.join(parts)})"

            if step.passed:
                print(f"  [{test_count}] {step.name} ... {detail} "
                      f"{_c('32', 'PASS')}")
            else:
                print(f"  [{test_count}] {step.name} ... "
                      f"{_c('31', 'FAIL')}")
                if step.error_msg:
                    print(f"       {step.error_msg}")

    return results


# ===== Result printing =====

def print_results(results, wall_clock_seconds=None):
    """Print metrics table and summary (same format as original TestRunner)."""
    # Metrics table
    print()
    print(f"  {'Phase':<25} {'Codegen':>8} {'Build':>8} {'Total':>8} "
          f"{'C++ Lines':>12}")
    print(f"  {'-'*25} {'-'*8} {'-'*8} {'-'*8} {'-'*12}")

    grand_codegen = 0.0
    grand_build = 0.0
    grand_total = 0.0

    for r in results:
        m = r.metrics
        cg = m.get("codegen_s", 0)
        bd = m.get("build_s", 0)
        dn = m.get("dotnet_s", 0)
        cf = m.get("configure_s", 0)
        rn = m.get("run_s", 0)
        fl = m.get("files_s", 0)
        total = dn + cg + fl + cf + bd + rn
        grand_codegen += cg
        grand_build += bd
        grand_total += total
        lines = m.get("lines", 0)
        lines_str = f"{lines:,}" if lines else "-"
        print(f"  {r.name:<25} {cg:>7.1f}s {bd:>7.1f}s "
              f"{total:>7.1f}s {lines_str:>12}")

    print(f"  {'-'*25} {'-'*8} {'-'*8} {'-'*8} {'-'*12}")
    print(f"  {'TOTAL':<25} {grand_codegen:>7.1f}s {grand_build:>7.1f}s "
          f"{grand_total:>7.1f}s")

    if wall_clock_seconds is not None:
        print(f"\n  Wall clock: {wall_clock_seconds:.1f}s "
              f"(speedup: {grand_total / wall_clock_seconds:.1f}x)")

    # Summary
    total_tests = sum(r.test_count for r in results)
    total_pass = sum(r.pass_count for r in results)
    total_fail = sum(r.fail_count for r in results)

    print(f"\n  Total:  {total_tests}")
    print(f"  {_c('32', f'Passed: {total_pass}')}")
    if total_fail:
        print(f"  {_c('31', f'Failed: {total_fail}')}")
        print()
        print(f"  {_c('31', 'Failures:')}")
        for r in results:
            for s in r.steps:
                if not s.passed:
                    print(f"  {_c('31', f'  - [{r.name}] {s.name}: {s.error_msg}')}")
    else:
        print(f"  {_c('32', f'Failed: {total_fail}')}")
    print()
    return total_fail
