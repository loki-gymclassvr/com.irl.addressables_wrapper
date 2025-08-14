using System;
using AddressableSystem;
using UnityEngine;

public class AddressableRuntimeTest : MonoBehaviour
{
    [SerializeField]
    private PrefabReference prefabReference;
    
    [SerializeField]
    private MaterialReference materialReference;
    
    public void LoadAsset()
    {
        Action<Material> OnComplete = HandleComplete;
        materialReference.LoadAsync(OnComplete);
    }

    private void HandleComplete(Material obj)
    {
        Debug.Log(obj.name);
    }

    public void Unload()
    {
        materialReference.Unload();
    }
}
