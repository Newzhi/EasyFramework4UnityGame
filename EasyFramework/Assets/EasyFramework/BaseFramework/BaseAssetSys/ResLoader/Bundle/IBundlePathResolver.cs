using Cysharp.Threading.Tasks;
using UnityEngine;

public interface IBundlePathResolver
{
    bool TryResolveLocalPath(string bundleName, out string localPath);
    bool IsLocalBundleAvailable(string bundleName);
}

public interface IRemoteBundleProvider
{
    bool EnsureBundle(string bundleName);
    UniTask<bool> EnsureBundleAsync(string bundleName);
}

public sealed class StubRemoteBundleProvider : IRemoteBundleProvider
{
    public bool EnsureBundle(string bundleName)
    {
        Debug.LogWarning("IRemoteBundleProvider not implemented; cannot download bundle: " + bundleName);
        return false;
    }

    public UniTask<bool> EnsureBundleAsync(string bundleName)
    {
        return UniTask.FromResult(EnsureBundle(bundleName));
    }
}
