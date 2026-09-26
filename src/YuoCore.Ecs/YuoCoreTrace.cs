using System;

namespace YuoCore.Ecs
{
    /// <summary>
    /// YuoCore 的可选运行时观测出口。
    ///
    /// ECS 基座不能反向引用 DaoHua.Domain，因此只暴露宿主注册的委托。
    /// 未注册时所有调用都是无时钟、无分配的空操作。
    /// </summary>
    public static class YuoCoreTrace
    {
        public static Func<string, long> BeginHandler { get; private set; }

        public static Action<string, long> EndHandler { get; private set; }

        public static Action<string, long> CountHandler { get; private set; }

        public static bool Enabled => BeginHandler != null || CountHandler != null;

        public static void Configure(Func<string, long> beginHandler, Action<string, long> endHandler, Action<string, long> countHandler)
        {
            BeginHandler = beginHandler;
            EndHandler = endHandler;
            CountHandler = countHandler;
        }

        public static Measurement Measure(string stage)
        {
            return new Measurement(stage, BeginHandler?.Invoke(stage) ?? 0);
        }

        public static void Count(string stage, long count = 1)
        {
            CountHandler?.Invoke(stage, count);
        }

        public readonly struct Measurement(string stage, long startedAt) : IDisposable
        {
            public void Dispose()
            {
                if (startedAt != 0) EndHandler?.Invoke(stage, startedAt);
            }
        }
    }
}
