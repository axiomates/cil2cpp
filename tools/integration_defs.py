"""Integration test definitions — data-driven test registry for CIL2CPP integration tests.

Each TestDefinition describes one test project and its specific configuration.
The generic executor in integration_runner.py uses these to run the 6-step pipeline.
"""

from dataclasses import dataclass, field


@dataclass
class TestDefinition:
    """Declarative definition of a single integration test."""
    name: str               # "HelloWorld" — also used as phase name in metrics
    phase_num: int          # 1-34, for ordering in reports
    csproj_dir: str         # directory name under tests/ (e.g. "HelloWorld")
    exe_name: str = ""      # executable name; defaults to csproj_dir if empty
    codegen_config: str = "Release"  # "Release" or "Debug"
    check_methods_glob: bool = False  # check {Name}_methods_*.cpp exists
    skip_lines: frozenset = frozenset()  # 0-based line indices to skip in comparison
    custom_compare: str = ""  # "socket" for SocketTest's custom comparison
    pre_run_cleanup: str = ""  # temp subdir name to clean before dotnet/cpp run
    run_timeout: int = 0      # subprocess timeout; 0 = default (no explicit timeout)
    build_dir_nested: bool = False  # True = build dir inside output dir
    expected_seconds: int = 25  # for longest-first scheduling

    def __post_init__(self):
        if not self.exe_name:
            self.exe_name = self.csproj_dir


@dataclass
class StepResult:
    """Result of a single step within a test."""
    name: str
    passed: bool
    elapsed: float
    error_msg: str = ""
    extra: str = ""  # e.g. diagnostic summary from cmake build


@dataclass
class TestResult:
    """Result of running a complete test (6 steps)."""
    name: str
    phase_num: int
    steps: list = field(default_factory=list)  # List[StepResult]
    metrics: dict = field(default_factory=dict)  # {codegen_s, build_s, lines, ...}

    @property
    def passed(self):
        return all(s.passed for s in self.steps)

    @property
    def total_seconds(self):
        return sum(s.elapsed for s in self.steps)

    @property
    def test_count(self):
        return len(self.steps)

    @property
    def pass_count(self):
        return sum(1 for s in self.steps if s.passed)

    @property
    def fail_count(self):
        return sum(1 for s in self.steps if not s.passed)


def compare_socket_output(got, expected):
    """SocketTest custom comparison — skip port line and DNS section.

    Compare the stable parts of output:
    - Lines 1-11 (before port line): exact match
    - Line 12 (0-based 11): port number varies — skip
    - Lines 13-20 (0-based 12-19): exact match
    - Lines 21+: DNS section differs — skip
    - Last line "=== Done ===" must match
    """
    got_lines = got.split('\n')
    exp_lines = expected.split('\n')
    mismatches = []
    # Compare lines before port line
    for i in range(min(11, len(got_lines), len(exp_lines))):
        if got_lines[i].strip() != exp_lines[i].strip():
            mismatches.append(
                f"  line {i+1}: got '{got_lines[i].strip()}', "
                f"expected '{exp_lines[i].strip()}'")
    # Compare lines after port line, before DNS section (lines 13-20, 0-based 12-19)
    for i in range(12, min(20, len(got_lines), len(exp_lines))):
        if got_lines[i].strip() != exp_lines[i].strip():
            mismatches.append(
                f"  line {i+1}: got '{got_lines[i].strip()}', "
                f"expected '{exp_lines[i].strip()}'")
    # Verify last line is "=== Done ==="
    if got_lines[-1].strip() != "=== Done ===":
        mismatches.append(
            f"  last line: got '{got_lines[-1].strip()}', expected '=== Done ==='")
    if mismatches:
        raise RuntimeError("Output mismatch:\n" + "\n".join(mismatches))


# All 34 integration tests in phase order.
# Special configurations are documented inline.
TESTS = [
    TestDefinition("HelloWorld", 1, "HelloWorld",
                   check_methods_glob=True),
    TestDefinition("ArrayTest", 2, "ArrayTest",
                   check_methods_glob=True),
    TestDefinition("FeatureTest", 3, "FeatureTest",
                   check_methods_glob=True, skip_lines=frozenset({40, 99}),
                   run_timeout=60, build_dir_nested=True),
    TestDefinition("ArglistTest", 4, "ArglistTest"),
    TestDefinition("MultiAssemblyTest", 5, "MultiAssemblyTest"),
    TestDefinition("SystemIOTest", 6, "SystemIOTest"),
    TestDefinition("FileStreamTest", 7, "FileStreamTest"),
    TestDefinition("SocketTest", 8, "SocketTest",
                   check_methods_glob=True, custom_compare="socket",
                   run_timeout=30, build_dir_nested=True, expected_seconds=20),
    TestDefinition("HttpGetTest", 9, "HttpGetTest",
                   check_methods_glob=True, run_timeout=30,
                   build_dir_nested=True, expected_seconds=20),
    TestDefinition("HttpTest", 10, "HttpTest",
                   check_methods_glob=True, run_timeout=30,
                   build_dir_nested=True, expected_seconds=20),
    TestDefinition("HttpsGetTest", 11, "HttpsGetTest",
                   check_methods_glob=True, run_timeout=30,
                   build_dir_nested=True, expected_seconds=20),
    TestDefinition("DirTest", 12, "DirTest",
                   pre_run_cleanup="cil2cpp_dirtest"),
    TestDefinition("JsonSGTest", 13, "JsonSGTest", expected_seconds=25),
    TestDefinition("NuGetSimpleTest", 14, "NuGetSimpleTest",
                   expected_seconds=275),
    TestDefinition("DITest", 15, "DITest",
                   codegen_config="Debug", expected_seconds=26),
    TestDefinition("HumanizerTest", 16, "HumanizerTest",
                   codegen_config="Debug", expected_seconds=38),
    TestDefinition("PollyTest", 17, "PollyTest",
                   codegen_config="Debug", expected_seconds=21),
    TestDefinition("PInvokeTest", 18, "PInvokeTest"),
    TestDefinition("SerilogTest", 19, "SerilogTest",
                   codegen_config="Debug", expected_seconds=29),
    TestDefinition("ConfigTest", 20, "ConfigTest", expected_seconds=31),
    TestDefinition("CompressionTest", 21, "CompressionTest"),
    TestDefinition("ValidationApp", 22, "ValidationApp", expected_seconds=22),
    TestDefinition("RegexTest", 23, "RegexTest", expected_seconds=29),
    TestDefinition("DateTimeTest", 24, "DateTimeTest"),
    TestDefinition("DecimalTest", 25, "DecimalTest"),
    TestDefinition("HashidsTest", 26, "HashidsTest", expected_seconds=30),
    TestDefinition("GuardClausesTest", 27, "GuardClausesTest"),
    TestDefinition("SlugifyTest", 28, "SlugifyTest", expected_seconds=29),
    TestDefinition("StatelessTest", 29, "StatelessTest", expected_seconds=23),
    TestDefinition("MiniCsvTool", 30, "MiniCsvTool", expected_seconds=23),
    TestDefinition("TodoManager", 31, "TodoManager", expected_seconds=24),
    TestDefinition("HealthChecker", 32, "HealthChecker", expected_seconds=38),
    TestDefinition("FluentValidationTest", 33, "FluentValidationTest",
                   expected_seconds=88),
    TestDefinition("MiniServiceApp", 34, "MiniServiceApp",
                   expected_seconds=59),
]
