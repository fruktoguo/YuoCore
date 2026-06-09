using System.Runtime.CompilerServices;

namespace YuoCore.Tasks;

/// <summary>
/// YuoTask 的 async method builder。
/// 同步完成时不租用 completion source；方法真正挂起或异常需要被延迟观察时才创建池化 source。
/// </summary>
public struct YuoTaskMethodBuilder
{
    private YuoTaskCompletionSource? _source;
    private Exception? _exception;
    private bool _completed;

    public static YuoTaskMethodBuilder Create() => default;

    public YuoTask Task
    {
        get
        {
            if (_source is not null)
            {
                return _source.Task;
            }

            if (_exception is not null)
            {
                return YuoTask.FromException(_exception);
            }

            return YuoTask.CompletedTask;
        }
    }

    public void SetResult()
    {
        _completed = true;
        _source?.SetResult();
    }

    public void SetException(Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        _exception = exception;
        if (_source is not null)
        {
            _source.SetException(exception);
        }
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
    }

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        stateMachine.MoveNext();
    }

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        EnsureSource();
        awaiter.OnCompleted(stateMachine.MoveNext);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        EnsureSource();
        awaiter.UnsafeOnCompleted(stateMachine.MoveNext);
    }

    private void EnsureSource()
    {
        if (_source is not null)
        {
            return;
        }

        _source = YuoTaskCompletionSource.Rent();
        if (_exception is not null)
        {
            _source.SetException(_exception);
        }
        else if (_completed)
        {
            _source.SetResult();
        }
    }
}

/// <summary>
/// 带返回值 YuoTask 的 async method builder。
/// 它让内部热路径可以直接声明 async YuoTask&lt;T&gt;，但仍保持一次性消费约束。
/// </summary>
public struct YuoTaskMethodBuilder<T>
{
    private YuoTaskCompletionSource<T>? _source;
    private Exception? _exception;
    private T? _result;
    private bool _hasResult;

    public static YuoTaskMethodBuilder<T> Create() => default;

    public YuoTask<T> Task
    {
        get
        {
            if (_source is not null)
            {
                return _source.Task;
            }

            if (_exception is not null)
            {
                return YuoTask<T>.FromException(_exception);
            }

            return YuoTask<T>.FromResult(_result!);
        }
    }

    public void SetResult(T result)
    {
        _result = result;
        _hasResult = true;
        _source?.SetResult(result);
    }

    public void SetException(Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        _exception = exception;
        if (_source is not null)
        {
            _source.SetException(exception);
        }
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
    }

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        stateMachine.MoveNext();
    }

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        EnsureSource();
        awaiter.OnCompleted(stateMachine.MoveNext);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        EnsureSource();
        awaiter.UnsafeOnCompleted(stateMachine.MoveNext);
    }

    private void EnsureSource()
    {
        if (_source is not null)
        {
            return;
        }

        _source = YuoTaskCompletionSource<T>.Rent();
        if (_exception is not null)
        {
            _source.SetException(_exception);
        }
        else if (_hasResult)
        {
            _source.SetResult(_result!);
        }
    }
}
