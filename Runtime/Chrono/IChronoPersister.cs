using System;

namespace Com.Scheherazade.Common.Chrono
{
    public interface IChronoPersister
    {
        void Save(string key, DateTime value);

        DateTime? Load(string key);

        void Delete(string key);
    }
}
