using System;
using UnityEngine;

namespace Overlord.Explorer
{
    public static class UIPrefab
    {
        public const string Folder = "UI/";

        public static GameObject Load(string name)
        {
            GameObject prefab = Resources.Load<GameObject>(Folder + name);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "the layout prefab Resources/" + Folder + name +
                    " is missing, so this screen cannot be built");
            }
            return prefab;
        }

        public static GameObject Instantiate(string name, Transform parent)
        {
            GameObject copy = UnityEngine.Object.Instantiate(Load(name), parent, false);
            copy.name = name;
            return copy;
        }

        public static T Bind<T>(GameObject root, string path) where T : Component
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            Transform found = root.transform.Find(path);
            if (found == null)
            {
                throw new InvalidOperationException(
                    root.name + " has no child at \"" + path + "\", so its " + typeof(T).Name +
                    " cannot be bound. The prefab and the code disagree: either the object was" +
                    " renamed or moved in the editor, or the path here is wrong.");
            }

            T component = found.GetComponent<T>();
            if (component == null)
            {
                throw new InvalidOperationException(
                    root.name + "/" + path + " exists but carries no " + typeof(T).Name +
                    ". The prefab and the code disagree: the component was removed in the editor.");
            }

            return component;
        }

        public static GameObject BindObject(GameObject root, string path)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            Transform found = root.transform.Find(path);
            if (found == null)
            {
                throw new InvalidOperationException(
                    root.name + " has no child at \"" + path + "\". The prefab and the code disagree.");
            }

            return found.gameObject;
        }

        public static T Require<T>(GameObject target) where T : Component
        {
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            T component = target.GetComponent<T>();
            if (component == null)
            {
                throw new InvalidOperationException(
                    target.name + " carries no " + typeof(T).Name +
                    ". The prefab and the code disagree: the component was removed in the editor.");
            }

            return component;
        }
    }
}
