﻿using Kalix.Leo.Encryption;
using Kalix.Leo.Table;
using Lokad.Cloud.Storage.Azure;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CT = Microsoft.Azure.Cosmos.Table;

namespace Kalix.Leo.Azure.Table
{
    public class AzureTableQuery<T> : ITableQuery<T>
    {
        private const string PartitionKey = "PartitionKey";
        private const string RowKey = "RowKey";

        private readonly string _filter;
        private readonly CT.CloudTable _table;
        private readonly IEncryptor _decryptor;
        private readonly int? _take;

        private AzureTableQuery(CT.CloudTable table, IEncryptor decryptor, string filter, int? take)
        {
            _table = table;
            _decryptor = decryptor;
            _filter = filter;
            _take = take;
        }

        public AzureTableQuery(CT.CloudTable table, IEncryptor decryptor)
            : this(table, decryptor, null, null)
        {
        }

        public async Task<T> ById(string partitionKey, string rowKey)
        {
            CT.TableOperation op = CT.TableOperation.Retrieve<FatEntity>(partitionKey, rowKey);
            CT.TableResult result = await _table.ExecuteAsync(op);
            if(result.Result == null)
            {
                return default(T);
            }

            return ConvertFatEntity((FatEntity)result.Result);            
        }

        public Task<T> FirstOrDefault()
        {
            return ExecuteQuery(_filter, 1).FirstOrDefaultAsync().AsTask();
        }

        public Task<int> Count()
        {
            return ExecuteCount(_filter, CancellationToken.None);
        }

        public ITableQuery<T> PartitionKeyEquals(string partitionKey)
        {
            string newFilter = CT.TableQuery.GenerateFilterCondition(PartitionKey, CT.QueryComparisons.Equal, partitionKey);
            return NewQuery(newFilter);
        }

        public ITableQuery<T> PartitionKeyLessThan(string partitionKey)
        {
            string newFilter = CT.TableQuery.GenerateFilterCondition(PartitionKey, CT.QueryComparisons.LessThan, partitionKey);
            return NewQuery(newFilter);
        }

        public ITableQuery<T> PartitionKeyLessThanOrEqual(string partitionKey)
        {
            string newFilter = CT.TableQuery.GenerateFilterCondition(PartitionKey, CT.QueryComparisons.LessThanOrEqual, partitionKey);
            return NewQuery(newFilter);
        }

        public ITableQuery<T> PartitionKeyGreaterThan(string partitionKey)
        {
            string newFilter = CT.TableQuery.GenerateFilterCondition(PartitionKey, CT.QueryComparisons.GreaterThan, partitionKey);
            return NewQuery(newFilter);
        }

        public ITableQuery<T> PartitionKeyGreaterThanOrEqual(string partitionKey)
        {
            string newFilter = CT.TableQuery.GenerateFilterCondition(PartitionKey, CT.QueryComparisons.GreaterThanOrEqual, partitionKey);
            return NewQuery(newFilter);
        }

        public ITableQuery<T> PartitionKeyStartsWith(string partitionKey)
        {
            // .startswith is not supported in table queries...
            // instead: we increase the last char by one
            int length = partitionKey.Length;
            char lastChar = partitionKey[length - 1];
            if (lastChar != char.MaxValue)
            {
                lastChar = Convert.ToChar(Convert.ToInt32(lastChar) + 1);
            }
            string endVal = partitionKey.Substring(0, length - 1) + lastChar;

            string newFilter = CT.TableQuery.CombineFilters(
                CT.TableQuery.GenerateFilterCondition(PartitionKey, CT.QueryComparisons.GreaterThanOrEqual, partitionKey),
                CT.TableOperators.And,
                CT.TableQuery.GenerateFilterCondition(PartitionKey, CT.QueryComparisons.LessThan, endVal));

            return NewQuery(newFilter);
        }


        public ITableQuery<T> RowKeyEquals(string rowKey)
        {
            string newFilter = CT.TableQuery.GenerateFilterCondition(RowKey, CT.QueryComparisons.Equal, rowKey);
            return NewQuery(newFilter);
        }

        public ITableQuery<T> RowKeyLessThan(string rowKey)
        {
            string newFilter = CT.TableQuery.GenerateFilterCondition(RowKey, CT.QueryComparisons.LessThan, rowKey);
            return NewQuery(newFilter);
        }

        public ITableQuery<T> RowKeyLessThanOrEqual(string rowKey)
        {
            string newFilter = CT.TableQuery.GenerateFilterCondition(RowKey, CT.QueryComparisons.LessThanOrEqual, rowKey);
            return NewQuery(newFilter);
        }

        public ITableQuery<T> RowKeyGreaterThan(string rowKey)
        {
            string newFilter = CT.TableQuery.GenerateFilterCondition(RowKey, CT.QueryComparisons.GreaterThan, rowKey);
            return NewQuery(newFilter);
        }

        public ITableQuery<T> RowKeyGreaterThanOrEqual(string rowKey)
        {
            string newFilter = CT.TableQuery.GenerateFilterCondition(RowKey, CT.QueryComparisons.GreaterThanOrEqual, rowKey);
            return NewQuery(newFilter);
        }

        public ITableQuery<T> RowKeyStartsWith(string rowKey)
        {
            // .startswith is not supported in table queries...
            // instead: we increase the last char by one
            int length = rowKey.Length;
            char lastChar = rowKey[length - 1];
            if (lastChar != char.MaxValue)
            {
                lastChar = Convert.ToChar(Convert.ToInt32(lastChar) + 1);
            }
            string endVal = rowKey.Substring(0, length - 1) + lastChar;

            string newFilter = CT.TableQuery.CombineFilters(
                CT.TableQuery.GenerateFilterCondition(RowKey, CT.QueryComparisons.GreaterThanOrEqual, rowKey),
                CT.TableOperators.And,
                CT.TableQuery.GenerateFilterCondition(RowKey, CT.QueryComparisons.LessThan, endVal));

            return NewQuery(newFilter);
        }

        public IAsyncEnumerable<T> AsEnumerable()
        {
            return ExecuteQuery(_filter, _take);
        }

        private ITableQuery<T> NewQuery(string newFilter)
        {
            if (_filter != null)
            {
                newFilter = CT.TableQuery.CombineFilters(_filter, CT.TableOperators.And, newFilter);
            }
            return new AzureTableQuery<T>(_table, _decryptor, newFilter, _take);
        }

        private async IAsyncEnumerable<T> ExecuteQuery(string filter, int? take, [EnumeratorCancellation] CancellationToken token = default)
        {
            var query = new CT.TableQuery<FatEntity>();
            if (filter != null)
            {
                query = query.Where(filter);
            }
            if (take.HasValue)
            {
                query = query.Take(take);
            }

            CT.TableQuerySegment<FatEntity> segment = null;
            do
            {
                segment = await _table.ExecuteQuerySegmentedAsync(query, segment == null ? null : segment.ContinuationToken, token);
                foreach (var entity in segment)
                {
                    yield return ConvertFatEntity(entity);
                    if (token.IsCancellationRequested) { break; }
                }
            }
            while (segment.ContinuationToken != null && !token.IsCancellationRequested);
        }

        private async Task<int> ExecuteCount(string filter, CancellationToken token)
        {
            var query = new CT.TableQuery<FatEntity>();
            if (filter != null)
            {
                query = query.Where(filter);
            }

            // Just select one column to reduce the payload significantly
            query.SelectColumns = new List<string> { "PartitionKey" };

            int count = 0;
            CT.TableQuerySegment<FatEntity> segment = null;
            do
            {
                segment = await _table.ExecuteQuerySegmentedAsync(query, segment?.ContinuationToken, token);
                count += segment.Results.Count;
            }
            while (segment.ContinuationToken != null);
            return count;
        }

        private T ConvertFatEntity(FatEntity fat)
        {
            T result;
            var data = fat.GetData();

            if (data.Length == 0)
            {
                result = default(T);
            }
            else
            {
                if (_decryptor != null)
                {
                    using(var ms = new MemoryStream())
                    {
                        using(var dc = _decryptor.Decrypt(ms, false))
                        {
                            dc.Write(data, 0, data.Length);
                        }

                        data = ms.ToArray();
                    }
                }

                result = JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(data));
            }

            return result;
        }
    }
}
