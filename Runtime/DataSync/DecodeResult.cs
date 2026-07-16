using System;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public readonly struct DecodeResult
    {
        public object Data { get; }
        public Type DataType { get; }
        public VersionTag Version { get; }

        public DecodeResult(object data, Type dataType, VersionTag version)
        {
            Data = data;
            DataType = dataType;
            Version = version;
        }
    }
}
