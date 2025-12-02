using System;

namespace Com.Hapiga.Scheherazade.MVOC
{
    public interface IObjectVariant<TIndex, TVariant>
        where TVariant : IObjectVariant<TIndex, TVariant>
        where TIndex : IEquatable<TIndex>
    {
        VariantController<TIndex, TVariant> Controller { get; }
        TIndex Index { get; }

        bool IsDefault { get; }

    }

}