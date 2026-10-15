using System.Threading.Tasks;
using DentalHouseWebAPI.Models;

namespace DentalHouseWebAPI.Services
{
    public interface IMongoLogService
    {
        Task InsertLogAsync(MongoSystemLog log);
    }
}
