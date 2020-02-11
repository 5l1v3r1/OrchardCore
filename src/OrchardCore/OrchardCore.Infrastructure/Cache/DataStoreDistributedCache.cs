using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using OrchardCore.Data;
using OrchardCore.Entities;

namespace OrchardCore.Infrastructure.Cache
{
    /// <summary>
    /// A scoped service to keep in sync a given data store with a multi level cache composed of
    /// a scoped level cache, a tenant level memory cache and a tenant level distributed cache.
    /// </summary>
    public class DataStoreDistributedCache<TDataStore> : IDataStoreDistributedCache<TDataStore> where TDataStore : ICacheableDataStore
    {
        private static readonly DefaultIdGenerator _idGenerator = new DefaultIdGenerator();

        private readonly TDataStore _dataStore;
        private readonly IDistributedCache _distributedCache;
        private readonly IMemoryCache _memoryCache;

        private readonly Dictionary<string, object> _scopedCache = new Dictionary<string, object>();

        public DataStoreDistributedCache(TDataStore dataStore, IDistributedCache distributedCache, IMemoryCache memoryCache)
        {
            _dataStore = dataStore;
            _distributedCache = distributedCache;
            _memoryCache = memoryCache;
        }

        public async Task<T> LoadAsync<T>() where T : DistributedCacheData, new()
        {
            var value = await _dataStore.LoadForUpdateAsync<T>();

            if (_memoryCache.TryGetValue<T>(typeof(T).FullName, out var cached) && value == cached)
            {
                throw new ArgumentException("Can't load for update a cached object");
            }

            value.Identifier = _idGenerator.GenerateUniqueId();

            return value;
        }

        public async Task<T> GetAsync<T>(Func<T> factory = null, DistributedCacheEntryOptions options = null, bool checkConsistency = true) where T : DistributedCacheData, new()
        {
            if (options == null)
            {
                options = new DistributedCacheEntryOptions();
            }

            var value = await GetInternalAsync<T>(options);

            if (value == null)
            {
                value = await _dataStore.GetForCachingAsync(factory);

                await SetInternalAsync(value, options, checkConsistency);

                var key = typeof(T).FullName;

                _memoryCache.Set(key, value, new MemoryCacheEntryOptions()
                {
                    AbsoluteExpiration = options.AbsoluteExpiration,
                    AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
                    SlidingExpiration = options.SlidingExpiration
                });

                _scopedCache[key] = value;
            }

            return value;
        }

        public Task UpdateAsync<T>(T value, DistributedCacheEntryOptions options = null, bool checkConcurrency = true, bool checkConsistency = true) where T : DistributedCacheData, new()
        {
            if (_memoryCache.TryGetValue<T>(typeof(T).FullName, out var cached) && value == cached)
            {
                throw new ArgumentException("Can't update a cached object");
            }

            if (options == null)
            {
                options = new DistributedCacheEntryOptions();
            }

            return _dataStore.UpdateAsync(value, value =>
            {
                return SetInternalAsync(value, options, checkConsistency);
            },
                checkConcurrency);
        }

        private async Task<T> GetInternalAsync<T>(DistributedCacheEntryOptions options) where T : DistributedCacheData
        {
            var key = typeof(T).FullName;

            if (_scopedCache.TryGetValue(key, out var scopedValue))
            {
                return (T)scopedValue;
            }

            var idData = await _distributedCache.GetAsync("ID_" + key);

            if (idData == null)
            {
                return null;
            }

            var id = Encoding.UTF8.GetString(idData);

            if (_memoryCache.TryGetValue<T>(key, out var value))
            {
                if (value.Identifier == id)
                {
                    if (options.SlidingExpiration.HasValue)
                    {
                        await _distributedCache.RefreshAsync(key);
                    }

                    _scopedCache[key] = value;

                    return value;
                }
            }

            var data = await _distributedCache.GetAsync(key);

            if (data == null)
            {
                return null;
            }

            using (var ms = new MemoryStream(data))
            {
                value = await DeserializeAsync<T>(ms);
            }

            if (value.Identifier != id)
            {
                return null;
            }

            _memoryCache.Set(key, value, new MemoryCacheEntryOptions()
            {
                AbsoluteExpiration = options.AbsoluteExpiration,
                AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
                SlidingExpiration = options.SlidingExpiration
            });

            _scopedCache[key] = value;

            return value;
        }

        private async Task SetInternalAsync<T>(T value, DistributedCacheEntryOptions options, bool checkConsistency) where T : DistributedCacheData, new()
        {
            value.Identifier ??= _idGenerator.GenerateUniqueId();

            byte[] data;

            using (var ms = new MemoryStream())
            {
                await SerializeAsync(ms, value);
                data = ms.ToArray();
            }

            var idData = Encoding.UTF8.GetBytes(value.Identifier);

            var key = typeof(T).FullName;

            await _distributedCache.SetAsync(key, data, options);
            await _distributedCache.SetAsync("ID_" + key, idData, options);

            // In case we were the last to update the cache, check if it was with the last stored value.
            if (checkConsistency && (await _dataStore.GetForCachingAsync<T>()).Identifier != value.Identifier)
            {
                await _distributedCache.RemoveAsync("ID_" + key);
            }
        }

        private static Task SerializeAsync<T>(Stream stream, T value) =>
            MessagePackSerializer.SerializeAsync(stream, value, ContractlessStandardResolver.Options);

        private static ValueTask<T> DeserializeAsync<T>(Stream stream) =>
            MessagePackSerializer.DeserializeAsync<T>(stream, ContractlessStandardResolver.Options);
    }
}
