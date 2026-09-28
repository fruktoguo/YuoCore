using System;

namespace YuoCore.Ecs
{
    /// <summary>
    /// YuoCore 的可选运行时观测出口。
    ///
    /// ECS 基座不能反向引用 DaoHua.Domain，因此只暴露宿主注册的委托。
    /// 未注册时所有调用都是无时钟、无分配的空操作。
    /// 宿主以 <c>YUOCORE_TRACE_OFF</c> 编译时全部入口折叠为 default 返回，
    /// JIT 后调用点无残留指令；内核独立构建默认保持启用，保证内核自身观测测试有效。
    /// </summary>
    public static class YuoCoreTrace
    {
#if !YUOCORE_TRACE_OFF
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
#else
        public static bool Enabled => false;

        public static void Configure(Func<string, long> beginHandler, Action<string, long> endHandler, Action<string, long> countHandler)
        {
        }

        public static Measurement Measure(string stage) => default;

        public static void Count(string stage, long count = 1) { }
#endif

        public readonly struct Measurement(string stage, long startedAt) : IDisposable
        {
            public void Dispose()
            {
#if !YUOCORE_TRACE_OFF
                if (startedAt != 0) EndHandler?.Invoke(stage, startedAt);
#else
                _ = stage; _ = startedAt;
#endif
            }
        }
    }
}
