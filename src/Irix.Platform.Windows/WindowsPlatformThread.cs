using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Irix.Platform.Windows;

internal sealed class WindowsPlatformThread : IDisposable
{
    private readonly ConcurrentQueue<IWorkItem> _workItems = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;

    private uint _threadId;
    private bool _isDisposed;

    public WindowsPlatformThread()
    {
        _thread = new Thread(ThreadMain)
        {
            IsBackground = false,
            Name = "Irix Platform Thread"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public T Invoke<T>(Func<T> callback)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(callback);

        if (PInvoke.GetCurrentThreadId() == _threadId)
        {
            return callback();
        }

        var workItem = new WorkItem<T>(callback);
        _workItems.Enqueue(workItem);
        _ = PInvoke.PostThreadMessage(_threadId, PlatformThreadMessages.ExecuteWork, default, default);
        return workItem.GetResult();
    }

    public void Invoke(Action callback)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(callback);

        if (PInvoke.GetCurrentThreadId() == _threadId)
        {
            callback();
            return;
        }

        var workItem = new WorkItem(callback);
        _workItems.Enqueue(workItem);
        _ = PInvoke.PostThreadMessage(_threadId, PlatformThreadMessages.ExecuteWork, default, default);
        workItem.GetResult();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Invoke(static () => PInvoke.PostQuitMessage(0));
        _thread.Join();
        _ready.Dispose();
        _isDisposed = true;
    }

    private void ThreadMain()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        _ready.Set();

        while (PInvoke.GetMessage(out var message, default, 0, 0).Value > 0)
        {
            if (message.message == PlatformThreadMessages.ExecuteWork)
            {
                DrainWorkItems();
                continue;
            }

            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        DrainWorkItems();
    }

    private void DrainWorkItems()
    {
        while (_workItems.TryDequeue(out var workItem))
        {
            workItem.Execute();
        }
    }

    private interface IWorkItem
    {
        void Execute();
    }

    private sealed class WorkItem(Action callback) : IWorkItem
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Execute()
        {
            try
            {
                callback();
                _completion.SetResult();
            }
            catch (Exception exception)
            {
                _completion.SetException(exception);
            }
        }

        public void GetResult()
        {
            _completion.Task.GetAwaiter().GetResult();
        }
    }

    private sealed class WorkItem<T>(Func<T> callback) : IWorkItem
    {
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Execute()
        {
            try
            {
                _completion.SetResult(callback());
            }
            catch (Exception exception)
            {
                _completion.SetException(exception);
            }
        }

        public T GetResult()
        {
            return _completion.Task.GetAwaiter().GetResult();
        }
    }
}
