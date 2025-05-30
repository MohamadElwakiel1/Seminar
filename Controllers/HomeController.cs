using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SeminarMvcApp.Models;
using System.IO;
using OfficeOpenXml;
using Microsoft.Extensions.Configuration;

namespace SeminarMvcApp.Controllers;

/// <summary>
/// HomeController handles the main application logic for Excel upload, analytics, and budget optimization.
/// </summary>
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    /// <summary>
    /// Constructor for HomeController. Injects a logger for diagnostics.
    /// </summary>
    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// GET: Home/Index
    /// Renders the main page with upload and analytics tabs.
    /// </summary>
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// POST: Home/Index
    /// Handles Excel file upload, parses the file, and displays its contents in a table.
    /// Also computes summary statistics (analytics) for each numeric column.
    /// </summary>
    /// <param name="excelFile">The uploaded Excel file (.xlsx)</param>
    /// <returns>The Index view with Excel data and analytics</returns>
    [HttpPost]
    public IActionResult Index(IFormFile excelFile)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            ViewBag.Error = "Please select a valid Excel file.";
            return View();
        }

        var model = new ExcelViewModel();
        using (var stream = new MemoryStream())
        {
            excelFile.CopyTo(stream);
            stream.Position = 0;
            // Parse the Excel file using EPPlus
            using (var package = new ExcelPackage(stream))
            {
                var worksheet = package.Workbook.Worksheets[0];
                int colCount = worksheet.Dimension.Columns;
                int rowCount = worksheet.Dimension.Rows;

                // Read headers from the first row
                for (int col = 1; col <= colCount; col++)
                {
                    model.Headers.Add(worksheet.Cells[1, col].Text);
                }
                // Read all data rows
                for (int row = 2; row <= rowCount; row++)
                {
                    var rowData = new List<string>();
                    for (int col = 1; col <= colCount; col++)
                    {
                        rowData.Add(worksheet.Cells[row, col].Text);
                    }
                    model.Rows.Add(rowData);
                }

                // Calculate analytics (summary statistics) for each column
                for (int col = 1; col <= colCount; col++)
                {
                    var values = new List<double>();
                    for (int row = 2; row <= rowCount; row++)
                    {
                        var cellText = worksheet.Cells[row, col].Text;
                        if (double.TryParse(cellText, out double val))
                        {
                            values.Add(val);
                        }
                    }
                    var analytics = new ColumnAnalytics
                    {
                        Header = worksheet.Cells[1, col].Text,
                        Count = values.Count,
                        Sum = values.Count > 0 ? values.Sum() : (double?)null,
                        Average = values.Count > 0 ? values.Average() : (double?)null,
                        Min = values.Count > 0 ? values.Min() : (double?)null,
                        Max = values.Count > 0 ? values.Max() : (double?)null
                    };
                    model.Analytics.Add(analytics);
                }
            }
        }
        return View(model);
    }

    /// <summary>
    /// POST: Home/Analytics
    /// Receives serialized Excel data, recalculates analytics, and displays the analytics table/chart.
    /// </summary>
    /// <param name="excelData">Serialized ExcelViewModel JSON</param>
    /// <returns>The Index view with analytics displayed</returns>
    [HttpPost]
    public IActionResult Analytics(string excelData)
    {
        if (string.IsNullOrEmpty(excelData))
        {
            ViewBag.Error = "No data provided for analytics.";
            return View("Index");
        }
        // Deserialize the ExcelViewModel
        var model = System.Text.Json.JsonSerializer.Deserialize<ExcelViewModel>(excelData);
        if (model == null || model.Headers.Count == 0 || model.Rows.Count == 0)
        {
            ViewBag.Error = "Invalid or empty Excel data.";
            return View("Index");
        }
        // Calculate analytics for each column (same as in upload)
        int colCount = model.Headers.Count;
        int rowCount = model.Rows.Count;
        model.Analytics = new List<ColumnAnalytics>();
        for (int col = 0; col < colCount; col++)
        {
            var values = new List<double>();
            for (int row = 0; row < rowCount; row++)
            {
                var cellText = model.Rows[row][col];
                if (double.TryParse(cellText, out double val))
                {
                    values.Add(val);
                }
            }
            var analytics = new ColumnAnalytics
            {
                Header = model.Headers[col],
                Count = values.Count,
                Sum = values.Count > 0 ? values.Sum() : (double?)null,
                Average = values.Count > 0 ? values.Average() : (double?)null,
                Min = values.Count > 0 ? values.Min() : (double?)null,
                Max = values.Count > 0 ? values.Max() : (double?)null
            };
            model.Analytics.Add(analytics);
        }
        return View("Index", model);
    }

    /// <summary>
    /// POST: Home/OptimizeBudget
    /// Handles the budget optimization form. Normalizes sector weights, computes social return and fiscal risk, and returns a risk assessment.
    /// </summary>
    /// <param name="Budget">Total budget input by the user</param>
    /// <param name="Debt">Total national debt</param>
    /// <param name="GDP">Gross Domestic Product</param>
    /// <param name="Revenue">Government revenue</param>
    /// <param name="Lambda">Debt importance factor</param>
    /// <param name="ReduceDebt">Whether to prioritize debt reduction</param>
    /// <param name="Education">Sector weight for Education</param>
    /// <param name="Infrastructure">Sector weight for Infrastructure</param>
    /// <param name="Health">Sector weight for Health</param>
    /// <param name="GovAdmin">Sector weight for Government & Admin</param>
    /// <param name="Other">Sector weight for Other</param>
    /// <param name="InputUnit">Unit of the input values (billion/trillion)</param>
    /// <param name="DebtSustainabilityRisk">Debt sustainability risk (IMF-style model)</param>
    [HttpPost("OptimizeBudget")]
    public IActionResult OptimizeBudget(
        [FromForm] double Budget,
        [FromForm] double Debt,
        [FromForm] double GDP,
        [FromForm] double Revenue,
        [FromForm] double Lambda,
        [FromForm] bool ReduceDebt,
        [FromForm] double Education,
        [FromForm] double Infrastructure,
        [FromForm] double Health,
        [FromForm] double GovAdmin,
        [FromForm] double Other,
        [FromForm] string InputUnit,
        [FromForm] double? InterestRate = null,
        [FromForm] double? GDPGrowth = null,
        [FromForm] double? PrevDebtToGDP = null,
        [FromForm] double? PrimaryBalance = null)
    {
        // Convert all main values to billions for internal calculation
        double multiplier = 1_000_000_000;
        InputUnit = "billion";
        // DEBUG: Log multiplier and InputUnit
        System.Diagnostics.Debug.WriteLine($"[DEBUG] InputUnit: {InputUnit}, Multiplier: {multiplier}");
        Budget *= multiplier;
        Debt *= multiplier;
        GDP *= multiplier;
        Revenue *= multiplier;

        // Normalize sector weights so they sum to 1
        double totalWeight = Education+ Infrastructure + Health + GovAdmin + Other;
        double w1 = Education / totalWeight;
        double w2 = Infrastructure / totalWeight;
        double w3 = Health / totalWeight;
        double w4 = GovAdmin / totalWeight;
        double w5 = Other / totalWeight;

        // Objective 1: Maximize Social Return (as negative for minimization)
        double socialReturn = 0.142 * w1 + 0.207 * w2 + 0.149 * w3 + 0.171 * w4 + 0.331 * w5;
        double f1 = -socialReturn;

        // Objective 2: Minimize Fiscal Risk
        double fiscalRisk = (Budget - Revenue) / Budget;
        if (ReduceDebt)
        {
            fiscalRisk += Lambda * (Debt / GDP);
        }
        // Fiscal risk score for display
        double fiscalRiskScore = (Budget - Revenue) / Budget + (ReduceDebt ? Lambda * (Debt / GDP) : 0);

        // Calculate total spending from budget and allocation weights
        double spent = 0;
        spent += Budget * (Education / totalWeight);
        spent += Budget * (Infrastructure / totalWeight);
        spent += Budget * (Health / totalWeight);
        spent += Budget * (GovAdmin / totalWeight);
        spent += Budget * (Other / totalWeight);
        // Calculate Primary Balance (PBₜ) as (Revenue - Spending) / GDP
        double primaryBalance = GDP != 0 ? (Revenue - spent) / GDP : 0;

        // Debt Sustainability Risk (IMF-style)
        double? debtSustainabilityRisk = null;
        if (InterestRate.HasValue && GDPGrowth.HasValue && PrevDebtToGDP.HasValue)
        {
            // Use calculated PBₜ if not provided
            double PBt = primaryBalance;
            if (PrimaryBalance.HasValue) PBt = PrimaryBalance.Value;
            double r = InterestRate.Value;
            double g = GDPGrowth.Value;
            double Dt_1 = PrevDebtToGDP.Value;
            debtSustainabilityRisk = ((r - g) / (1 + g)) * Dt_1 - PBt;
        }

        // Risk assessment based on fiscal risk score
        string riskLevel = fiscalRiskScore > 1.5 ? "<span class='text-danger fw-bold'>High Fiscal Risk</span>" :
            (fiscalRiskScore > 1.0 ? "<span class='text-warning fw-bold'>Moderate Fiscal Risk</span>" :
            "<span class='text-success fw-bold'>Low Fiscal Risk</span>");

        // Compose the result HTML for display in the view
        string result = $@"
            <h5>Optimization Results</h5>
            <ul>
                <li>Normalized Weights: Education={(w1 * 100.0):N2}%, Infrastructure={(w2 * 100.0):N2}%, Health={(w3 * 100.0):N2}%, Gov/Admin={(w4 * 100.0):N2}%, Other={(w5 * 100.0):N2}%</li>
                <li>Social Return Score: {socialReturn:N3}</li>
                <li>Fiscal Risk Score: {fiscalRiskScore:N3} ({riskLevel})</li>";
        result += $"<li>Primary Balance (PBₜ): {primaryBalance:N3}</li>";
        if (debtSustainabilityRisk.HasValue)
        {
            // Color indicator and interpretation for ΔDₜ
            string dsrClass, dsrIcon, dsrText;
            if (debtSustainabilityRisk.Value <= 0)
            {
                dsrClass = "<span class='text-success fw-bold'>🟢</span>";
                dsrIcon = "🟢";
                dsrText = "Debt is stable or shrinking";
            }
            else if (debtSustainabilityRisk.Value > 0 && debtSustainabilityRisk.Value <= 0.03)
            {
                dsrClass = "<span class='text-warning fw-bold'>🟡</span>";
                dsrIcon = "🟡";
                dsrText = "Slight upward pressure";
            }
            else
            {
                dsrClass = "<span class='text-danger fw-bold'>🔴</span>";
                dsrIcon = "🔴";
                dsrText = "Rapid debt accumulation risk";
            }
            result += $"<li>Debt Sustainability Risk (ΔDₜ): {debtSustainabilityRisk.Value:P2} {dsrClass} - {dsrText}</li>";
        }
        result += "</ul>";
        ViewBag.BudgetResult = result;
        ViewBag.ActiveTab = "budget";
        ViewBag.BudgetForm = new {
            Budget = Budget / multiplier,
            Debt = Debt / multiplier,
            GDP = GDP / multiplier,
            Revenue = Revenue / multiplier,
            Spending = spent / multiplier,
            Lambda,
            ReduceDebt,
            Education,
            Infrastructure,
            Health,
            GovAdmin,
            Other,
            InputUnit,
            InterestRate,
            GDPGrowth,
            PrevDebtToGDP,
            PrimaryBalance = primaryBalance, // always set calculated PBₜ
            NormalizedEducation = w1,
            NormalizedInfrastructure = w2,
            NormalizedHealth = w3,
            NormalizedGovAdmin = w4,
            NormalizedOther = w5,
            DebtSustainabilityRisk = debtSustainabilityRisk
        };
        return View("Index");
    }

    /// <summary>
    /// GET: Home/Privacy
    /// Renders the privacy policy page.
    /// </summary>
    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>
    /// Handles errors and displays the error page with request ID.
    /// </summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
