using System;

namespace YuoCore.Ecs.Helpers
{
    public static class TimeHelper
    {
        public const long OneDay = 86400000;
        public const long Hour = 3600000;
        public const long Minute = 60000;

        /// <summary>
        /// 客户端时间
        /// </summary>
        /// <returns></returns>
        public static long ClientNow()
        {
            return TimeInfo.Instance.ClientNow();
        }

        public static long ClientNowSeconds()
        {
            return ClientNow() / 1000;
        }

        public static DateTime DateTimeNow()
        {
            return DateTime.Now;
        }

        public static long ServerNow()
        {
            return TimeInfo.Instance.ServerNow();
        }

        public static long ClientFrameTime()
        {
            return TimeInfo.Instance.ClientFrameTime();
        }

        public static long ServerFrameTime()
        {
            return TimeInfo.Instance.ServerFrameTime();
        }
    }

    public class TimeInfo : IDisposable
    {
        public static TimeInfo Instance = new TimeInfo();

        private int timeZone;

        public int TimeZone
        {
            get => timeZone;
            set
            {
                timeZone = value;
                _dt = _dt1970.AddHours(TimeZone);
            }
        }

        private readonly DateTime _dt1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private DateTime _dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public long ServerMinusClientTime { private get; set; }

        public long FrameTime;

        public TimeInfo()
        {
            this.FrameTime = this.ClientNow();
        }

        /// <summary>
        /// 由宿主在世界 tick 中驱动；确定性时钟可重写为固定步长推进
        /// </summary>
        public virtual void Update()
        {
            this.FrameTime = this.ClientNow();
        }

        /// <summary> 
        /// 根据时间戳获取时间 
        /// </summary>  
        public DateTime ToDateTime(long timeStamp)
        {
            return _dt.AddTicks(timeStamp * 10000);
        }

        // 线程安全
        public virtual long ClientNow()
        {
            return (DateTime.UtcNow.Ticks - this._dt1970.Ticks) / 10000;
        }

        public long ServerNow()
        {
            return ClientNow() + this.ServerMinusClientTime;
        }

        public long ClientFrameTime()
        {
            return this.FrameTime;
        }

        public long ServerFrameTime()
        {
            return this.FrameTime + this.ServerMinusClientTime;
        }

        public long Transition(DateTime d)
        {
            return (d.Ticks - _dt.Ticks) / 10000;
        }

        public void Dispose()
        {
            // 不置null，避免后续访问NRE
            // Instance = null;
        }
    }
}