using System;
using Microsoft.Extensions.DependencyInjection;
using Music.Services;

namespace Music;

/// <summary>
/// 应用级依赖注入容器。
/// 各平台 head（Desktop / Android）在启动时调用 <see cref="Configure"/> 注册平台相关实现，
/// 从而让 Core 完全不依赖任何平台 API。
/// </summary>
public static class AppHost
{
    private static IServiceProvider? _services;

    public static bool IsConfigured => _services is not null;

    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException(
            "AppHost 尚未初始化，请先调用 AppHost.Configure(...)。");

    /// <param name="configurePlatform">
    /// 平台 head 传入的额外注册。在 Core 服务之后执行，因此同类型下平台实现会覆盖 Core 中的默认实现。
    /// </param>
    public static void Configure(Action<IServiceCollection>? configurePlatform = null)
    {
        if (_services is not null)
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddCoreServices();
        configurePlatform?.Invoke(services);
        _services = services.BuildServiceProvider();
    }
}
