﻿using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Kalix.Leo.Storage
{
    public interface IOptimisticStore : IStore
    {
        /// <summary>
        /// Save data to a specified location, but put a lock on it while writing. Does not support multipart...
        /// </summary>
        /// <param name="data">Stream of data and metadata</param>
        /// <param name="location">Location to store the file</param>
        /// <returns>Whether the write was successful or not</returns>
        Task<bool> TryOptimisticWrite(StoreLocation location, DataWithMetadata data);
    }
}