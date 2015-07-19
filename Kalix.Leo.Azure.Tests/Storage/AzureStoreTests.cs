﻿using Kalix.Leo.Azure.Storage;
using Kalix.Leo.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;

namespace Kalix.Leo.Azure.Tests.Storage
{
    [TestFixture]
    public class AzureStoreTests
    {
        protected AzureStore _store;
        protected CloudBlockBlob _blob;
        protected StoreLocation _location;

        [SetUp]
        public virtual void Init()
        {
            _store = new AzureStore(CloudStorageAccount.DevelopmentStorageAccount.CreateCloudBlobClient(), true);

            _blob = AzureTestsHelper.GetBlockBlob("kalix-leo-tests", "AzureStoreTests.testdata", true);
            _location = new StoreLocation("kalix-leo-tests", "AzureStoreTests.testdata");
        }

        protected string WriteData(StoreLocation location, Metadata m, byte[] data)
        {
            return _store.SaveData(location, m, async s =>
            {
                await s.WriteAsync(data, 0, data.Length);
                return data.Length;
            }).Result.Snapshot;
        }

        protected OptimisticStoreWriteResult TryOptimisticWrite(StoreLocation location, Metadata m, byte[] data)
        {
            return _store.TryOptimisticWrite(location, m, async s =>
            {
                await s.WriteAsync(data, 0, data.Length);
                return data.Length;
            }).Result;
        }

        [TestFixture]
        public class SaveDataMethod : AzureStoreTests
        {
            [Test]
            public void HasMetadataCorrectlySavesIt()
            {
                var data = AzureTestsHelper.RandomData(1);
                var m = new Metadata();
                m["metadata1"] = "somemetadata";
                WriteData(_location, m, data);

                _blob.FetchAttributes();
                Assert.AreEqual("somemetadata", _blob.Metadata["metadata1"]);
            }

            [Test]
            public void AlwaysOverridesMetadata()
            {
                var data = AzureTestsHelper.RandomData(1);
                var m = new Metadata();
                m["metadata1"] = "somemetadata";
                WriteData(_location, m, data);

                var m2 = new Metadata();
                m2["metadata2"] = "othermetadata";
                WriteData(_location, m2, data);

                _blob.FetchAttributes();
                Assert.IsFalse(_blob.Metadata.ContainsKey("metadata1"));
                Assert.AreEqual("othermetadata", _blob.Metadata["metadata2"]);
            }

            [Test]
            public void MultiUploadLargeFileIsSuccessful()
            {
                var data = AzureTestsHelper.RandomData(7);
                WriteData(_location, null, data);

                Assert.IsTrue(_blob.Exists());
            }
        }

        [TestFixture]
        public class TryOptimisticWriteMethod : AzureStoreTests
        {
            [Test]
            public void HasMetadataCorrectlySavesIt()
            {
                var data = AzureTestsHelper.RandomData(1);
                var m = new Metadata();
                m["metadata1"] = "somemetadata";
                var success = TryOptimisticWrite(_location, m, data);

                _blob.FetchAttributes();
                Assert.IsTrue(success.Result);
                Assert.IsNotNull(success.Metadata.Snapshot);
                Assert.AreEqual("somemetadata", _blob.Metadata["metadata1"]);
            }

            [Test]
            public void AlwaysOverridesMetadata()
            {
                var data = AzureTestsHelper.RandomData(1);
                var m = new Metadata();
                m["metadata1"] = "somemetadata";
                var success1 = TryOptimisticWrite(_location, m, data);
                var oldMetadata = _store.GetMetadata(_location).Result;

                var m2 = new Metadata();
                m2.ETag = oldMetadata.ETag;
                m2["metadata2"] = "othermetadata";
                var success2 = TryOptimisticWrite(_location, m2, data);
                var newMetadata = _store.GetMetadata(_location).Result;

                _blob.FetchAttributes();
                Assert.IsTrue(success1.Result, "first write failed");
                Assert.IsTrue(success2.Result, "second write failed");
                Assert.AreEqual(success1.Metadata.Snapshot, oldMetadata.Snapshot);
                Assert.AreEqual(success2.Metadata.Snapshot, newMetadata.Snapshot);
                Assert.AreNotEqual(success1.Metadata.Snapshot, success2.Metadata.Snapshot);
                Assert.IsFalse(_blob.Metadata.ContainsKey("metadata1"));
                Assert.AreEqual("othermetadata", _blob.Metadata["metadata2"]);
            }

            [Test]
            public void NoETagMustBeNewSave()
            {
                var data = AzureTestsHelper.RandomData(1);
                var success1 = TryOptimisticWrite(_location, null, data);
                var success2 = TryOptimisticWrite(_location, null, data);

                Assert.IsTrue(success1.Result, "first write failed");
                Assert.IsFalse(success2.Result, "second write succeeded");
            }

            [Test]
            public void ETagDoesNotMatchFails()
            {
                var data = AzureTestsHelper.RandomData(1);
                var metadata = new Metadata { ETag = "notreal" };
                var success = TryOptimisticWrite(_location, metadata, data);

                Assert.IsFalse(success.Result, "write should not have succeeded with fake eTag");
            }

            [Test]
            public void MultiUploadLargeFileIsSuccessful()
            {
                var data = AzureTestsHelper.RandomData(7);
                var success = TryOptimisticWrite(_location, null, data);

                Assert.IsTrue(success.Result);
                Assert.IsNotNull(success.Metadata.Snapshot);
                Assert.IsTrue(_blob.Exists());
            }
        }

        [TestFixture]
        public class GetMetadataMethod : AzureStoreTests
        {
            [Test]
            public void NoFileReturnsNull()
            {
                var result = _store.GetMetadata(_location).Result;
                Assert.IsNull(result);
            }

            [Test]
            public void FindsMetadataIncludingSizeAndLength()
            {
                var data = AzureTestsHelper.RandomData(1);
                var m = new Metadata();
                m["metadata1"] = "somemetadata";
                WriteData(_location, m, data);

                var result = _store.GetMetadata(_location).Result;

                Assert.AreEqual("1048576", result[MetadataConstants.ContentLengthMetadataKey]);
                Assert.IsTrue(result.ContainsKey(MetadataConstants.ModifiedMetadataKey));
                Assert.IsNotNull(result.Snapshot);
                Assert.AreEqual("somemetadata", result["metadata1"]);
            }
        }

        [TestFixture]
        public class LoadDataMethod : AzureStoreTests
        {
            [Test]
            public void MetadataIsTransferedWhenSelectingAStream()
            {
                var data = AzureTestsHelper.RandomData(1);
                var m = new Metadata();
                m["metadata1"] = "metadata";
                WriteData(_location, m, data);

                var result = _store.LoadData(_location).Result;
                Assert.IsNotNull(result.Metadata.Snapshot);
                Assert.AreEqual("metadata", result.Metadata["metadata1"]);
            }

            [Test]
            public void NoFileReturnsFalse()
            {
                var result = _store.LoadData(_location).Result;
                Assert.IsNull(result);
            }

            [Test]
            public void NoContainerReturnsFalse()
            {
                var result = _store.LoadData(new StoreLocation("blahblahblah", "blah")).Result;
                Assert.IsNull(result);
            }

            [Test]
            public void FileMarkedAsDeletedReturnsNull()
            {
                var data = AzureTestsHelper.RandomData(1);
                var m = new Metadata();
                m["leodeleted"] = DateTime.UtcNow.Ticks.ToString();
                WriteData(_location, m, data);

                var result = _store.LoadData(_location).Result;
                Assert.IsNull(result);
            }

            [Test]
            public void AllDataLoadsCorrectly()
            {
                var data = AzureTestsHelper.RandomData(1);
                WriteData(_location, null, data);

                var result = _store.LoadData(_location).Result;
                byte[] resData;
                using(var ms = new MemoryStream())
                {
                    result.Stream.CopyTo(ms);
                    resData = ms.ToArray();
                }
                Assert.IsTrue(data.SequenceEqual(resData));
            }
        }

        [TestFixture]
        public class FindSnapshotsMethod : AzureStoreTests
        {
            [Test]
            public void NoSnapshotsReturnsEmpty()
            {
                var snapshots = _store.FindSnapshots(_location).ToEnumerable();

                Assert.AreEqual(0, snapshots.Count());
            }

            [Test]
            public void SingleSnapshotCanBeFound()
            {
                var data = AzureTestsHelper.RandomData(1);
                var m = new Metadata();
                m["metadata1"] = "metadata";
                WriteData(_location, m, data);

                var snapshots = _store.FindSnapshots(_location).ToEnumerable();

                Assert.AreEqual(1, snapshots.Count());
            }

            [Test]
            public void SubItemBlobSnapshotsAreNotIncluded()
            {
                var data = AzureTestsHelper.RandomData(1);
                WriteData(_location, null, data);

                var blob2 = AzureTestsHelper.GetBlockBlob("kalix-leo-tests", "AzureStoreTests.testdata/subitem.data", true);
                var location2 = new StoreLocation("kalix-leo-tests", "AzureStoreTests.testdata/subitem.data");

                WriteData(location2, null, data);

                var snapshots = _store.FindSnapshots(_location).ToEnumerable();

                Assert.AreEqual(1, snapshots.Count());
            }
        }

        [TestFixture]
        public class LoadDataMethodWithSnapshot : AzureStoreTests
        {
            [Test]
            public void MetadataIsTransferedWhenSelectingAStream()
            {
                var data = AzureTestsHelper.RandomData(1);
                var m = new Metadata();
                m["metadata1"] = "metadata";
                var shapshot = WriteData(_location, m, data);

                var res = _store.LoadData(_location, shapshot).Result;
                Assert.AreEqual(shapshot, res.Metadata.Snapshot);
                Assert.AreEqual("metadata", res.Metadata["metadata1"]);
            }

            [Test]
            public void NoFileReturnsFalse()
            {
                var result = _store.LoadData(_location, DateTime.UtcNow.Ticks.ToString()).Result;
                Assert.IsNull(result);
            }
        }

        [TestFixture]
        public class SoftDeleteMethod : AzureStoreTests
        {
            [Test]
            public void BlobThatDoesNotExistShouldNotThrowError()
            {
                _store.SoftDelete(_location).Wait();
            }

            [Test]
            public void BlobThatIsSoftDeletedShouldNotBeLoadable()
            {
                var data = AzureTestsHelper.RandomData(1);
                WriteData(_location, null, data);

                _store.SoftDelete(_location).Wait();

                var result = _store.LoadData(_location).Result;
                Assert.IsNull(result);
            }

            [Test]
            public void ShouldNotDeleteSnapshots()
            {
                var data = AzureTestsHelper.RandomData(1);
                WriteData(_location, null, data);
                var shapshot = _store.FindSnapshots(_location).ToEnumerable().Single().Id;

                _store.SoftDelete(_location).Wait();

                var result = _store.LoadData(_location, shapshot).Result;
                Assert.IsNotNull(result);
            }
        }

        [TestFixture]
        public class PermanentDeleteMethod : AzureStoreTests
        {
            [Test]
            public void BlobThatDoesNotExistShouldNotThrowError()
            {
                _store.PermanentDelete(_location).Wait();
            }

            [Test]
            public void BlobThatIsSoftDeletedShouldNotBeLoadable()
            {
                var data = AzureTestsHelper.RandomData(1);
                WriteData(_location, null, data);

                _store.PermanentDelete(_location).Wait();

                var result = _store.LoadData(_location).Result;
                Assert.IsNull(result);
            }

            [Test]
            public void ShouldDeleteAllSnapshots()
            {
                var data = AzureTestsHelper.RandomData(1);
                WriteData(_location, null, data);
                var shapshot = _store.FindSnapshots(_location).ToEnumerable().Single().Id;

                _store.PermanentDelete(_location).Wait();

                var result = _store.LoadData(_location, shapshot).Result;
                Assert.IsNull(result);
            }
        }

        [TestFixture]
        public class LockMethod : AzureStoreTests
        {
            [Test]
            public void LockSuceedsEvenIfNoFile()
            {
                using(var l = _store.Lock(_location).Result)
                {
                    Assert.IsNotNull(l);
                }
            }

            [Test]
            public void IfAlreadyLockedOtherLocksFail()
            {
                using(var l = _store.Lock(_location).Result)
                using(var l2 = _store.Lock(_location).Result)
                {
                    Assert.IsNotNull(l);
                    Assert.IsNull(l2);
                }
            }

            [Test]
            [ExpectedException(typeof(LockException))]
            public void IfFileLockedReturnsFalse()
            {
                using (var l = _store.Lock(_location).Result)
                {
                    var data = AzureTestsHelper.RandomData(1);
                    try
                    {
                        WriteData(_location, null, data);
                    }
                    catch (AggregateException e)
                    {
                        throw e.InnerException;
                    }
                }
            }
        }
    }
}
