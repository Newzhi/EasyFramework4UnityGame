using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 轻量版异步 AutoResetEvent（UniTask 版本兼容）。
/// 语义：Set() 唤醒一个等待者；若无人等待则记一次信号，下一次 WaitAsync 直接通过。
/// </summary>
public sealed class AsyncAutoResetEventLite
{
    private UniTaskCompletionSource core;
    private int signaled;

    public AsyncAutoResetEventLite(bool initialState = false)
    {
        signaled = initialState ? 1 : 0;
    }

    public UniTask WaitAsync()
    {
        if (Interlocked.Exchange(ref signaled, 0) == 1)
        {
            return UniTask.CompletedTask;
        }

        var c = core;
        if (c == null)
        {
            c = new UniTaskCompletionSource();
            Interlocked.CompareExchange(ref core, c, null);
            c = core;
        }

        return c.Task;
    }

    public void Set()
    {
        var c = Interlocked.Exchange(ref core, null);
        if (c != null)
        {
            c.TrySetResult();
            return;
        }

        Interlocked.Exchange(ref signaled, 1);
    }
}
