using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(BoxCollider))]
public class GravityBox : GravityArea
{
    public enum FaceDirection { Up, Down, Left, Right, Forward, Back }

    [Header("Face that acts as 'gravity down'")]
    public FaceDirection gravityFace = FaceDirection.Down;

    [Header("Hysteresis / Delay")]
    [Tooltip("Time in seconds before gravity change is applied after enter/exit.")]
    public float gravityChangeDelay = 0.5f;

    [Header("Debug")]
    public bool showDebug = true;
    public Color boxColor = new Color(0.2f, 0.4f, 0.8f, 0.2f);
    public Color faceColor = new Color(1f, 0.3f, 0.3f, 0.5f);

    private BoxCollider _col;
    private readonly Dictionary<GravityBody, float> _enterTimes = new();
    private readonly Dictionary<GravityBody, float> _exitTimes  = new();

    protected override void Awake()
    {
        base.Awake();
        _col = GetComponent<BoxCollider>();
        if (_col) _col.isTrigger = true;
    }

    private void Update()
    {
        // process delayed enters
        var toApply = new List<GravityBody>();
        foreach (var kv in _enterTimes)
            if (Time.time - kv.Value >= gravityChangeDelay) toApply.Add(kv.Key);

        foreach (var body in toApply)
        {
            ApplyEnter(body);
            _enterTimes.Remove(body);
        }

        // process delayed exits
        toApply.Clear();
        foreach (var kv in _exitTimes)
            if (Time.time - kv.Value >= gravityChangeDelay) toApply.Add(kv.Key);

        foreach (var body in toApply)
        {
            ApplyExit(body);
            _exitTimes.Remove(body);
        }
    }

    public override Vector3 GetGravityDirection(GravityBody body)
    {
        switch (gravityFace)
        {
            case FaceDirection.Up:      return transform.up;
            case FaceDirection.Down:    return -transform.up;
            case FaceDirection.Left:    return -transform.right;
            case FaceDirection.Right:   return transform.right;
            case FaceDirection.Forward: return transform.forward;
            case FaceDirection.Back:    return -transform.forward;
        }
        return -transform.up;
    }

    protected override void OnTriggerEnter(Collider other)
    {
        var body = other.GetComponentInParent<GravityBody>();
        if (!body) return;

        // entering cancels pending exit
        if (_exitTimes.ContainsKey(body)) _exitTimes.Remove(body);
        _enterTimes[body] = Time.time;
    }

    protected override void OnTriggerExit(Collider other)
    {
        var body = other.GetComponentInParent<GravityBody>();
        if (!body) return;

        // leaving cancels pending enter
        if (_enterTimes.ContainsKey(body)) _enterTimes.Remove(body);
        _exitTimes[body] = Time.time;
    }

    private void ApplyEnter(GravityBody body)
    {
        if (!body) return;
        body.AddGravityArea(this);
        body.ForceAlignWithGravity(true); // snap to avoid wobble on entry
    }

    private void ApplyExit(GravityBody body)
    {
        if (!body) return;
        body.RemoveGravityArea(this);
        body.ForceAlignWithGravity(true); // snap to new effective area/down
    }

    private void OnDrawGizmos()
    {
        if (!showDebug) return;
        var c = GetComponent<BoxCollider>();
        if (!c) return;

        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);

        Gizmos.color = boxColor;
        Gizmos.DrawCube(c.center, c.size);

        // draw the selected face
        Gizmos.color = faceColor;
        Vector3 faceCenter = c.center;
        Vector3 faceSize = c.size;
        const float faceDepth = 0.01f;

        switch (gravityFace)
        {
            case FaceDirection.Up:
                faceCenter += Vector3.up * (c.size.y * 0.5f);
                faceSize = new Vector3(c.size.x, faceDepth, c.size.z);
                break;
            case FaceDirection.Down:
                faceCenter += Vector3.down * (c.size.y * 0.5f);
                faceSize = new Vector3(c.size.x, faceDepth, c.size.z);
                break;
            case FaceDirection.Left:
                faceCenter += Vector3.left * (c.size.x * 0.5f);
                faceSize = new Vector3(faceDepth, c.size.y, c.size.z);
                break;
            case FaceDirection.Right:
                faceCenter += Vector3.right * (c.size.x * 0.5f);
                faceSize = new Vector3(faceDepth, c.size.y, c.size.z);
                break;
            case FaceDirection.Forward:
                faceCenter += Vector3.forward * (c.size.z * 0.5f);
                faceSize = new Vector3(c.size.x, c.size.y, faceDepth);
                break;
            case FaceDirection.Back:
                faceCenter += Vector3.back * (c.size.z * 0.5f);
                faceSize = new Vector3(c.size.x, c.size.y, faceDepth);
                break;
        }

        Gizmos.DrawCube(faceCenter, faceSize);
        Gizmos.matrix = old;
    }
}