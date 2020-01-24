using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace OrchardCore.Infrastructure.Cache
{
    public class ScopedDistributedCache : IScopedDistributedCache
    {
        private readonly IDistributedCache _distributedCache;
        private readonly IMemoryCache _memoryCache;

        private readonly Dictionary<string, object> _scopedCache = new Dictionary<string, object>();

        public ScopedDistributedCache(IDistributedCache distributedCache, IMemoryCache memoryCache)
        {
            _distributedCache = distributedCache;
            _memoryCache = memoryCache;
        }

        public async Task<T> GetAsync<T>(string key) where T : ScopedDistributedCacheEntry
        {
            if (_scopedCache.TryGetValue(key, out var scopedValue))
            {
                return (T)scopedValue;
            }

            var cacheIdData = await _distributedCache.GetAsync("ID_" + key);

            if (cacheIdData == null)
            {
                return null;
            }

            var cacheId = Encoding.UTF8.GetString(cacheIdData);

            if (_memoryCache.TryGetValue<T>(key, out var value))
            {
                if (value.CacheId == cacheId)
                {
                    if (value.HasSliding)
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

            if (value.CacheId != cacheId)
            {
                return null;
            }

            _memoryCache.Set(key, value);
            _scopedCache[key] = value;

            return value;
        }

        public async Task<T> GetOrCreateAsync<T>(string key, DistributedCacheEntryOptions options, Func<Task<T>> factory) where T : ScopedDistributedCacheEntry
        {
            var value = await GetAsync<T>(key);

            if (value == null)
            {
                value = await factory();

                await SetAsync(key, value, options);

                _memoryCache.Set(key, value);
                _scopedCache[key] = value;
            }

            return value;
        }

        public async Task SetAsync<T>(string key, T value, DistributedCacheEntryOptions options) where T : ScopedDistributedCacheEntry
        {
            if (options.SlidingExpiration.HasValue)
            {
                value.HasSliding = true;
            }

            byte[] data;

            using (var ms = new MemoryStream())
            {
                await SerializeAsync(ms, value);
                data = ms.ToArray();
            }

            var cacheIdData = Encoding.UTF8.GetBytes(value.CacheId);

            await _distributedCache.SetAsync(key, data, options);
            await _distributedCache.SetAsync("ID_" + key, cacheIdData, options);
            _memoryCache.Set(key, value);
        }

        public async Task RemoveAsync(string key)
        {
            await _distributedCache.RemoveAsync(key);
            await _distributedCache.RemoveAsync("ID_" + key);
            _memoryCache.Remove(key);
        }

        private Task SerializeAsync<T>(Stream stream, T value) => MessagePackSerializer.SerializeAsync(stream, value, ContractlessStandardResolver.Options);

        private ValueTask<T> DeserializeAsync<T>(Stream stream) => MessagePackSerializer.DeserializeAsync<T>(stream, ContractlessStandardResolver.Options);
    }
}
