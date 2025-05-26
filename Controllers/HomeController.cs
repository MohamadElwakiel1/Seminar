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
    /// <returns>The Index view with budget optimization results and risk assessment</returns>
    [HttpPost]
    public IActionResult OptimizeBudget(double Budget, double Debt, double GDP, double Revenue, double Lambda, bool ReduceDebt,
        double Education, double Infrastructure, double Health, double GovAdmin, double Other, string InputUnit)
    {
        // Convert all main values to trillions for internal calculation
        double multiplier = 1;
        if (InputUnit == "billion") multiplier = 1_000_000_000;
        else if (InputUnit == "trillion") multiplier = 1_000_000_000_000;
        Budget *= multiplier;
        Debt *= multiplier;
        GDP *= multiplier;
        Revenue *= multiplier;

        // Normalize sector weights so they sum to 1
        double totalWeight = Education + Infrastructure + Health + GovAdmin + Other;
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

        // Risk assessment based on fiscal risk score
        string riskLevel = fiscalRiskScore > 1.5 ? "<span class='text-danger fw-bold'>High Fiscal Risk</span>" :
            (fiscalRiskScore > 1.0 ? "<span class='text-warning fw-bold'>Moderate Fiscal Risk</span>" :
            "<span class='text-success fw-bold'>Low Fiscal Risk</span>");

        // Compose the result HTML for display in the view
        string result = $@"
            <h5>Optimization Results</h5>
            <ul>
                <li>Normalized Weights: Education={w1:P1}, Infrastructure={w2:P1}, Health={w3:P1}, Gov/Admin={w4:P1}, Other={w5:P1}</li>
                <li>Social Return Score: {socialReturn:N3}</li>
                <li>Fiscal Risk Score: {fiscalRiskScore:N3} ({riskLevel})</li>
            </ul>
        ";
        ViewBag.BudgetResult = result;
        ViewBag.ActiveTab = "budget";
        ViewBag.BudgetForm = new {
            Budget = Budget / multiplier,
            Debt = Debt / multiplier,
            GDP = GDP / multiplier,
            Revenue = Revenue / multiplier,
            Lambda,
            ReduceDebt,
            Education,
            Infrastructure,
            Health,
            GovAdmin,
            Other,
            InputUnit
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
