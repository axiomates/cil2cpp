using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// ============================================================
// MiniCsvTool — Real Project Validation (Phase R1-A.1)
//
// A self-contained CSV reader/processor that exercises:
//   - File I/O (WriteAllText, ReadAllLines, StreamWriter)
//   - String parsing (Split, Trim, Join)
//   - Collections (List<T>, Dictionary<K,V>)
//   - LINQ (Where, Select, OrderBy, GroupBy, Aggregate, Sum, Average)
//   - Generics (Repository<T>, typed parsing)
//   - Custom exceptions with inheritance
//   - IDisposable pattern
//   - StringBuilder for output formatting
//   - Nullable<T> for optional fields
//   - Error handling with try/catch composition
//
// ~400 lines, zero NuGet dependencies.
// All data is generated in-memory and written to temp files.
// ============================================================

// --- Domain model ---

class CsvParseException : Exception
{
    public int LineNumber { get; }
    public string RawLine { get; }

    public CsvParseException(string message, int lineNumber, string rawLine)
        : base(message)
    {
        LineNumber = lineNumber;
        RawLine = rawLine;
    }

    public override string ToString() =>
        $"CsvParseException(Line {LineNumber}): {Message} — \"{RawLine}\"";
}

class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public decimal Salary { get; set; }
    public DateTime HireDate { get; set; }
    public string? Title { get; set; }

    public int YearsEmployed =>
        (int)((new DateTime(2025, 1, 1) - HireDate).TotalDays / 365.25);

    public override string ToString() =>
        $"{Name} ({Department}, ${Salary:F0}, {YearsEmployed}yr)";
}

class DepartmentSummary
{
    public string Department { get; set; } = "";
    public int EmployeeCount { get; set; }
    public decimal TotalSalary { get; set; }
    public decimal AverageSalary { get; set; }
    public decimal MinSalary { get; set; }
    public decimal MaxSalary { get; set; }
    public string TopEarner { get; set; } = "";
}

// --- Generic repository ---

interface IRepository<T>
{
    void Add(T item);
    IEnumerable<T> GetAll();
    int Count { get; }
}

class ListRepository<T> : IRepository<T>
{
    private readonly List<T> _items = new();

    public void Add(T item) => _items.Add(item);
    public IEnumerable<T> GetAll() => _items;
    public int Count => _items.Count;
}

// --- CSV Parser ---

class CsvParser : IDisposable
{
    private readonly string _filePath;
    private string[]? _headers;
    private bool _disposed;

    public CsvParser(string filePath)
    {
        _filePath = filePath;
    }

    public string[] Headers
    {
        get
        {
            if (_headers == null)
                throw new InvalidOperationException("Headers not loaded. Call Parse() first.");
            return _headers;
        }
    }

    public List<Employee> Parse()
    {
        var employees = new List<Employee>();
        string[] lines = File.ReadAllLines(_filePath);

        if (lines.Length == 0)
            throw new CsvParseException("Empty CSV file", 0, "");

        _headers = lines[0].Split(',');

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            try
            {
                var emp = ParseLine(line, i + 1);
                employees.Add(emp);
            }
            catch (FormatException ex)
            {
                throw new CsvParseException(
                    $"Format error in field: {ex.Message}", i + 1, line);
            }
        }

        return employees;
    }

    private Employee ParseLine(string line, int lineNumber)
    {
        string[] fields = line.Split(',');
        if (fields.Length < 5)
            throw new CsvParseException(
                $"Expected at least 5 fields, got {fields.Length}",
                lineNumber, line);

        return new Employee
        {
            Id = int.Parse(fields[0].Trim()),
            Name = fields[1].Trim(),
            Department = fields[2].Trim(),
            Salary = decimal.Parse(fields[3].Trim()),
            HireDate = DateTime.Parse(fields[4].Trim()),
            Title = fields.Length > 5 && !string.IsNullOrWhiteSpace(fields[5])
                ? fields[5].Trim()
                : null
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

// --- Report generator ---

class ReportGenerator
{
    private readonly List<Employee> _employees;

    public ReportGenerator(List<Employee> employees)
    {
        _employees = employees;
    }

    public List<DepartmentSummary> GenerateDepartmentSummaries()
    {
        return _employees
            .GroupBy(e => e.Department)
            .Select(g => new DepartmentSummary
            {
                Department = g.Key,
                EmployeeCount = g.Count(),
                TotalSalary = g.Sum(e => e.Salary),
                AverageSalary = Math.Round((decimal)g.Average(e => (double)e.Salary), 0),
                MinSalary = g.Min(e => e.Salary),
                MaxSalary = g.Max(e => e.Salary),
                TopEarner = g.OrderByDescending(e => e.Salary).First().Name
            })
            .OrderBy(s => s.Department)
            .ToList();
    }

    public List<Employee> FilterBySalaryRange(decimal min, decimal max)
    {
        return _employees
            .Where(e => e.Salary >= min && e.Salary <= max)
            .OrderBy(e => e.Salary)
            .ToList();
    }

    public List<Employee> SearchByName(string query)
    {
        string lowerQuery = query.ToLower();
        return _employees
            .Where(e => e.Name.ToLower().Contains(lowerQuery))
            .OrderBy(e => e.Name)
            .ToList();
    }

    public Dictionary<string, int> CountByHireYear()
    {
        return _employees
            .GroupBy(e => e.HireDate.Year)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());
    }

    public List<Employee> GetTopEarners(int count)
    {
        return _employees
            .OrderByDescending(e => e.Salary)
            .Take(count)
            .ToList();
    }

    public List<Employee> GetSeniorEmployees(int minYears)
    {
        return _employees
            .Where(e => e.YearsEmployed >= minYears)
            .OrderByDescending(e => e.YearsEmployed)
            .ToList();
    }

    public string FormatTable(List<Employee> emps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("  ID | Name                 | Department  | Salary   | Hired      | Title");
        sb.AppendLine("  ---+----------------------+-------------+----------+------------+------");
        foreach (var e in emps)
        {
            string title = e.Title ?? "(none)";
            sb.AppendLine($"  {e.Id,3} | {e.Name,-20} | {e.Department,-11} | {e.Salary,8:F0} | {e.HireDate:yyyy-MM-dd} | {title}");
        }
        return sb.ToString();
    }

    public decimal CalculateTotalPayroll()
    {
        return _employees.Aggregate(0m, (sum, e) => sum + e.Salary);
    }
}

// --- CSV Writer ---

static class CsvWriter
{
    public static void WriteSummaryReport(string filePath, List<DepartmentSummary> summaries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Department,Employees,TotalSalary,AvgSalary,MinSalary,MaxSalary,TopEarner");
        foreach (var s in summaries)
        {
            sb.AppendLine($"{s.Department},{s.EmployeeCount},{s.TotalSalary:F0},{s.AverageSalary:F2},{s.MinSalary:F0},{s.MaxSalary:F0},{s.TopEarner}");
        }
        File.WriteAllText(filePath, sb.ToString());
    }
}

// --- Main program ---

class Program
{
    static readonly string TestCsvContent = @"Id,Name,Department,Salary,HireDate,Title
1,Alice Johnson,Engineering,125000,2018-03-15,Senior Engineer
2,Bob Smith,Marketing,85000,2020-07-22,
3,Carol Williams,Engineering,140000,2015-11-01,Staff Engineer
4,David Brown,Sales,92000,2019-06-10,Account Manager
5,Eva Martinez,Engineering,110000,2021-01-18,Engineer
6,Frank Lee,Marketing,78000,2022-04-05,
7,Grace Chen,Sales,105000,2017-08-30,Senior Account Manager
8,Henry Davis,Engineering,155000,2013-02-14,Principal Engineer
9,Ivy Taylor,Marketing,95000,2016-12-03,Marketing Manager
10,Jack Wilson,Sales,88000,2023-09-15,
11,Kate Anderson,Engineering,98000,2022-11-20,Engineer
12,Leo Thomas,Sales,115000,2014-05-28,Regional Director
13,Mia Jackson,Marketing,72000,2024-01-08,
14,Nathan White,Engineering,130000,2016-07-19,Senior Engineer
15,Olivia Harris,Sales,97000,2019-03-25,Account Manager
16,Paul Martin,Engineering,145000,2012-10-11,Staff Engineer
17,Quinn Garcia,Marketing,110000,2015-06-22,VP Marketing
18,Rachel Robinson,Sales,82000,2021-08-14,
19,Sam Clark,Engineering,105000,2023-04-01,Engineer
20,Tina Lewis,Marketing,68000,2024-06-15,";

    static void Main()
    {
        string tempDir = Path.GetTempPath();
        string workDir = Path.Combine(tempDir, "minicsvtool_test");
        string csvFile = Path.Combine(workDir, "employees.csv");
        string reportFile = Path.Combine(workDir, "summary.csv");

        // Setup
        if (!Directory.Exists(workDir))
            Directory.CreateDirectory(workDir);

        int testNum = 0;

        // ── Test 1: Write and parse CSV ──
        testNum++;
        try
        {
            File.WriteAllText(csvFile, TestCsvContent);
            using var parser = new CsvParser(csvFile);
            var employees = parser.Parse();
            Console.WriteLine($"[{testNum}] Parsed {employees.Count} employees: PASS");

            // ── Test 2: Verify headers ──
            testNum++;
            string headerStr = string.Join(", ", parser.Headers);
            Console.WriteLine($"[{testNum}] Headers: {headerStr}");

            // Load into repository
            var repo = new ListRepository<Employee>();
            foreach (var emp in employees)
                repo.Add(emp);
            Console.WriteLine($"    Repository count: {repo.Count}");

            // ── Test 3: Department summaries ──
            testNum++;
            var report = new ReportGenerator(employees);
            var summaries = report.GenerateDepartmentSummaries();
            Console.WriteLine($"[{testNum}] Department summaries ({summaries.Count} departments):");
            foreach (var s in summaries)
            {
                Console.WriteLine($"    {s.Department}: {s.EmployeeCount} employees, avg ${s.AverageSalary:F0}, top={s.TopEarner}");
            }

            // ── Test 4: Filter by salary range ──
            testNum++;
            var midRange = report.FilterBySalaryRange(90000, 120000);
            Console.WriteLine($"[{testNum}] Salary $90K-$120K ({midRange.Count} employees):");
            foreach (var e in midRange)
                Console.WriteLine($"    {e}");

            // ── Test 5: Search by name ──
            testNum++;
            var searchResults = report.SearchByName("son");
            Console.WriteLine($"[{testNum}] Name search 'son' ({searchResults.Count} matches):");
            foreach (var e in searchResults)
                Console.WriteLine($"    {e.Name} ({e.Department})");

            // ── Test 6: Hire year histogram ──
            testNum++;
            var hireYears = report.CountByHireYear();
            Console.WriteLine($"[{testNum}] Hires by year:");
            foreach (var kv in hireYears)
                Console.WriteLine($"    {kv.Key}: {kv.Value} hires");

            // ── Test 7: Top earners ──
            testNum++;
            var topEarners = report.GetTopEarners(5);
            Console.WriteLine($"[{testNum}] Top 5 earners:");
            foreach (var e in topEarners)
                Console.WriteLine($"    {e.Name}: ${e.Salary:F0}");

            // ── Test 8: Senior employees (5+ years) ──
            testNum++;
            var seniors = report.GetSeniorEmployees(5);
            Console.WriteLine($"[{testNum}] Senior employees (5+ years): {seniors.Count}");
            foreach (var e in seniors)
                Console.WriteLine($"    {e.Name}: {e.YearsEmployed} years");

            // ── Test 9: Table formatting ──
            testNum++;
            var engTeam = employees
                .Where(e => e.Department == "Engineering")
                .OrderBy(e => e.Name)
                .ToList();
            Console.WriteLine($"[{testNum}] Engineering team table:");
            Console.Write(report.FormatTable(engTeam));

            // ── Test 10: Total payroll via Aggregate ──
            testNum++;
            decimal totalPayroll = report.CalculateTotalPayroll();
            Console.WriteLine($"[{testNum}] Total payroll: ${totalPayroll:F0}");

            // ── Test 11: Write summary CSV and read it back ──
            testNum++;
            CsvWriter.WriteSummaryReport(reportFile, summaries);
            string summaryContent = File.ReadAllText(reportFile);
            string[] summaryLines = summaryContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Console.WriteLine($"[{testNum}] Summary CSV written and verified ({summaryLines.Length} lines)");
            foreach (var line in summaryLines)
                Console.WriteLine($"    {line.Trim()}");

            // ── Test 12: Chained LINQ pipeline ──
            testNum++;
            var result = employees
                .Where(e => e.Salary > 80000)
                .GroupBy(e => e.Department)
                .Select(g => new { Dept = g.Key, Avg = Math.Round(g.Average(e => (double)e.Salary), 0) })
                .OrderByDescending(x => x.Avg)
                .ToList();
            Console.WriteLine($"[{testNum}] Avg salary by dept (>$80K employees only):");
            foreach (var r in result)
                Console.WriteLine($"    {r.Dept}: ${r.Avg:F0}");

            // ── Test 13: Nullable title analysis ──
            testNum++;
            int withTitle = employees.Count(e => e.Title != null);
            int withoutTitle = employees.Count(e => e.Title == null);
            var titlesByDept = employees
                .Where(e => e.Title != null)
                .GroupBy(e => e.Department)
                .Select(g => new { Dept = g.Key, Titles = g.Select(e => e.Title!).ToList() })
                .OrderBy(x => x.Dept)
                .ToList();
            Console.WriteLine($"[{testNum}] Title analysis: {withTitle} with, {withoutTitle} without");
            foreach (var td in titlesByDept)
                Console.WriteLine($"    {td.Dept}: {string.Join(", ", td.Titles)}");

            // ── Test 14: Error handling — bad CSV ──
            testNum++;
            string badCsv = Path.Combine(workDir, "bad.csv");
            File.WriteAllText(badCsv, "Id,Name,Department,Salary,HireDate\n1,Bad\n");
            try
            {
                using var badParser = new CsvParser(badCsv);
                badParser.Parse();
                Console.WriteLine($"[{testNum}] Bad CSV error handling: FAIL (no exception)");
            }
            catch (CsvParseException ex)
            {
                Console.WriteLine($"[{testNum}] Bad CSV error handling: PASS — {ex}");
            }
            File.Delete(badCsv);

            // ── Test 15: SelectMany + Distinct ──
            testNum++;
            var allDepartments = employees
                .Select(e => e.Department)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
            var nameWords = employees
                .SelectMany(e => e.Name.Split(' '))
                .Distinct()
                .OrderBy(w => w)
                .Take(10)
                .ToList();
            Console.WriteLine($"[{testNum}] Departments: {string.Join(", ", allDepartments)}");
            Console.WriteLine($"    First 10 name words: {string.Join(", ", nameWords)}");

            // ── Test 16: Dictionary operations ──
            testNum++;
            var salaryLookup = new Dictionary<string, decimal>();
            foreach (var e in employees)
            {
                if (!salaryLookup.ContainsKey(e.Department) || e.Salary > salaryLookup[e.Department])
                    salaryLookup[e.Department] = e.Salary;
            }
            Console.WriteLine($"[{testNum}] Max salary per department:");
            foreach (var kv in salaryLookup.OrderBy(kv => kv.Key))
                Console.WriteLine($"    {kv.Key}: ${kv.Value:F0}");

            // ── Test 17: Skip/Take pagination ──
            testNum++;
            int pageSize = 5;
            var page1 = employees.OrderBy(e => e.Id).Skip(0).Take(pageSize).ToList();
            var page2 = employees.OrderBy(e => e.Id).Skip(pageSize).Take(pageSize).ToList();
            var page3 = employees.OrderBy(e => e.Id).Skip(pageSize * 2).Take(pageSize).ToList();
            Console.WriteLine($"[{testNum}] Pagination (page size {pageSize}):");
            Console.WriteLine($"    Page 1: {string.Join(", ", page1.Select(e => e.Name))}");
            Console.WriteLine($"    Page 2: {string.Join(", ", page2.Select(e => e.Name))}");
            Console.WriteLine($"    Page 3: {string.Join(", ", page3.Select(e => e.Name))}");

            // ── Test 18: Zip two collections ──
            testNum++;
            var names = employees.OrderBy(e => e.Salary).Select(e => e.Name).Take(5).ToList();
            var salaries = employees.OrderBy(e => e.Salary).Select(e => e.Salary).Take(5).ToList();
            var zipped = names.Zip(salaries, (n, s) => $"{n}=${s:F0}").ToList();
            Console.WriteLine($"[{testNum}] Bottom 5 (zipped): {string.Join(", ", zipped)}");

            // ── Test 19: Any/All/Contains ──
            testNum++;
            bool anyOver150k = employees.Any(e => e.Salary > 150000);
            bool allPositive = employees.All(e => e.Salary > 0);
            bool containsEngineering = allDepartments.Contains("Engineering");
            Console.WriteLine($"[{testNum}] Any >$150K: {anyOver150k}, All positive: {allPositive}, Has Engineering: {containsEngineering}");

            // ── Test 20: First/Last/Single variants ──
            testNum++;
            var first = employees.OrderBy(e => e.Id).First();
            var last = employees.OrderBy(e => e.Id).Last();
            var firstOrDefault = employees.FirstOrDefault(e => e.Name == "Nonexistent");
            Console.WriteLine($"[{testNum}] First: {first.Name}, Last: {last.Name}, Missing: {(firstOrDefault == null ? "null" : firstOrDefault.Name)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{testNum}] UNEXPECTED ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        // Cleanup
        try
        {
            if (File.Exists(csvFile)) File.Delete(csvFile);
            if (File.Exists(reportFile)) File.Delete(reportFile);
        }
        catch { }

        Console.WriteLine("=== MiniCsvTool complete ===");
    }
}
