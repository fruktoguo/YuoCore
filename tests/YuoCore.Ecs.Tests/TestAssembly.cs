// YuoWorld.Instance / ComponentTypeRegistry / ComponentIdCache<T> 是进程级静态状态，
// 并行跑测试会互相踩世界边界；整个程序集串行执行，换取确定性的语义断言。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
