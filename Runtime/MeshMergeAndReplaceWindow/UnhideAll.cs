using UnityEngine;

public class UnhideAll : MonoBehaviour
{
    void Start()
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            go.hideFlags = HideFlags.None;
        }
        Debug.Log("HideFlags cleared");
    }
}

