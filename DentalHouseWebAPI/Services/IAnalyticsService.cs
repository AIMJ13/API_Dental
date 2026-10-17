using System.Collections.Generic;
using System.Threading.Tasks;
using DentalHouseWebAPI.Models.Analytics;

namespace DentalHouseWebAPI.Services
{
    public interface IAnalyticsService
    {
        Task<BusinessKpisDto> GetBusinessKpisAsync(int? year = null, int? month = null);
        Task<BusinessSummaryKpiDto> GetSummaryAsync(int? year = null, int? month = null);
        Task<List<DoctorRevenueKpiDto>> GetTopDoctorsAsync(int? year = null, int? month = null);
        Task<List<ServiceRevenueKpiDto>> GetTopServicesAsync(int? year = null, int? month = null);
        Task<List<SpecialtyRevenueKpiDto>> GetSpecialtiesAsync(int? year = null, int? month = null);
        Task<PatientRecurrenceKpiDto> GetPatientRecurrenceAsync(int? year = null, int? month = null);
        Task<List<MonthlyRevenueKpiDto>> GetMonthlyRevenueAsync();
    }
}
