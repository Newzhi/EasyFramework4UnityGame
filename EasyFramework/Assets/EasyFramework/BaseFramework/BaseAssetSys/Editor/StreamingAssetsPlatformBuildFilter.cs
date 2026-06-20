using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Player 构建前临时移出 StreamingAssets 下非目标平台的 AB 子目录。
/// </summary>
public class StreamingAssetsPlatformBuildFilter : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!StreamingAssetsPlatformIsolation.IsolateForPlayerBuild(report.summary.platform, failBuildOnError: true))
            throw new BuildFailedException(
                "StreamingAssets 平台隔离失败：无法移出非目标平台 AB 目录。请关闭占用 StreamingAssets 的进程后重试。");
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        StreamingAssetsPlatformIsolation.RestoreAfterPlayerBuild();
    }
}
