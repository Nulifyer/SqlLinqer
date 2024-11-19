using System.Collections.Generic;
using System.Linq;

namespace SqlLinqer.Components.Generic
{
    public abstract class ComponentCollectionBase<TCom> : IStashable
    {
        private List<TCom> _stash;
        protected List<TCom> Components;

        public int Count { get => Components.Count; }

        public ComponentCollectionBase()
        {
            Components = new List<TCom>();
        }

        protected void AddComponentFirst(TCom component)
        {
            Components.Insert(0, component);
        }
        protected void AddComponent(TCom component)
        {
            Components.Add(component);
        }
        protected void AddComponents(IEnumerable<TCom> components)
        {
            Components.AddRange(components);
        }
        protected void Clear()
        {
            Components.Clear();
        }

        private bool _stashed = false;
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

        protected IEnumerable<TCom> GetAll()
        {
            return Components.ToArray();
        }
    }
}