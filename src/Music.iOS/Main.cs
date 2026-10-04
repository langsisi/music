using UIKit;

namespace Music.iOS;

public static class Program
{
    private static void Main(string[] args)
    {
        // 必须最先执行：AppPaths.Root 是静态只读，一旦被其他类型触发就再也改不回来了。
        PlatformPaths.Configure();

        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}