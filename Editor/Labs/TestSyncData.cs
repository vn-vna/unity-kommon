using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [Serializable]
    [CurrentDataVersion("1.0.0")]
    public class TestSyncData
    {
        public string playerName = "Player";
        public int score;
        public float playTime;

        public override string ToString()
        {
            return $"TestSyncData[v1] Name='{playerName}', Score={score}, PlayTime={playTime:F1}s";
        }
    }

    [Serializable]
    public class TestSyncDataV0
    {
        public string playerName;
        public int score;

        public override string ToString()
        {
            return $"TestSyncData[v0] Name='{playerName}', Score={score}";
        }
    }

    [MigratorVersion("0.0.1", "1.0.0")]
    public class TestSyncDataV0ToV1Migrator : VersionMigrator<TestSyncDataV0, TestSyncData>
    {
        private const float DefaultPlayTime = 60f;

        public override TestSyncData Migrate(TestSyncDataV0 snapshot)
        {
            return new TestSyncData
            {
                playerName = snapshot.playerName,
                score = snapshot.score,
                playTime = DefaultPlayTime
            };
        }
    }
}
