using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    /// <summary>
    /// dic嵌套list
    /// </summary>
    /// <typeparam name="Tk"></typeparam>
    /// <typeparam name="Tv"></typeparam>
    [Serializable]
    public class MultiMap<Tk, Tv> : Dictionary<Tk, List<Tv>>
    {
        // 键缺失时索引器返回共享空集合；只读返回类型保证它不会被误写污染。
        // 需要写入时改用 AddItem/RemoveItem/TryGetValue 取得真实集合。
        private static readonly List<Tv> Empty = new();

        public Tv[] Copy(Tk t)
        {
            TryGetValue(t, out var list);
            return list == null ? Array.Empty<Tv>() : list.ToArray();
        }

        public new IReadOnlyList<Tv> this[Tk t] => TryGetValue(t, out var list) ? list : Empty;

        public void AddItem(Tk t, Tv k)
        {
            if (!TryGetValue(t, out var list))
            {
                list = new List<Tv>();
                Add(t, list);
            }

            list.Add(k);
        }

        public bool RemoveItem(Tk t, Tv k)
        {
            return TryGetValue(t, out var list) && list.Remove(k);
        }

        public List<Tv> GetAll()
        {
            List<Tv> values = new();
            foreach (var item in this)
            {
                values.AddRange(item.Value);
            }

            return values;
        }

        public new bool Remove(Tk t)
        {
            if (!ContainsKey(t))
            {
                return false;
            }

            base.Remove(t);
            return true;
        }
    }

    /// <summary>
    ///  dic嵌套dic
    /// </summary>
    public class MultiDicMap<Tk, Tvk, Tv> : Dictionary<Tk, Dictionary<Tvk, Tv>>
    {
        private static readonly Dictionary<Tvk, Tv> Empty = new();

        public Tv[] Copy(Tk t)
        {
            TryGetValue(t, out var dic);
            return dic == null ? Array.Empty<Tv>() : new List<Tv>(dic.Values).ToArray();
        }

        public Tv[] Copy(Tk t, Tvk tvk)
        {
            TryGetValue(t, out var dic);
            if (dic == null)
                return Array.Empty<Tv>();
            if (dic.TryGetValue(tvk, out var tv))
                return new[] { tv };
            return Array.Empty<Tv>();
        }

        public new IReadOnlyDictionary<Tvk, Tv> this[Tk t] => TryGetValue(t, out var dic) ? dic : Empty;

        public void AddItem(Tk t, Tvk tvk, Tv tv)
        {
            if (!TryGetValue(t, out var dic))
            {
                dic = new Dictionary<Tvk, Tv>();
                Add(t, dic);
            }

            dic.Add(tvk, tv);
        }

        public bool RemoveItem(Tk t, Tvk tvk)
        {
            if (!TryGetValue(t, out var dic))
            {
                return false;
            }

            return dic.Remove(tvk);
        }

        public Tv[] GetAll()
        {
            List<Tv> values = new();
            foreach (var item in this)
            {
                values.AddRange(item.Value.Values);
            }

            return values.ToArray();
        }

        public new bool Remove(Tk t)
        {
            if (!ContainsKey(t))
            {
                return false;
            }

            base.Remove(t);
            return true;
        }
    }

    /// <summary>
    ///  dic嵌套hashset
    /// </summary>
    /// <typeparam name="Tk"></typeparam>
    /// <typeparam name="Tv"></typeparam>
    public class MultiHashSetMap<Tk, Tv> : Dictionary<Tk, HashSet<Tv>>
    {
        private static readonly HashSet<Tv> Empty = new();

        public Tv[] Copy(Tk t)
        {
            TryGetValue(t, out var list);
            return list == null ? Array.Empty<Tv>() : new List<Tv>(list).ToArray();
        }

        public new IReadOnlySet<Tv> this[Tk t] => TryGetValue(t, out var list) ? list : Empty;

        public void AddItem(Tk t, Tv tv)
        {
            if (!TryGetValue(t, out var list))
            {
                list = new HashSet<Tv>();
                Add(t, list);
            }

            list.Add(tv);
        }

        public bool RemoveItem(Tk t, Tv tv)
        {
            return TryGetValue(t, out var list) && list.Remove(tv);
        }

        public Tv[] GetAll()
        {
            List<Tv> values = new();
            foreach (var item in this)
            {
                values.AddRange(item.Value);
            }

            return values.ToArray();
        }

        public new bool Remove(Tk t)
        {
            if (!ContainsKey(t))
            {
                return false;
            }

            base.Remove(t);
            return true;
        }

        public Tv GetFirst(Tk t)
        {
            if (!TryGetValue(t, out var list))
            {
                return default;
            }

            foreach (var item in list)
            {
                return item;
            }

            return default;
        }

        public bool TryGetFirst(Tk t, out Tv tv)
        {
            if (!TryGetValue(t, out var list))
            {
                tv = default;
                return false;
            }

            foreach (var item in list)
            {
                tv = item;
                return true;
            }

            tv = default;
            return false;
        }
    }
}
