using System;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Select
{
    public class PagingControls : IStashable
    {
        public int Top { get; private set; }
        public int Limit { get; private set; }
        public int Offset { get; private set; }

        public PagingControls()
        {
            Top = 0;
            Limit = 0;
            Offset = 0;
        }

        public void SetTop(int num)
        {
            Top = num;
            Limit = 0;
            Offset = 0;
        }
        public void SetPage(int page, int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 0;

            SetLimit(pageSize, pageSize * (page - 1));
        }
        public void SetLimit(int limit, int offset)
        {
            Top = 0;
            Limit = limit;
            Offset = offset;
        }

        public void Reset()
        {
            Top = 0;
            Limit = 0;
            Offset = 0;
        }

        public string Render(DbFlavor flavor)
        {
            return SqlLinqer.Compilation.SqlDialect.For(flavor).Paging(Top, Limit, Offset);
        }

        private int _Top;
        private int _Limit;
        private int _Offset;
        private bool _stashed = false;
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
