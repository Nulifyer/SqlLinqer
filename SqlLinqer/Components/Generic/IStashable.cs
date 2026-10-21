using System.Collections.Generic;
using System.Linq;

namespace SqlLinqer.Components.Generic
{
    /// <summary>Saves and restores one level of mutable state.</summary>
    public interface IStashable
    {
        /// <summary>Saves the current state if no state is already saved. Returns true when saved.</summary>
        bool Stash();
        /// <summary>Restores and clears saved state. Returns true when saved state existed.</summary>
        bool Unstash();
    }
}
