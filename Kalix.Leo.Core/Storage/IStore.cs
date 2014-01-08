﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Kalix.Leo.Storage
{
    /// <summary>
    /// An interface to hide an underlying storage mechanism, if you can implement this interface you can use
    /// the Kalix engine
    /// </summary>
    public interface IStore
    {
        /// <summary>
        /// Save Data to a specified location
        /// </summary>
        /// <param name="data">Stream of data</param>
        /// <param name="location">Location to store the file</param>
        /// <param name="metadata">Any additional user defined metadata</param>
        /// <param name="multipart">Whether to upload the stream in parts, good for large blobs of data</param>
        Task SaveData(StoreLocation location, DataWithMetadata data, bool? multipart = null);

        /// <summary>
        /// Gets the metadata at a certain location
        /// </summary>
        /// <param name="location">Location to find the metadata</param>
        /// <param name="snapshot">Specific snapshot to find metadata</param>
        /// <returns>Metadata, or null if not found</returns>
        Task<IMetadata> GetMetadata(StoreLocation location, string snapshot = null);

        /// <summary>
        /// Load data from a specified location
        /// </summary>
        /// <param name="location">Location of the file to load</param>
        /// <param name="snapshot">Whether to load a specific snapshot</param>
        /// <returns>Returns whether there was a file found</returns>
        Task<DataWithMetadata> LoadData(StoreLocation location, string snapshot = null); 

        /// <summary>
        /// Find a list of snapshots. Ignores the fact if the file is 'soft' deleted. 
        /// </summary>
        /// <param name="location">The location of the file to find snapshots of</param>
        /// <returns>List of snapshot dates, not guarenteed to be in any order</returns>
        IObservable<Snapshot> FindSnapshots(StoreLocation location);

        /// <summary>
        /// Finds all non-shapshot files in the specified container, with a path prefix if required.
        /// Do not find files that are 'soft' deleted
        /// </summary>
        /// <param name="container">Container to search</param>
        /// <param name="prefix">Prefix of the path to filter by</param>
        /// <returns>List of files</returns>
        IObservable<StoreLocation> FindFiles(string container, string prefix = null);

        /// <summary>
        /// Marks the file as deleted, but snapshots are still available
        /// </summary>
        /// <param name="location">Location of the file</param>
        Task SoftDelete(StoreLocation location);

        /// <summary>
        /// Deletes the file and all snapshots, not recoverable
        /// </summary>
        /// <param name="location">Location of the file</param>
        Task PermanentDelete(StoreLocation location);

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