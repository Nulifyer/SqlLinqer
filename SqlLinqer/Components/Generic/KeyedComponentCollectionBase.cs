using System.Linq;
using System.Collections.Generic;

namespace SqlLinqer.Components.Generic
{
    /// <summary>A collection of query components indexed by key, with one level of saved state.</summary>
    public abstract class KeyedComponentCollectionBase<TKey, TCom> : IStashable
    {
        private Dictionary<TKey, TCom> _stash;
        /// <summary>The current components indexed by key.</summary>
        protected Dictionary<TKey, TCom> Components;

        /// <summary>The number of current components.</summary>
        public int Count { get => Components.Count; }

        /// <summary>Creates an empty keyed component collection.</summary>
        public KeyedComponentCollectionBase()
        {
            Components = new Dictionary<TKey, TCom>();
        }

        /// <summary>Adds the component if its key is absent and returns the stored component.</summary>
        protected TCom AddComponent(TKey key, TCom component)
        {
            if (!Components.ContainsKey(key))
                Components.Add(key, component);
            return Components[key];
        }
        /// <summary>Stores the component, replacing any component with the same key.</summary>
        protected TCom AddComponentOverwrite(TKey key, TCom component)
        {
            if (Components.ContainsKey(key))
                Components[key] = component;
            else
                Components.Add(key, component);
            return Components[key];
        }
        /// <summary>Removes all current components.</summary>
        protected void Clear()
        {
            Components.Clear();
        }

        /// <summary>Returns whether the collection contains the key.</summary>
        public bool HasKey(TKey key)
        {
            return Components.ContainsKey(key);
        }        
        /// <summary>Returns the component for the key, or the component type's default value if absent.</summary>
        public TCom TryGet(TKey key)
        {
            if (Components.TryGetValue(key, out var value))
                return value;
            return default;
        }
        /// <summary>Returns a copy of the current components, optionally filtered by type.</summary>
        public IEnumerable<TCom> GetAll()
        {
            return Components.Values.ToArray();
        }
        /// <summary>Returns a copy of the current components, optionally filtered by type.</summary>
        public IEnumerable<T> GetAll<T>()
        {
            return Components.Values.OfType<T>().Cast<T>().ToArray();
        }
    
        private bool _stashed = false;
        /// <summary>Saves the current state if no state is already saved. Returns true when saved.</summary>
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
        /// <summary>Restores and clears saved state. Returns true when saved state existed.</summary>
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