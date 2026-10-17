using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DentalHouseWebAPI.Models.Analytics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DentalHouseWebAPI.Services
{
    public class AnalyticsService : IAnalyticsService
    {
        private readonly string _connectionString;

        public AnalyticsService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AnalyticsDWConnection")
                ?? "Server=localhost;Database=DentalHouseDW;Integrated Security=True;TrustServerCertificate=True;";
        }
        }

        public async Task<BusinessKpisDto> GetBusinessKpisAsync(int? year = null, int? month = null)
        {
            var summary = await GetSummaryAsync(year, month);
            var doctors = await GetTopDoctorsAsync(year, month);
            var services = await GetTopServicesAsync(year, month);
            var specialties = await GetSpecialtiesAsync(year, month);
            var recurrence = await GetPatientRecurrenceAsync(year, month);
            var monthly = await GetMonthlyRevenueAsync();

            return new BusinessKpisDto
            {
                Periodo = summary.Periodo,
                TotalRevenue = summary.TotalRevenue,
                TotalSalesCount = summary.TotalSalesCount,
                AverageTicket = summary.AverageTicket,
                TotalServicesRendered = summary.TotalServicesRendered,
                RevenueGrowthPct = summary.RevenueGrowthPct,
                VolumeVariationPct = summary.VolumeVariationPct,
                PatientRecurrence = recurrence,
                TopDoctors = doctors,
                TopServices = services,
                RevenueBySpecialty = specialties,
                MonthlyRevenue = monthly
            };
        }

        public async Task<BusinessSummaryKpiDto> GetSummaryAsync(int? year = null, int? month = null)
        {
            var result = new BusinessSummaryKpiDto();
            result.Periodo = (year.HasValue && month.HasValue) 
                ? $"Mes {month.Value:D2}/{year.Value}" 
                : (year.HasValue ? $"Año {year.Value}" : "Historico Global");

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var sql = @"
                    SELECT 
                        ISNULL(SUM(f.TotalRevenue), 0) AS TotalRevenue,
                        COUNT(DISTINCT f.SaleId) AS TotalSalesCount,
                        ISNULL(AVG(f.TotalRevenue), 0) AS AverageTicket,
                        ISNULL(SUM(f.Quantity), 0) AS TotalServices
                    FROM Fact_Sales f
                    JOIN Dim_Time t ON f.TimeKey = t.TimeKey
                    WHERE (@Year IS NULL OR t.Year = @Year)
                      AND (@Month IS NULL OR t.Month = @Month);";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Year", (object?)year ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Month", (object?)month ?? DBNull.Value);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    result.TotalRevenue = reader["TotalRevenue"] != DBNull.Value ? Convert.ToDecimal(reader["TotalRevenue"]) : 0;
                    result.TotalSalesCount = reader["TotalSalesCount"] != DBNull.Value ? Convert.ToInt32(reader["TotalSalesCount"]) : 0;
                    result.AverageTicket = reader["AverageTicket"] != DBNull.Value ? Math.Round(Convert.ToDecimal(reader["AverageTicket"]), 2) : 0;
                    result.TotalServicesRendered = reader["TotalServices"] != DBNull.Value ? Convert.ToInt32(reader["TotalServices"]) : 0;
                }
            }

            if (year.HasValue && month.HasValue)
            {
                var monthlyList = await GetMonthlyRevenueAsync();
                var currentIndex = monthlyList.FindIndex(m => m.Year == year.Value && m.Month == month.Value);
                if (currentIndex >= 0)
                {
                    result.RevenueGrowthPct = monthlyList[currentIndex].RevenueGrowthPct;
                    result.VolumeVariationPct = monthlyList[currentIndex].VolumeVariationPct;
                }
            }

            return result;
        }

        public async Task<List<DoctorRevenueKpiDto>> GetTopDoctorsAsync(int? year = null, int? month = null)
        {
            var list = new List<DoctorRevenueKpiDto>();
            decimal totalRevenue = 0;

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var sql = @"
                    SELECT TOP 5 
                        d.FullName AS DoctorName,
                        d.SpecialtyName,
                        ISNULL(SUM(f.TotalRevenue), 0) AS TotalRevenue,
                        ISNULL(SUM(f.Quantity), 0) AS ServicesCount
                    FROM Fact_Sales f
                    JOIN Dim_Doctor d ON f.DoctorKey = d.DoctorKey
                    JOIN Dim_Time t ON f.TimeKey = t.TimeKey
                    WHERE (@Year IS NULL OR t.Year = @Year)
                      AND (@Month IS NULL OR t.Month = @Month)
                    GROUP BY d.FullName, d.SpecialtyName
                    ORDER BY TotalRevenue DESC;";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Year", (object?)year ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Month", (object?)month ?? DBNull.Value);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var rev = Convert.ToDecimal(reader["TotalRevenue"]);
                    totalRevenue += rev;
                    list.Add(new DoctorRevenueKpiDto
                    {
                        DoctorName = reader["DoctorName"].ToString() ?? string.Empty,
                        SpecialtyName = reader["SpecialtyName"].ToString() ?? string.Empty,
                        TotalRevenue = rev,
                        ServicesCount = Convert.ToInt32(reader["ServicesCount"])
                    });
                }
            }

            var globalSummary = await GetSummaryAsync(year, month);
            var baseRevenue = globalSummary.TotalRevenue > 0 ? globalSummary.TotalRevenue : totalRevenue;

            if (baseRevenue > 0)
            {
                foreach (var doc in list)
                {
                    doc.RevenueSharePct = Math.Round((doc.TotalRevenue / baseRevenue) * 100, 2);
                }
            }

            return list;
        }

        public async Task<List<ServiceRevenueKpiDto>> GetTopServicesAsync(int? year = null, int? month = null)
        {
            var list = new List<ServiceRevenueKpiDto>();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var sql = @"
                    SELECT TOP 5 
                        s.ServiceName,
                        ISNULL(SUM(f.Quantity), 0) AS Quantity,
                        ISNULL(SUM(f.TotalRevenue), 0) AS TotalRevenue
                    FROM Fact_Sales f
                    JOIN Dim_Service s ON f.ServiceKey = s.ServiceKey
                    JOIN Dim_Time t ON f.TimeKey = t.TimeKey
                    WHERE (@Year IS NULL OR t.Year = @Year)
                      AND (@Month IS NULL OR t.Month = @Month)
                    GROUP BY s.ServiceName
                    ORDER BY TotalRevenue DESC;";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Year", (object?)year ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Month", (object?)month ?? DBNull.Value);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var qty = Convert.ToInt32(reader["Quantity"]);
                    var rev = Convert.ToDecimal(reader["TotalRevenue"]);
                    var avg = qty > 0 ? Math.Round(rev / qty, 2) : 0;

                    list.Add(new ServiceRevenueKpiDto
                    {
                        ServiceName = reader["ServiceName"].ToString() ?? string.Empty,
                        Quantity = qty,
                        TotalRevenue = rev,
                        AverageRevenuePerService = avg
                    });
                }
            }

            var globalSummary = await GetSummaryAsync(year, month);
            if (globalSummary.TotalRevenue > 0)
            {
                foreach (var srv in list)
                {
                    srv.RevenueSharePct = Math.Round((srv.TotalRevenue / globalSummary.TotalRevenue) * 100, 2);
                }
            }

            return list;
        }

        public async Task<List<SpecialtyRevenueKpiDto>> GetSpecialtiesAsync(int? year = null, int? month = null)
        {
            var list = new List<SpecialtyRevenueKpiDto>();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var sql = @"
                    SELECT 
                        ISNULL(d.SpecialtyName, 'General') AS SpecialtyName,
                        ISNULL(SUM(f.TotalRevenue), 0) AS TotalRevenue
                    FROM Fact_Sales f
                    JOIN Dim_Doctor d ON f.DoctorKey = d.DoctorKey
                    JOIN Dim_Time t ON f.TimeKey = t.TimeKey
                    WHERE (@Year IS NULL OR t.Year = @Year)
                      AND (@Month IS NULL OR t.Month = @Month)
                    GROUP BY d.SpecialtyName
                    ORDER BY TotalRevenue DESC;";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Year", (object?)year ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Month", (object?)month ?? DBNull.Value);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new SpecialtyRevenueKpiDto
                    {
                        SpecialtyName = reader["SpecialtyName"].ToString() ?? string.Empty,
                        TotalRevenue = Convert.ToDecimal(reader["TotalRevenue"])
                    });
                }
            }

            var globalSummary = await GetSummaryAsync(year, month);
            if (globalSummary.TotalRevenue > 0)
            {
                foreach (var esp in list)
                {
                    esp.RevenueSharePct = Math.Round((esp.TotalRevenue / globalSummary.TotalRevenue) * 100, 2);
                }
            }

            return list;
        }

        public async Task<PatientRecurrenceKpiDto> GetPatientRecurrenceAsync(int? year = null, int? month = null)
        {
            var result = new PatientRecurrenceKpiDto();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT 
                    COUNT(DISTINCT PatientKey) AS TotalPatients,
                    COUNT(DISTINCT CASE WHEN VisitCount > 1 THEN PatientKey END) AS RepeatPatients
                FROM (
                    SELECT f.PatientKey, COUNT(DISTINCT f.SaleId) AS VisitCount
                    FROM Fact_Sales f
                    JOIN Dim_Time t ON f.TimeKey = t.TimeKey
                    WHERE (@Year IS NULL OR t.Year = @Year)
                      AND (@Month IS NULL OR t.Month = @Month)
                    GROUP BY f.PatientKey
                ) sub;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Year", (object?)year ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Month", (object?)month ?? DBNull.Value);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var total = reader["TotalPatients"] != DBNull.Value ? Convert.ToInt32(reader["TotalPatients"]) : 0;
                var repeat = reader["RepeatPatients"] != DBNull.Value ? Convert.ToInt32(reader["RepeatPatients"]) : 0;
                var rate = total > 0 ? Math.Round(((decimal)repeat / total) * 100, 2) : 0;

                result.TotalPatients = total;
                result.RepeatPatients = repeat;
                result.RecurrenceRatePct = rate;
            }

            return result;
        }

        public async Task<List<MonthlyRevenueKpiDto>> GetMonthlyRevenueAsync()
        {
            var list = new List<MonthlyRevenueKpiDto>();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var sql = @"
                    SELECT 
                        t.Year,
                        t.Month,
                        t.MonthName,
                        ISNULL(SUM(f.TotalRevenue), 0) AS TotalRevenue,
                        COUNT(DISTINCT f.SaleId) AS TotalSales,
                        ISNULL(SUM(f.Quantity), 0) AS TotalServices
                    FROM Fact_Sales f
                    JOIN Dim_Time t ON f.TimeKey = t.TimeKey
                    GROUP BY t.Year, t.Month, t.MonthName
                    ORDER BY t.Year, t.Month;";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    list.Add(new MonthlyRevenueKpiDto
                    {
                        Year = Convert.ToInt32(reader["Year"]),
                        Month = Convert.ToInt32(reader["Month"]),
                        MonthName = reader["MonthName"].ToString() ?? string.Empty,
                        TotalRevenue = Convert.ToDecimal(reader["TotalRevenue"]),
                        TotalSales = Convert.ToInt32(reader["TotalSales"]),
                        TotalServices = Convert.ToInt32(reader["TotalServices"])
                    });
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                {
                    var prevRevenue = list[i - 1].TotalRevenue;
                    if (prevRevenue > 0)
                    {
                        list[i].RevenueGrowthPct = Math.Round(((list[i].TotalRevenue - prevRevenue) / prevRevenue) * 100, 2);
                    }

                    var prevServices = list[i - 1].TotalServices;
                    if (prevServices > 0)
                    {
                        list[i].VolumeVariationPct = Math.Round(((decimal)(list[i].TotalServices - prevServices) / prevServices) * 100, 2);
                    }
                }
            }

            return list;
        }

    }
}
