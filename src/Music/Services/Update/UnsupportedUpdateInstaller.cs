using System;

namespace Music.Services.Update;

/// <summary>没有专门实现的平台使用的占位安装器：不支持自更新。</summary>
public sealed class UnsupportedUpdateInstaller : IUpdateInstaller
{
    public bool CanSelfUpdate => false;

    public void Install(string packagePath)
        => throw new NotSupportedException("当前平台不支持应用内自更新。");
}