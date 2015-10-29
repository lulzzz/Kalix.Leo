﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Kalix.Leo
{
    public static class StreamPipelines
    {
        // 8k buffer size...
        private const int BufferSize = 8192;

        /// <summary>
        /// Read all the bytes from a stream
        /// </summary>
        public static async Task<byte[]> ReadBytes(this IReadAsyncStream readStream)
        {
            using (var ms = new MemoryStream())
            {
                await readStream.CopyToStream(ms, CancellationToken.None).ConfigureAwait(false);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Move all bytes from a read stream to a normal write stream 
        /// (Note: uses sync call on normal stream)
        /// </summary>
        public static async Task CopyToStream(this IReadAsyncStream readStream, Stream stream, CancellationToken ct)
        {
            using (readStream)
            {
                var buffer = new byte[BufferSize];
                int read;
                do
                {
                    ct.ThrowIfCancellationRequested();
                    read = await readStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    if (read > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        stream.Write(buffer, 0, read);
                    }
                } while (read > 0);
            }
        }

        /// <summary>
        /// Straight copy from the reader to the writer
        /// </summary>
        public static async Task CopyToAsync(this IReadAsyncStream readStream, IWriteAsyncStream writeStream, CancellationToken ct)
        {
            using (readStream)
            {
                var buffer = new byte[BufferSize];
                int read;
                do
                {
                    ct.ThrowIfCancellationRequested();
                    read = await readStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    if (read > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        await writeStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    }
                } while (read > 0);
            }
        }

        public static IReadAsyncStream AddTransformer(this IReadAsyncStream stream, Func<Stream, Stream> writeStack)
        {
            return new ReadWriteAsyncStream(stream, writeStack);
        }

        public static IWriteAsyncStream AddTransformer(this IWriteAsyncStream stream, Func<Stream, Stream> writeStack)
        {
            return new WriteWriteAsyncStream(stream, writeStack);
        }

        private class WriteWriteAsyncStream : IWriteAsyncStream
        {
            private readonly IWriteAsyncStream _stream;
            private readonly MemoryStream _ms;
            private readonly Stream _stack;

            private bool _isComplete;

            public WriteWriteAsyncStream(IWriteAsyncStream stream, Func<Stream, Stream> writeStack)
            {
                _stream = stream;
                _ms = new MemoryStream();
                _stack = writeStack(_ms);
            }

            public async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                if(_isComplete) { throw new InvalidOperationException("Cannot write anymore to a completed stream"); }

                ct.ThrowIfCancellationRequested();
                _stack.Write(buffer, offset, count);
                if(_ms.Position > 0)
                {
                    var data = _ms.GetBuffer(); // This does not copy the array! more efficient!
                    ct.ThrowIfCancellationRequested();
                    await _stream.WriteAsync(data, 0, (int)_ms.Length, ct).ConfigureAwait(false);
                    _ms.SetLength(0);
                }
            }

            public async Task Complete(CancellationToken ct)
            {
                if (!_isComplete)
                {
                    _stack.Dispose();
                    if (_ms.Position > 0)
                    {
                        var data = _ms.GetBuffer();
                        await _stream.WriteAsync(data, 0, (int)_ms.Length, ct).ConfigureAwait(false);
                        _ms.SetLength(0);
                    }
                    await _stream.Complete(ct).ConfigureAwait(false);
                    _isComplete = true;
                }
            }

            public Task FlushAsync(CancellationToken ct)
            {
                return Task.FromResult(0);
            }

            public void Dispose()
            {
                if (!_isComplete) { _stack.Dispose(); }
                _ms.Dispose();
                _stream.Dispose();
            }
        }

        private class ReadWriteAsyncStream : IReadAsyncStream
        {
            private readonly IReadAsyncStream _stream;
            private readonly MemoryStream _ms;
            private readonly WriteWatcherStream _watcher;
            private readonly Stream _stack;
            private readonly byte[] _readBuffer;
            
            private int _offset;
            private bool _isReading;
            private int _read;

            public ReadWriteAsyncStream(IReadAsyncStream stream, Func<Stream, Stream> writeStack)
            {
                _isReading = true;
                _read = 1;
                _stream = stream;
                _ms = new MemoryStream();
                _watcher = new WriteWatcherStream(_ms, () => _isReading = false);
                _stack = writeStack(_watcher);
                _readBuffer = new byte[BufferSize];
            }

            public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                int length = 0;
                while(_read > 0 || !_isReading)
                {
                    if (_isReading)
                    {
                        _read = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length, ct).ConfigureAwait(false);
                        if (_read > 0)
                        {
                            _stack.Write(_readBuffer, 0, _read);
                        }
                        else
                        {
                            // Finish the stack
                            _stack.Dispose();
                            _isReading = false;
                        }
                    }
                    else
                    {
                        length = Math.Min((int)_ms.Length - _offset, count);
                        Buffer.BlockCopy(_ms.GetBuffer(), _offset, buffer, offset, length);
                        _offset += length;
                        if(_offset == _ms.Length)
                        {
                            _ms.SetLength(0);
                            _offset = 0;
                            _isReading = true;
                        }
                        break;
                    }
                }
                return length;
            }

            public void Dispose()
            {
                _stack.Dispose();
                _watcher.Dispose();
                _ms.Dispose();
                _stream.Dispose();
            }
        }

        private class WriteWatcherStream : Stream
        {
            private readonly Stream _stream;
            private readonly Action _writeAction;

            public WriteWatcherStream(Stream stream, Action writeAction)
            {
                _stream = stream;
                _writeAction = writeAction;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _stream.Write(buffer, offset, count);
                _writeAction();
            }

            public override bool CanRead
            {
                get
                {
                    return false;
                }
            }

            public override bool CanSeek
            {
                get
                {
                    return false;
                }
            }

            public override bool CanWrite
            {
                get
                {
                    return true;
                }
            }

            public override long Length
            {
                get
                {
                    throw new NotImplementedException();
                }
            }

            public override long Position
            {
                get
                {
                    throw new NotImplementedException();
                }

                set
                {
                    throw new NotImplementedException();
                }
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }
        }
    }
}
