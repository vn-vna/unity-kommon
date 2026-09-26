using System.Reflection;
using Com.Scheherazade.Common.Singleton;
using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.Singleton.Editor.Tests
{
    public sealed class SingletonBehaviorTests
    {
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            ResetInstance();
            _gameObject = new GameObject(nameof(TestSingleton));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
            ResetInstance();
        }

        [Test]
        public void ReenablingExistingComponent_RestoresStaticInstance()
        {
            TestSingleton singleton = _gameObject.AddComponent<TestSingleton>();
            Assert.That(TestSingleton.Instance, Is.SameAs(singleton));

            // Simulate the static state Unity clears while recompiling scripts.
            ResetInstance();
            _gameObject.SetActive(false);
            _gameObject.SetActive(true);

            Assert.That(TestSingleton.Instance, Is.SameAs(singleton));
        }

        private static void ResetInstance()
        {
            FieldInfo instanceField = typeof(SingletonBehavior<TestSingleton>)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            instanceField.SetValue(null, null);
        }

    }

    public sealed class TestSingleton : SingletonBehavior<TestSingleton>
    {
    }
}
