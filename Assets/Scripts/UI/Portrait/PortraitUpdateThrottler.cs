using UnityEngine;
[RequireComponent(typeof(Camera))]
public class PortraitUpdateThrottler : MonoBehaviour {
  public int targetFps = 24; float t;
  Camera cam; void Awake(){ cam = GetComponent<Camera>(); cam.enabled = false; }
  void LateUpdate(){ t += Time.unscaledDeltaTime; if(t >= 1f/targetFps){ t=0; cam.Render(); } }
}
