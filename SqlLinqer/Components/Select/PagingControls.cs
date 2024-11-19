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
            switch (flavor)
            {
                case DbFlavor.PostgreSql:
                case DbFlavor.MySql:
                    if (Top > 0)
                    {
                        Limit = Top;
                        Offset = 0;
                        Top = 0;
                    }
                    break;
                case DbFlavor.SqlServer:
                    // nothing
                    break;
                default:
                    throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported by {nameof(PagingControls)}");
            }

            if (Limit > 0)
            {
                switch (flavor)
                {
                    case DbFlavor.PostgreSql:
                    case DbFlavor.MySql:
                        return $" LIMIT {Limit} OFFSET {Offset} ";
                    case DbFlavor.SqlServer:
                        return $" OFFSET {Offset} ROWS FETCH NEXT {Limit} ROWS ONLY";
                    default:
                        throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported by {nameof(PagingControls)}");
                }
            }
            return null;
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
                return true;
            }
            return false;
        }
    }
}