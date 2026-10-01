using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.DebugCaller.Editor.Tests
{
    public sealed class DebugCallerStateTests
    {
        private bool _hadStoredValue;
        private int _storedValue;

        [SetUp]
        public void SetUp()
        {
            _hadStoredValue = PlayerPrefs.HasKey(DebugCallerState.PlayerPrefsKey);
            _storedValue = PlayerPrefs.GetInt(DebugCallerState.PlayerPrefsKey, 0);
            PlayerPrefs.DeleteKey(DebugCallerState.PlayerPrefsKey);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadStoredValue)
            {
                PlayerPrefs.SetInt(DebugCallerState.PlayerPrefsKey, _storedValue);
            }
            else
            {
                PlayerPrefs.DeleteKey(DebugCallerState.PlayerPrefsKey);
            }

            PlayerPrefs.Save();
        }

        [Test]
        public void Enable_PersistsStateAndAddsVersionSuffixOnce()
        {
            int changeCount = 0;
            void HandleStateChanged() => changeCount++;
            DebugCallerState.StateChanged += HandleStateChanged;

            try
            {
                DebugCallerState.Enable();
                DebugCallerState.Enable();

                Assert.That(DebugCallerState.IsEnabled, Is.True);
                Assert.That(
                    PlayerPrefs.GetInt(DebugCallerState.PlayerPrefsKey),
                    Is.EqualTo(1)
                );
                Assert.That(
                    DebugCallerState.FormatVersion("v1.2.3"),
                    Is.EqualTo("v1.2.3 [debug enabled]")
                );
                Assert.That(
                    DebugCallerState.FormatVersion("v1.2.3 [debug enabled]"),
                    Is.EqualTo("v1.2.3 [debug enabled]")
                );
                Assert.That(
                    DebugCallerState.FormatVersion(string.Empty),
                    Is.EqualTo(" [debug enabled]")
                );
                Assert.That(changeCount, Is.EqualTo(1));
            }
            finally
            {
                DebugCallerState.StateChanged -= HandleStateChanged;
            }
        }

        [Test]
        public void Clear_RemovesPersistentStateAndVersionSuffix()
        {
            PlayerPrefs.SetInt(DebugCallerState.PlayerPrefsKey, 1);

            DebugCallerState.Clear();

            Assert.That(DebugCallerState.IsEnabled, Is.False);
            Assert.That(PlayerPrefs.HasKey(DebugCallerState.PlayerPrefsKey), Is.False);
            Assert.That(
                DebugCallerState.FormatVersion("v1.2.3"),
                Is.EqualTo("v1.2.3")
            );
        }
    }
}
