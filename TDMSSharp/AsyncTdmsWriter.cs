// AsyncTdmsWriter.cs
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TdmsSharp
{
    /// <summary>
    /// Thread-safe asynchronous TDMS writer that serializes write operations from multiple threads.
    /// </summary>
    public class AsyncTdmsWriter : IDisposable
    {
        private readonly TdmsFileWriter _writer;
        private readonly ConcurrentQueue<TdmsWriteCommand> _commandQueue;
        private readonly Task _writerTask;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _commandSignal;
        private volatile bool _isDisposed;
        private Exception? _writerException;

        public AsyncTdmsWriter(string filePath)
        {
            _writer = new TdmsFileWriter(filePath);
            _commandQueue = new ConcurrentQueue<TdmsWriteCommand>();
            _cancellationTokenSource = new CancellationTokenSource();
            _commandSignal = new SemaphoreSlim(0);

            // Start the background writer task
            _writerTask = Task.Run(ProcessCommands, _cancellationTokenSource.Token);
        }

        #region Public Thread-Safe API

        /// <summary>
        /// Buffers numeric data for a channel asynchronously.
        /// Call <see cref="WriteSegmentAsync"/> or <see cref="FlushAsync"/> to persist buffered data.
        /// </summary>
        public void WriteChannelData<T>(string groupName, string channelName, T[] data) where T : unmanaged
        {
            ThrowIfDisposed();
            var command = new WriteChannelDataCommand<T>(groupName, channelName, data);
            EnqueueCommand(command);
        }

        /// <summary>
        /// Buffers numeric data for a channel asynchronously (ReadOnlyMemory version).
        /// Call <see cref="WriteSegmentAsync"/> or <see cref="FlushAsync"/> to persist buffered data.
        /// </summary>
        public void WriteChannelData<T>(string groupName, string channelName, ReadOnlyMemory<T> data) where T : unmanaged
        {
            ThrowIfDisposed();
            var command = new WriteChannelDataCommand<T>(groupName, channelName, data);
            EnqueueCommand(command);
        }

        /// <summary>
        /// Buffers string data for a channel asynchronously.
        /// Call <see cref="WriteSegmentAsync"/> or <see cref="FlushAsync"/> to persist buffered data.
        /// </summary>
        public void WriteStringChannelData(string groupName, string channelName, string[] data)
        {
            ThrowIfDisposed();
            var command = new WriteStringChannelDataCommand(groupName, channelName, data);
            EnqueueCommand(command);
        }

        /// <summary>
        /// Sets a property on the file object asynchronously.
        /// </summary>
        public void SetFileProperty<T>(string name, T value) where T : notnull
        {
            ThrowIfDisposed();
            var command = new SetFilePropertyCommand<T>(name, value);
            EnqueueCommand(command);
        }

        /// <summary>
        /// Sets a property on a group asynchronously.
        /// </summary>
        public void SetGroupProperty<T>(string groupName, string name, T value) where T : notnull
        {
            ThrowIfDisposed();
            var command = new SetGroupPropertyCommand<T>(groupName, name, value);
            EnqueueCommand(command);
        }

        /// <summary>
        /// Sets a property on a channel asynchronously.
        /// </summary>
        public void SetChannelProperty<T>(string groupName, string channelName, string name, T value) where T : notnull
        {
            ThrowIfDisposed();
            var command = new SetChannelPropertyCommand<T>(groupName, channelName, name, value);
            EnqueueCommand(command);
        }

        /// <summary>
        /// Writes a TDMS segment from currently buffered channel data and metadata.
        /// </summary>
        public Task WriteSegmentAsync()
        {
            ThrowIfDisposed();
            var writeSegmentCommand = new WriteSegmentCommand();
            EnqueueCommand(writeSegmentCommand);
            return writeSegmentCommand.CompletionTask;
        }

        /// <summary>
        /// Synchronously writes a TDMS segment from currently buffered channel data and metadata.
        /// </summary>
        public void WriteSegment()
        {
            WriteSegmentAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Requests a flush operation. Returns a task that completes when the flush is done.
        /// </summary>
        public Task FlushAsync()
        {
            ThrowIfDisposed();
            var flushCommand = new FlushCommand();
            EnqueueCommand(flushCommand);
            return flushCommand.CompletionTask;
        }

        /// <summary>
        /// Synchronously flushes all pending writes.
        /// </summary>
        public void Flush()
        {
            FlushAsync().GetAwaiter().GetResult();
        }

        #endregion

        #region Command Processing

        private void EnqueueCommand(TdmsWriteCommand command)
        {
            _commandQueue.Enqueue(command);
            _commandSignal.Release();
        }

        private async Task ProcessCommands()
        {
            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // Wait for a command to be available
                    await _commandSignal.WaitAsync(_cancellationTokenSource.Token);

                    // Process all available commands
                    while (_commandQueue.TryDequeue(out var command))
                    {
                        try
                        {
                            command.Execute(_writer);
                        }
                        catch (Exception ex)
                        {
                            // Store the exception to rethrow on dispose
                            _writerException = ex;
                            command.SetException(ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _writerException = ex;
            }
        }

        #endregion

        #region Disposal

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(AsyncTdmsWriter));

            if (_writerException != null)
                throw new InvalidOperationException("Writer encountered an error", _writerException);
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            if (_writerException == null)
            {
                try
                {
                    FlushAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // Any writer-side failure is captured and rethrown after cleanup.
                }
            }

            _isDisposed = true;

            // Signal shutdown and wait for writer task to complete
            _cancellationTokenSource.Cancel();
            _commandSignal.Release();

            try
            {
                _writerTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Ignore cancellation exceptions during shutdown
            }

            // Dispose resources
            _writer?.Dispose();
            _cancellationTokenSource?.Dispose();
            _commandSignal?.Dispose();

            // Rethrow any writer exception
            if (_writerException != null)
                throw new InvalidOperationException("Writer encountered an error during operation", _writerException);
        }

        #endregion
    }

     /// <summary>
    /// Base class for all TDMS write commands.
    /// </summary>
    public abstract class TdmsWriteCommand
    {
        private TaskCompletionSource<bool>? _completionSource;

        protected TdmsWriteCommand(bool requiresCompletion = false)
        {
            if (requiresCompletion)
                _completionSource = new TaskCompletionSource<bool>();
        }

        public Task CompletionTask => _completionSource?.Task ?? Task.CompletedTask;

        public abstract void Execute(TdmsFileWriter writer);

        internal void SetException(Exception ex)
        {
            _completionSource?.TrySetException(ex);
        }

        protected void SetCompleted()
        {
            _completionSource?.TrySetResult(true);
        }
    }

    /// <summary>
    /// Command to write numeric data to a channel.
    /// </summary>
    internal class WriteChannelDataCommand<T> : TdmsWriteCommand where T : unmanaged
    {
        private readonly string _groupName;
        private readonly string _channelName;
        private readonly ReadOnlyMemory<T> _data;

        public WriteChannelDataCommand(string groupName, string channelName, T[] data)
            : this(groupName, channelName, new ReadOnlyMemory<T>(data))
        {
        }

        public WriteChannelDataCommand(string groupName, string channelName, ReadOnlyMemory<T> data)
        {
            _groupName = groupName;
            _channelName = channelName;
            _data = data;
        }

        public override void Execute(TdmsFileWriter writer)
        {
            var dataType = TdmsDataTypeHelper.GetDataType(typeof(T));
            var channel = writer.GetChannel(_groupName, _channelName) 
                ?? writer.CreateChannel(_groupName, _channelName, dataType);
            
            channel.WriteValues(_data.Span);
        }
    }

    /// <summary>
    /// Command to write string data to a channel.
    /// </summary>
    internal class WriteStringChannelDataCommand : TdmsWriteCommand
    {
        private readonly string _groupName;
        private readonly string _channelName;
        private readonly string[] _data;

        public WriteStringChannelDataCommand(string groupName, string channelName, string[] data)
        {
            _groupName = groupName;
            _channelName = channelName;
            _data = data;
        }

        public override void Execute(TdmsFileWriter writer)
        {
            var channel = writer.GetChannel(_groupName, _channelName) 
                ?? writer.CreateChannel(_groupName, _channelName, TdmsDataType.String);
            
            channel.WriteStrings(_data);
        }
    }

    /// <summary>
    /// Command to write a segment from buffered data.
    /// </summary>
    internal class WriteSegmentCommand : TdmsWriteCommand
    {
        public WriteSegmentCommand() : base(requiresCompletion: true)
        {
        }

        public override void Execute(TdmsFileWriter writer)
        {
            writer.WriteSegment();
            SetCompleted();
        }
    }

    /// <summary>
    /// Command to set a file property.
    /// </summary>
    internal class SetFilePropertyCommand<T> : TdmsWriteCommand where T : notnull
    {
        private readonly string _name;
        private readonly T _value;

        public SetFilePropertyCommand(string name, T value)
        {
            _name = name;
            _value = value;
        }

        public override void Execute(TdmsFileWriter writer)
        {
            writer.SetFileProperty(_name, _value);
        }
    }

    /// <summary>
    /// Command to set a group property.
    /// </summary>
    internal class SetGroupPropertyCommand<T> : TdmsWriteCommand where T : notnull
    {
        private readonly string _groupName;
        private readonly string _name;
        private readonly T _value;

        public SetGroupPropertyCommand(string groupName, string name, T value)
        {
            _groupName = groupName;
            _name = name;
            _value = value;
        }

        public override void Execute(TdmsFileWriter writer)
        {
            var group = writer.CreateGroup(_groupName);
            group.SetProperty(_name, _value);
        }
    }

    /// <summary>
    /// Command to set a channel property.
    /// </summary>
    internal class SetChannelPropertyCommand<T> : TdmsWriteCommand where T : notnull
    {
        private readonly string _groupName;
        private readonly string _channelName;
        private readonly string _name;
        private readonly T _value;

        public SetChannelPropertyCommand(string groupName, string channelName, string name, T value)
        {
            _groupName = groupName;
            _channelName = channelName;
            _name = name;
            _value = value;
        }

        public override void Execute(TdmsFileWriter writer)
        {
            var channel = writer.GetChannel(_groupName, _channelName);
            if (channel == null)
                throw new InvalidOperationException($"Channel {_groupName}/{_channelName} does not exist");
            
            channel.SetProperty(_name, _value);
        }
    }

    /// <summary>
    /// Command to flush the writer.
    /// </summary>
    internal class FlushCommand : TdmsWriteCommand
    {
        public FlushCommand() : base(requiresCompletion: true)
        {
        }

        public override void Execute(TdmsFileWriter writer)
        {
            writer.Flush();
            SetCompleted();
        }
    }
}
