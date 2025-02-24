using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Scrolling : MonoBehaviour
{
    public float ScrollX = 0.5f;
    public float ScrollY = 0.5f;

    void Update () {
        float OffsetX = Time.time * ScrollY;
        float OffsetY = Time.time * ScrollY;
        GetComponent<Renderer> ().material.mainTextureOffset = new Vector2 (OffsetX, OffsetY);
    }
}
