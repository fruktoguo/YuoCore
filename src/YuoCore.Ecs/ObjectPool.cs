using System;
using System.Collections.Concurrent;

namespace YuoCore.Ecs
{
    /// <summary>
    /// 线程安全的通用对象池
    /// 用于减少频繁创建和销毁对象带来的GC压力
    /// </summary>
    /// <typeparam name="T">池化对象类型，必须是引用类型且有无参构造函数</typeparam>
    public class YuoObjectPool<T> where T : class, new()
    {
        private readonly ConcurrentQueue<T> _objects = new ConcurrentQueue<T>();
        private readonly Func<T> _objectGenerator;
        private readonly Action<T> _resetAction;
        private readonly int _maxCapacity;
        private int _currentCount;

        /// <summary>
        /// 创建一个使用默认构造函数的对象池
        /// </summary>
        /// <param name="maxCapacity">最大容量，-1表示无限制</param>
        public YuoObjectPool(int maxCapacity = 100) : this(() => new T(), null, maxCapacity)
        {
        }

        /// <summary>
        /// 创建一个使用自定义生成器的对象池
        /// </summary>
        /// <param name="objectGenerator">对象生成器</param>
        /// <param name="maxCapacity">最大容量，-1表示无限制</param>
        public YuoObjectPool(Func<T> objectGenerator, int maxCapacity = 100)
            : this(objectGenerator, null, maxCapacity)
        {
        }

        /// <summary>
        /// 创建一个使用自定义生成器和重置方法的对象池
        /// </summary>
        /// <param name="objectGenerator">对象生成器</param>
        /// <param name="resetAction">对象归还时的重置方法</param>
        /// <param name="maxCapacity">最大容量，-1表示无限制</param>
        public YuoObjectPool(Func<T> objectGenerator, Action<T> resetAction, int maxCapacity = 100)
        {
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            _resetAction = resetAction;
            _maxCapacity = maxCapacity;
            _currentCount = 0;
        }

        /// <summary>
        /// 从对象池获取一个对象
        /// 如果池中没有可用对象，则创建新对象
        /// </summary>
        /// <returns>池化对象</returns>
        public T Get()
        {
            try
            {
                if (_objects.TryDequeue(out T item))
                {
                    System.Threading.Interlocked.Decrement(ref _currentCount);
                    return item;
                }
                return _objectGenerator();
            }
            catch (Exception e)
            {
                // 降级到直接创建，确保不会因为池化逻辑导致功能失败
                Console.Error.WriteLine($"ObjectPool.Get failed: {e.Message}, falling back to direct creation");
                return new T();
            }
        }

        /// <summary>
        /// 将对象归还到对象池
        /// 如果池已满，对象将被丢弃（由GC回收）
        /// </summary>
        /// <param name="item">要归还的对象</param>
        public void Return(T item)
        {
            if (item == null) return;

            try
            {
                // 执行重置操作
                _resetAction?.Invoke(item);

                // 检查容量限制
                if (_maxCapacity >= 0 && _currentCount >= _maxCapacity)
                {
                    // 池已满，丢弃对象
                    return;
                }

                _objects.Enqueue(item);
                System.Threading.Interlocked.Increment(ref _currentCount);
            }
            catch (Exception e)
            {
                // 静默失败，不影响主逻辑
                Console.Error.WriteLine($"ObjectPool.Return failed: {e.Message}");
            }
        }

        /// <summary>
        /// 当前池中的对象数量
        /// </summary>
        public int Count => _currentCount;

        /// <summary>
        /// 最大容量
        /// </summary>
        public int MaxCapacity => _maxCapacity;

        /// <summary>
        /// 清空对象池
        /// </summary>
        public void Clear()
        {
            while (_objects.TryDequeue(out _))
            {
                System.Threading.Interlocked.Decrement(ref _currentCount);
            }
        }

        /// <summary>
        /// 预热对象池，预先创建指定数量的对象
        /// </summary>
        /// <param name="count">预热数量</param>
        public void Warmup(int count)
        {
            for (int i = 0; i < count && (_maxCapacity < 0 || _currentCount < _maxCapacity); i++)
            {
                Return(_objectGenerator());
            }
        }
    }
}
