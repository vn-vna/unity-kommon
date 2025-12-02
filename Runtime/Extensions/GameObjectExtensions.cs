using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    public static class GameObjectUtils
    {
        public static GameObject FindChild(this GameObject gameObject, string name)
        {
            foreach (Transform child in gameObject.transform)
            {
                if (child.name == name) return child.gameObject;
            }

            return null;
        }

        public static T FindComponentInChildren<T>(this GameObject gameObject, string name)
            where T : Component
        {
            var child = gameObject.FindChild(name);
            return child != null ? child.GetComponent<T>() : null;
        }

        public static T FindComponentInChildren<T>(this Component comp, string name)
            where T : Component
        {
            var child = comp.gameObject.FindChild(name);
            return child != null ? child.GetComponent<T>() : null;
        }

        public static void SetActiveGameObject<T>(this T comp, bool isActive)
            where T : Component
        {
            comp.gameObject.SetActive(isActive);
        }

        public static void SetActiveGameObjectRecursive(this GameObject gameObject, bool isActive)
        {
            gameObject.SetActive(isActive);
            foreach (Transform child in gameObject.transform) child.gameObject.SetActiveGameObjectRecursive(isActive);
        }

        public static void SetLayerRecursively(this GameObject gameObject, int layer)
        {
            gameObject.layer = layer;
            foreach (Transform child in gameObject.transform) child.gameObject.SetLayerRecursively(layer);
        }

        public static T CompareTags<T>(this T component, string[] tags)
            where T : Component
        {
            foreach (var tag in tags)
                if (component.gameObject.CompareTag(tag))
                    return component;

            return null;
        }

        public static T SetPosition<T>(this T component, Vector3 position)
            where T : Component
        {
            component.transform.position = position;
            return component;
        }

        public static T SetPositionX<T>(this T component, float x)
            where T : Component
        {
            component.transform.position =
                new Vector3(x, component.transform.position.y, component.transform.position.z);
            return component;
        }

        public static T SetPositionY<T>(this T component, float y)
            where T : Component
        {
            component.transform.position =
                new Vector3(component.transform.position.x, y, component.transform.position.z);
            return component;
        }

        public static T SetPositionZ<T>(this T component, float z)
            where T : Component
        {
            component.transform.position =
                new Vector3(component.transform.position.x, component.transform.position.y, z);
            return component;
        }

        public static T SetLocalPosition<T>(this T component, Vector3 position)
            where T : Component
        {
            component.transform.localPosition = position;
            return component;
        }

        public static T SetLocalPositionX<T>(this T component, float x)
            where T : Component
        {
            component.transform.localPosition = new Vector3(x, component.transform.localPosition.y,
                component.transform.localPosition.z);
            return component;
        }

        public static T SetLocalPositionY<T>(this T component, float y)
            where T : Component
        {
            component.transform.localPosition = new Vector3(component.transform.localPosition.x, y,
                component.transform.localPosition.z);
            return component;
        }

        public static T SetLocalPositionZ<T>(this T component, float z)
            where T : Component
        {
            component.transform.localPosition = new Vector3(component.transform.localPosition.x,
                component.transform.localPosition.y, z);
            return component;
        }

        public static T SetRotation<T>(this T component, Quaternion rotation)
            where T : Component
        {
            component.transform.rotation = rotation;
            return component;
        }

        public static T SetEulerRotation<T>(this T component, Vector3 eulerAngles)
            where T : Component
        {
            component.transform.eulerAngles = eulerAngles;
            return component;
        }

        public static T SetEulerRotationX<T>(this T component, float x)
            where T : Component
        {
            component.transform.eulerAngles = new Vector3(x, component.transform.eulerAngles.y, component.transform.eulerAngles.z);
            return component;
        }

        public static T SetEulerRotationY<T>(this T component, float y)
            where T : Component
        {
            component.transform.eulerAngles = new Vector3(component.transform.eulerAngles.x, y, component.transform.eulerAngles.z);
            return component;
        }

        public static T SetEulerRotationZ<T>(this T component, float z)
            where T : Component
        {
            component.transform.eulerAngles = new Vector3(component.transform.eulerAngles.x, component.transform.eulerAngles.y, z);
            return component;
        }

        public static T SetLocalRotation<T>(this T component, Quaternion rotation)
            where T : Component
        {
            component.transform.localRotation = rotation;
            return component;
        }

        public static T SetLocalEulerRotation<T>(this T component, Vector3 eulerAngles)
            where T : Component
        {
            component.transform.localEulerAngles = eulerAngles;
            return component;
        }

        public static T SetLocalEulerRotationX<T>(this T component, float x)
            where T : Component
        {
            component.transform.localEulerAngles = new Vector3(x, component.transform.localEulerAngles.y, component.transform.localEulerAngles.z);
            return component;
        }

        public static T SetLocalEulerRotationY<T>(this T component, float y)
            where T : Component
        {
            component.transform.localEulerAngles = new Vector3(component.transform.localEulerAngles.x, y, component.transform.localEulerAngles.z);
            return component;
        }

        public static T SetLocalEulerRotationZ<T>(this T component, float z)
            where T : Component
        {
            component.transform.localEulerAngles = new Vector3(component.transform.localEulerAngles.x, component.transform.localEulerAngles.y, z);
            return component;
        }

        public static T SetScale<T>(this T component, Vector3 scale)
            where T : Component
        {
            component.transform.localScale = scale;
            return component;
        }

        public static T Scale<T>(this T component, Vector3 scale)
            where T : Component
        {
            component.transform.localScale += scale;
            return component;
        }

        public static Transform SetParent(this Transform transform, Transform parent)
        {
            transform.parent = parent;
            return transform;
        }

        public static T SetParent<T, U>(this T component, U parent)
            where T : Component
            where U : Component
        {
            component.transform.SetParent(parent.transform);
            return component;
        }

        public static void GetPosition<T>(this T component, out Vector3 position)
            where T : Component
        {
            position = component.transform.position;
        }

        public static void GetPosition<T>(this T component, out float x, out float y, out float z)
            where T : Component
        {
            var pos = component.transform.position;
            x = pos.x;
            y = pos.y;
            z = pos.z;
        }

        public static void GetLocalPosition<T>(this T component, out Vector3 position)
            where T : Component
        {
            position = component.transform.localPosition;
        }

        public static void GetLocalPosition<T>(this T component, out float x, out float y, out float z)
            where T : Component
        {
            var pos = component.transform.localPosition;
            x = pos.x;
            y = pos.y;
            z = pos.z;
        }

        public static void GetPositionX<T>(this T component, out float x)
            where T : Component
        {
            x = component.transform.position.x;
        }

        public static void GetPositionY<T>(this T component, out float y)
            where T : Component
        {
            y = component.transform.position.y;
        }

        public static void GetPositionZ<T>(this T component, out float z)
            where T : Component
        {
            z = component.transform.position.z;
        }

        public static void GetLocalPositionX<T>(this T component, out float x)
            where T : Component
        {
            x = component.transform.localPosition.x;
        }

        public static void GetLocalPositionY<T>(this T component, out float y)
            where T : Component
        {
            y = component.transform.localPosition.y;
        }

        public static void GetLocalPositionZ<T>(this T component, out float z)
            where T : Component
        {
            z = component.transform.localPosition.z;
        }

        public static void DisableChildren(this GameObject gameObject)
        {
            foreach (Transform child in gameObject.transform)
            {
                child.gameObject.SetActive(false);
            }
        }

        public static void EnableChildren(this GameObject gameObject)
        {
            foreach (Transform child in gameObject.transform)
            {
                child.gameObject.SetActive(true);
            }
        }
    }
}