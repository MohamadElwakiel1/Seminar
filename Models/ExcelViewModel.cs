using System.Collections.Generic;

namespace SeminarMvcApp.Models
{
    public class ExcelViewModel
    {
        public List<string> Headers { get; set; } = new List<string>();
        public List<List<string>> Rows { get; set; } = new List<List<string>>();

        // Analytics: summary statistics for each column
        public List<ColumnAnalytics> Analytics { get; set; } = new List<ColumnAnalytics>();
    }

    public class ColumnAnalytics
    {
        public string Header { get; set; } = string.Empty;
        public int Count { get; set; }
        public double? Sum { get; set; }
        public double? Average { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
    }
}
