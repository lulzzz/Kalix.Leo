﻿using Kalix.Leo.Configuration;
using Kalix.Leo.Encryption;
using Kalix.Leo.Indexing;
using Kalix.Leo.Listeners;
using Kalix.Leo.Storage;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace Kalix.Leo
{
    public class LeoEngine : IDisposable, ILeoEngine
    {
        private readonly LeoEngineConfiguration _config;
        private readonly IBackupListener _backupListener;
        private readonly IIndexListener _indexListener;
        private readonly Lazy<IRecordSearchComposer> _composer;
        
        private static readonly object _cacheLock = new object();
        private readonly MemoryCache _cache;
        private readonly CacheItemPolicy _cachePolicy;
        private readonly string _baseName;

        private bool _listenersStarted;
        private bool _hasInitEncryptorContainer;
        private List<IDisposable> _disposables;

        public LeoEngine(LeoEngineConfiguration config)
        {
            _config = config;
            _disposables = new List<IDisposable>();
            _backupListener = config.BackupStore != null && config.BackupQueue != null ? new BackupListener(config.BackupQueue, config.BaseStore, config.BackupStore) : null;
            _indexListener = config.IndexQueue != null ? new IndexListener(config.IndexQueue, config.TypeResolver, config.TypeNameResolver) : null;
            _cache = MemoryCache.Default;
            _cachePolicy = new CacheItemPolicy
            {
                Priority = CacheItemPriority.Default,
                SlidingExpiration = TimeSpan.FromHours(1),
                RemovedCallback = (a) => 
                {
                    var disp = a.CacheItem.Value as IDisposable;
                    if(disp != null)
                    {
                        disp.Dispose();
                    }
                }
            };

            _baseName = "LeoEngine::" + config.UniqueName + "::";
            _composer = new Lazy<IRecordSearchComposer>(() => config.TableStore == null ? null : new RecordSearchComposer(config.TableStore), true);

            if (_indexListener != null)
            {
                if (config.Objects == null)
                {
                    throw new ArgumentNullException("You have not initialised any objects");
                }

                if (config.Objects.Select(o => o.BasePath).Distinct().Count() != config.Objects.Count())
                {
                    throw new ArgumentException("Must have unique base paths accross all objects");
                }

                foreach (var obj in config.Objects.Where(o => o.Type != null && o.Indexer != null))
                {
                    _indexListener.RegisterTypeIndexer(obj.Type, obj.Indexer);
                    if(obj.IndexerAllowFallbackToBasePath)
                    {
                        _indexListener.RegisterPathIndexer(obj.BasePath, obj.Indexer);
                    }
                }

                foreach (var obj in config.Objects.Where(o => o.Type == null && o.Indexer != null))
                {
                    _indexListener.RegisterPathIndexer(obj.BasePath, obj.Indexer);
                }
            }
        }

        public IRecordSearchComposer Composer
        {
            get { return _composer.Value; }
        }

        public IObjectPartition<T> GetObjectPartition<T>(long partitionId)
            where T : ObjectWithAuditInfo
        {
            var config = _config.Objects.FirstOrDefault(o => o.Type == typeof(T));
            if(config == null)
            {
                throw new InvalidOperationException("The object type '" + typeof(T).FullName + "' is not registered");
            }
            var key = _baseName + config.BasePath + "::" + partitionId.ToString(CultureInfo.InvariantCulture);

            return GetCachedValue(key, () => new ObjectPartition<T>(_config, partitionId, config, () => GetEncryptor(partitionId)));
        }

        public IDocumentPartition GetDocumentPartition(string basePath, long partitionId)
        {
            var config = _config.Objects.FirstOrDefault(o => o.Type == null && o.BasePath == basePath);
            if (config == null)
            {
                throw new InvalidOperationException("The document type with base path '" + basePath + "' is not registered");
            }

            var key = _baseName + config.BasePath + "::" + partitionId.ToString(CultureInfo.InvariantCulture);

            return GetCachedValue(key, () => new DocumentPartition(_config, partitionId, config, () => GetEncryptor(partitionId)));
        }

        public Task<IEncryptor> GetEncryptor(long partitionId)
        {
            if (!_hasInitEncryptorContainer && !string.IsNullOrEmpty(_config.KeyContainer))
            {
                _config.BaseStore.CreateContainerIfNotExists(_config.KeyContainer);
                _hasInitEncryptorContainer = true;
            }

            var partitionKey = partitionId.ToString(CultureInfo.InvariantCulture);
            var key = _baseName + "Encryptor::" + partitionKey;
            return GetCachedValue(key, () => CertProtectedEncryptor.CreateEncryptor(_config.BaseStore, new StoreLocation(_config.KeyContainer, partitionKey), _config.RsaCert));
        }

        public void StartListeners(int? messagesToProcessInParallel = null)
        {
            if (_listenersStarted)
            {
                throw new InvalidOperationException("Listeners have already started");
            }

            if(_backupListener != null)
            {
                _disposables.Add(_backupListener.StartListener(_config.UncaughtExceptions, messagesToProcessInParallel));
            }

            if (_indexListener != null)
            {
                _disposables.Add(_indexListener.StartListener(_config.UncaughtExceptions, messagesToProcessInParallel));
            }

            _listenersStarted = true;
        }

        public void Dispose()
        {
            foreach (var d in _disposables)
            {
                d.Dispose();
            }
        }

        private Task<T> GetCachedValue<T>(string key, Func<Task<T>> factory)
            where T : class
        {
            // This is very safe, will only create one. This will not dispose, but that was the case anyways
            var lazy = new Lazy<Task<T>>(factory, true);
            _cache.AddOrGetExisting(key, lazy, _cachePolicy);
            return ((Lazy<Task<T>>)_cache.Get(key)).Value;
        }

        private T GetCachedValue<T>(string key, Func<T> factory)
            where T : class, IDisposable
        {
            // We need this method to produce values that will be disposed when removed from the cache
            // Not so important that we create additional values
            var value = _cache.Get(key);
            if (value == null)
            {
                var newValue = factory();
                value = _cache.AddOrGetExisting(key, newValue, _cachePolicy);
                if (value != null)
                {
                    // We can dispose this one straight away, we are using something that already exists
                    newValue.Dispose();
                }
            }
            return (T)value;
        }

        private static MethodInfo _genericGetPartitionInfo = typeof(LeoEngine).GetMethod("GetObjectPartition");
        private IBasePartition GetPartitionByType(Type type, string container)
        {
            var method = _genericGetPartitionInfo.MakeGenericMethod(type);
            return (IBasePartition)method.Invoke(this, new[] { container });
        }
    }
}
