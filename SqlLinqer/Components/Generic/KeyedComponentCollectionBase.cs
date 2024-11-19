using System.Linq;
using System.Collections.Generic;

namespace SqlLinqer.Components.Generic
{
    public abstract class KeyedComponentCollectionBase<TKey, TCom> : IStashable
    {
        private Dictionary<TKey, TCom> _stash;
        protected Dictionary<TKey, TCom> Components;

        public int Count { get => Components.Count; }

        public KeyedComponentCollectionBase()
        {
            Components = new Dictionary<TKey, TCom>();
        }

        protected TCom AddComponent(TKey key, TCom component)
        {
            if (!Components.ContainsKey(key))
                Components.Add(key, component);
            return Components[key];
        }
        protected TCom AddComponentOverwrite(TKey key, TCom component)
        {
            if (Components.ContainsKey(key))
                Components[key] = component;
            else
                Components.Add(key, component);
            return Components[key];
        }
        protected void Clear()
        {
            Components.Clear();
        }

        public bool HasKey(TKey key)
        {
            return Components.ContainsKey(key);
        }        
        public TCom TryGet(TKey key)
        {
            if (Components.TryGetValue(key, out var value))
                return value;
            return default;
        }
        public IEnumerable<TCom> GetAll()
        {
            return Components.Values.ToArray();
        }
        public IEnumerable<T> GetAll<T>()
        {
            return Components.Values.OfType<T>().Cast<T>().ToArray();
        }
    
        private bool _stashed = false;
        public virtual bool Stash()
        {
            if (!_stashed)
            {
                _stash = new Dictionary<TKey, TCom>();
                foreach (var kv in Components)
                    _stash.Add(kv.Key, kv.Value);
                _stashed = true;
                return true;
            }
            return false;
        }
        public virtual bool Unstash()
        {
            if (_stashed)
            {
                Components = new Dictionary<TKey, TCom>();
                foreach (var kv in _stash)
                    Components.Add(kv.Key, kv.Value);
                _stash = null;
                _stashed = false;
                return true;
            }
            return false;
        }
    }
}