using System.Collections.Generic;

namespace YuoCore.Ecs
{
    public static class YuoWorldExtension
    {
        public static List<SystemBase> SplitOfTag<T>(this List<SystemBase> systems) where T : ISystemTag
        {
            List<SystemBase> result = new();
            foreach (var system in systems)
            {
                if (system.HasTag<T>())
                {
                    result.Add(system);
                }
            }

            return result;
        }

        internal static List<T> Copy<T>(this List<T> collection)
        {
            return new List<T>(collection);
        }

        internal static T[] Copy<T>(this T[] array)
        {
            T[] newArray = new T[array.Length];
            array.CopyTo(newArray, 0);
            return newArray;
        }

        internal static bool Assert(this bool b, string msg = "")
        {
            if (b)
            {
                Console.Error.WriteLine(msg);
            }

            return b;
        }
        internal static bool InverseAssert(this bool b, string msg = "")
        {
            if (!b)
            {
                Console.Error.WriteLine(msg);
            }

            return b;
        }
    }
}
