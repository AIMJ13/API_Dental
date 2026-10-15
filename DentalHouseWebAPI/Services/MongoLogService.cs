using System;
using System.Threading.Tasks;
using DentalHouseWebAPI.Configurations;
using DentalHouseWebAPI.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace DentalHouseWebAPI.Services
{
    public class MongoLogService : IMongoLogService
    {
        private readonly IMongoCollection<MongoSystemLog>? _collection;

        public MongoLogService(IOptions<MongoLogSettings> options)
        {
            var settings = options.Value;
            if (string.IsNullOrWhiteSpace(settings.ConnectionString) || settings.ConnectionString.Contains("<DB_PASSWORD>"))
            {
                return;
            }

            try
            {
                var client = new MongoClient(settings.ConnectionString);
                var database = client.GetDatabase(settings.DatabaseName);
                _collection = database.GetCollection<MongoSystemLog>(settings.CollectionName);
                InicializarIndices(settings.RetentionDays);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] Error al conectar con MongoDB: {ex.Message}");
            }
        }

        private void InicializarIndices(int retentionDays)
        {
            if (_collection == null) return;
            try
            {
                var ttlKeys = Builders<MongoSystemLog>.IndexKeys.Ascending(x => x.Timestamp);
                var ttlOptions = new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.FromDays(retentionDays)
                };

                var traceKeys = Builders<MongoSystemLog>.IndexKeys.Ascending(x => x.TraceId);
                var levelKeys = Builders<MongoSystemLog>.IndexKeys.Ascending(x => x.Level);

                _collection.Indexes.CreateMany(new[]
                {
                    new CreateIndexModel<MongoSystemLog>(ttlKeys, ttlOptions),
                    new CreateIndexModel<MongoSystemLog>(traceKeys),
                    new CreateIndexModel<MongoSystemLog>(levelKeys)
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] Fallo al inicializar indices en MongoDB: {ex.Message}");
            }
        }

        public async Task InsertLogAsync(MongoSystemLog log)
        {
            if (_collection == null) return;
            try
            {
                await _collection.InsertOneAsync(log);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] Fallo de persistencia en MongoDB: {ex.Message}");
            }
        }
    }
}
