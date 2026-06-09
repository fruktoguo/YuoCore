namespace YuoCore.Ecs
{
    public static class YuoEcsExtensions
    {
        /// <summary>
        /// 判断指定的组件是否为null或已释放。
        /// </summary>
        /// <param name="component">需要判断的组件对象。</param>
        /// <returns>如果组件为null或已释放，返回 true；否则返回 false。</returns>
        public static bool IsNullOrDisposed(this YuoComponent component)
        {
            return component == null || component.IsDisposed;
        }

        /// <summary>
        /// 判断指定的实体是否为null或已释放。
        /// </summary>
        /// <param name="entity">需要判断的实体对象。</param>
        /// <returns>如果实体为null或已释放，返回 true；否则返回 false。</returns>
        public static bool IsNullOrDisposed(this YuoEntity entity)
        {
            return entity == null || entity.IsDisposed;
        }

        /// <summary>
        /// 判断指定的组件是否不可用。
        /// </summary>
        /// <param name="component">需要判断的组件对象。</param>
        /// <returns>如果组件为null、已释放或已被销毁，返回 true；否则返回 false。</returns>
        public static bool IsUnusable(this YuoComponent component)
        {
            return component == null || component.IsDisposed || component.IsDestroy;
        }

        /// <summary>
        /// 判断指定的组件是否可用。
        /// </summary>
        /// <param name="component">需要判断的组件对象。</param>
        /// <returns>如果组件可用，返回 true；否则返回 false。</returns>
        public static bool IsUsable(this YuoComponent component) => !component.IsUnusable();

        /// <summary>
        /// 判断指定的实体是否不可用。
        /// </summary>
        /// <param name="entity">需要判断的实体对象。</param>
        /// <returns>如果实体为null、已释放或已被销毁，返回 true；否则返回 false。</returns>
        public static bool IsUnusable(this YuoEntity entity)
        {
            return entity == null || entity.IsDisposed || entity.IsDestroy;
        }

        /// <summary>
        /// 判断指定的实体是否可用。
        /// </summary>
        /// <param name="entity">需要判断的实体对象。</param>
        /// <returns>如果实体可用，返回 true；否则返回 false。</returns>
        public static bool IsUsable(this YuoEntity entity) => !entity.IsUnusable();
    }
}