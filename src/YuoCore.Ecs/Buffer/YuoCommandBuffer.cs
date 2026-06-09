using System.Collections.Generic;

namespace YuoCore.Ecs
{
    public class YuoCommandBuffer<T>
    {
        HashSet<T> buffer = new();
        HashSet<T> bufferTemp = new();

        public void Add(T component)
        {
            // Debug.Log($"{component} 被加入到命令缓冲区");
            buffer.Add(component);
        }

        public HashSet<T> ExecutionBuffer()
        {
            if (HasBuffer)
            {
                bufferTemp.Clear();
                bufferTemp.UnionWith(buffer); // 优化：一次性批量添加
                buffer.Clear();
            }

            return bufferTemp;
        }

        public bool HasBuffer => buffer.Count > 0;
    }
}