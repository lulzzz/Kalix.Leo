﻿using Kalix.Leo.Encryption;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kalix.Leo.Storage
{
    public interface ISecureStore
    {
        bool CanCompress { get; }
        bool CanIndex { get; }
        bool CanBackup { get; }

        Task<DataWithMetadata> LoadData(StoreLocation location, string snapshot = null, IEncryptor encryptor = null);
        Task<ObjectWithMetadata<T>> LoadObject<T>(StoreLocation location, string snapshot = null, IEncryptor encryptor = null) where T : ObjectWithAuditInfo;

        Task<Metadata> GetMetadata(StoreLocation location, string snapshot = null);

        Task<Metadata> SaveData(StoreLocation location, Metadata metadata, UpdateAuditInfo audit, Func<IWriteAsyncStream, Task> savingFunc, CancellationToken token, IEncryptor encryptor = null, SecureStoreOptions options = SecureStoreOptions.All);
        Task<Metadata> SaveObject<T>(StoreLocation location, ObjectWithMetadata<T> obj, UpdateAuditInfo audit, IEncryptor encryptor = null, SecureStoreOptions options = SecureStoreOptions.All) where T : ObjectWithAuditInfo;
        Task<Metadata> SaveMetadata(StoreLocation location, Metadata metadata, SecureStoreOptions options = SecureStoreOptions.All);

        Task Delete(StoreLocation location, UpdateAuditInfo audit, SecureStoreOptions options = SecureStoreOptions.All);

        IAsyncEnumerable<Snapshot> FindSnapshots(StoreLocation location);
        IAsyncEnumerable<LocationWithMetadata> FindFiles(string container, string prefix = null);

        Task ForceObjectIndex<T>(StoreLocation location, Metadata metadata = null);
        Task ForceIndex(StoreLocation location, Metadata metadata);

        Task ReIndexAll(string container, Func<LocationWithMetadata, bool> filter, string prefix = null);
        Task BackupAll(string container, string prefix = null);

        Task<IAsyncDisposable> Lock(StoreLocation location);
        Task RunOnce(StoreLocation location, Func<Task> action);
        IAsyncEnumerable<bool> RunEvery(StoreLocation location, TimeSpan interval, Action<Exception> unhandledExceptions = null);

        IUniqueIdGenerator GetIdGenerator(StoreLocation location);

        /// <summary>
        /// Make sure a container exists
        /// </summary>
        /// <param name="container">Name of the container to create</param>
        Task CreateContainerIfNotExists(string container);

        /// <summary>
        /// Delete a container if it exists
        /// </summary>
        /// <param name="container">Name of the container to delete</param>
        Task PermanentDeleteContainer(string container);
    }
}
