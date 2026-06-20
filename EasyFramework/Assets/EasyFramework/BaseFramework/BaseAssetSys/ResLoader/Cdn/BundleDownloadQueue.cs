using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

/// <summary>
/// Bundle 下载队列：同 bundle in-flight 合并；按 resourcePriority 排序待下载项。
/// </summary>
public sealed class BundleDownloadQueue
{
    readonly object gate = new object();
    readonly Dictionary<string, UniTask<bool>> inFlight = new Dictionary<string, UniTask<bool>>(StringComparer.OrdinalIgnoreCase);
    readonly SortedList<DownloadRequest, UniTaskCompletionSource<bool>> pending =
        new SortedList<DownloadRequest, UniTaskCompletionSource<bool>>(new DownloadRequestComparer());

    readonly Func<string, UniTask<bool>> downloadFunc;
    readonly Func<string, int> priorityFunc;
    bool workerRunning;

    struct DownloadRequest
    {
        public string bundleName;
        public int priority;
        public int sequence;
    }

    sealed class DownloadRequestComparer : IComparer<DownloadRequest>
    {
        public int Compare(DownloadRequest x, DownloadRequest y)
        {
            int byPriority = y.priority.CompareTo(x.priority);
            if (byPriority != 0)
                return byPriority;
            return x.sequence.CompareTo(y.sequence);
        }
    }

    int sequenceCounter;

    public BundleDownloadQueue(Func<string, UniTask<bool>> downloadFunc, Func<string, int> priorityFunc)
    {
        this.downloadFunc = downloadFunc ?? throw new ArgumentNullException(nameof(downloadFunc));
        this.priorityFunc = priorityFunc ?? (_ => (int)ResourcePriority.Normal);
    }

    public UniTask<bool> EnqueueAsync(string bundleName)
    {
        bundleName = BundlePlatformPaths.NormalizeBundleName(bundleName);
        if (string.IsNullOrEmpty(bundleName))
            return UniTask.FromResult(false);

        lock (gate)
        {
            if (inFlight.TryGetValue(bundleName, out UniTask<bool> existing))
                return existing;

            var tcs = new UniTaskCompletionSource<bool>();
            pending.Add(new DownloadRequest
            {
                bundleName = bundleName,
                priority = priorityFunc(bundleName),
                sequence = ++sequenceCounter
            }, tcs);

            UniTask<bool> task = tcs.Task;
            inFlight[bundleName] = task;
            EnsureWorker();
            return task;
        }
    }

    public bool Enqueue(string bundleName)
    {
        return EnqueueAsync(bundleName).GetAwaiter().GetResult();
    }

    void EnsureWorker()
    {
        if (workerRunning)
            return;

        workerRunning = true;
        UniTask.Void(ProcessQueueAsync);
    }

    async UniTaskVoid ProcessQueueAsync()
    {
        while (true)
        {
            DownloadRequest request;
            UniTaskCompletionSource<bool> tcs;

            lock (gate)
            {
                if (pending.Count == 0)
                {
                    workerRunning = false;
                    return;
                }

                request = pending.Keys[0];
                tcs = pending.Values[0];
                pending.RemoveAt(0);
            }

            bool ok = false;
            try
            {
                ok = await downloadFunc(request.bundleName);
            }
            catch (Exception)
            {
                ok = false;
            }

            tcs.TrySetResult(ok);

            lock (gate)
            {
                inFlight.Remove(request.bundleName);
            }
        }
    }
}
