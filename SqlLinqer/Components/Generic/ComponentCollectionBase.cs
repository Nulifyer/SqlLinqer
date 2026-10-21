using System.Collections.Generic;
using System.Linq;

namespace SqlLinqer.Components.Generic
{
    /// <summary>An ordered collection of query components with one level of saved state.</summary>
    public abstract class ComponentCollectionBase<TCom> : IStashable
    {
        private List<TCom> _stash;
        /// <summary>The current components in insertion order.</summary>
        protected List<TCom> Components;

        /// <summary>The number of current components.</summary>
        public int Count { get => Components.Count; }

        /// <summary>Creates an empty component collection.</summary>
        public ComponentCollectionBase()
        {
            Components = new List<TCom>();
        }

        /// <summary>Inserts a component before the current components.</summary>
        protected void AddComponentFirst(TCom component)
        {
            Components.Insert(0, component);
        }
        /// <summary>Appends a component.</summary>
        protected void AddComponent(TCom component)
        {
            Components.Add(component);
        }
        /// <summary>Appends the supplied components in enumeration order.</summary>
        protected void AddComponents(IEnumerable<TCom> components)
        {
            Components.AddRange(components);
        }
        /// <summary>Removes all current components.</summary>
        protected void Clear()
        {
            Components.Clear();
        }

        private bool _stashed = false;
        /// <summary>Saves the current state if no state is already saved. Returns true when saved.</summary>
        public virtual bool Stash()
        {
            if (!_stashed)
            {
                _stash = new List<TCom>();
                _stash.AddRange(Components);
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
                Components = new List<TCom>();
                Components.AddRange(_stash);
                _stash = null;
                _stashed = false;
                return true;
            }
            return false;
        }

        /// <summary>Returns a copy of the current component sequence.</summary>
        protected IEnumerable<TCom> GetAll()
        {
            return Components.ToArray();
        }
    }
}
