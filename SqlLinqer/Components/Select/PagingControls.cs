using System;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Select
{
    /// <summary>Row limits and offsets used when rendering a SELECT query.</summary>
    public class PagingControls : IStashable
    {
        /// <summary>The maximum row count set by SetTop, or zero when unset.</summary>
        public int Top { get; private set; }
        /// <summary>The page size set by SetLimit, or zero when unset.</summary>
        public int Limit { get; private set; }
        /// <summary>The number of rows to skip.</summary>
        public int Offset { get; private set; }

        /// <summary>Creates paging controls without a row limit or offset.</summary>
        public PagingControls()
        {
            Top = 0;
            Limit = 0;
            Offset = 0;
        }

        /// <summary>Sets the maximum row count and clears the page limit and offset.</summary>
        public void SetTop(int num)
        {
            Top = num;
            Limit = 0;
            Offset = 0;
        }
        /// <summary>Sets a one-based page and page size, clamping pages below one and negative sizes.</summary>
        public void SetPage(int page, int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 0;

            SetLimit(pageSize, pageSize * (page - 1));
        }
        /// <summary>Sets the row limit and offset and clears the TOP value.</summary>
        public void SetLimit(int limit, int offset)
        {
            Top = 0;
            Limit = limit;
            Offset = offset;
        }

        /// <summary>Clears all row limits and offsets.</summary>
        public void Reset()
        {
            Top = 0;
            Limit = 0;
            Offset = 0;
        }

        /// <summary>Renders this component for the selected database dialect.</summary>
        public string Render(DbFlavor flavor)
        {
            return SqlLinqer.Compilation.SqlDialect.For(flavor).Paging(Top, Limit, Offset);
        }

        private int _Top;
        private int _Limit;
        private int _Offset;
        private bool _stashed = false;
        /// <summary>Saves the current state if no state is already saved. Returns true when saved.</summary>
        public virtual bool Stash()
        {
            if (!_stashed)
            {
                _Top = Top;
                _Limit = Limit;
                _Offset = Offset;
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
                Top = _Top;
                Limit = _Limit;
                Offset = _Offset;
                _stashed = false;
                return true;
            }
            return false;
        }
    }
}
