using System.Collections.Generic;
using System.Linq;

namespace SqlLinqer.Components.Generic
{
    public interface IStashable
    {
        bool Stash();
        bool Unstash();
    }    
}