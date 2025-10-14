using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshCollider))]
public class GravityMeshBox : GravityArea
{
    public enum FaceDirection { Up, Down, Left, Right, Forward, Back }

    [Header("Face that acts as 'gravity down'")]
    public FaceDirection gravityFace = FaceDirection.Down;

    [Header("Hysteresis / Delay")]
    [Tooltip("Time in seconds before gravity change is applied after enter/exit.")]
    public float gravityChangeDelay = 0.5f;

    [Header("Debug")]
    public bool showDebug = true;
    public Color meshColor = new Color(0.2f, 0.4f, 0.8f, 0.15f);
    public Color faceColor = new Color(1f, 0.3f, 0.3f, 0.45f);

    private MeshCollider _col;
    private readonly Dictionary<GravityBody, float> _enterTimes = new();
    private readonly Dictionary<GravityBody, float> _exitTimes  = new();

    private void Awake()
    {
        _col = GetComponent<MeshCollider>();
        if (_col)
        {
            _col.isTrigger = true;
            // leave non-convex for large volumes
        }
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

    // Fully override to avoid base auto add/remove
    protected override void OnTriggerEnter(Collider other)
    {
        var body = other.GetComponentInParent<GravityBody>();
        if (!body) return;

        if (_exitTimes.ContainsKey(body)) _exitTimes.Remove(body);
        _enterTimes[body] = Time.time;
    }

    protected override void OnTriggerExit(Collider other)
    {
        var body = other.GetComponentInParent<GravityBody>();
        if (!body) return;

        if (_enterTimes.ContainsKey(body)) _enterTimes.Remove(body);
        _exitTimes[body] = Time.time;
    }

    private void ApplyEnter(GravityBody body)
    {
        if (!body) return;
        body.AddGravityArea(this);
        body.ForceAlignWithGravity(true);
        body.MarkGravityTransition(0.20f);
    }

    private void ApplyExit(GravityBody body)
    {
        if (!body) return;
        body.RemoveGravityArea(this);
        body.ForceAlignWithGravity(true);
        body.MarkGravityTransition(0.20f);
    }

    private void OnDrawGizmos()
    {
        if (!showDebug) return;
        var mc = GetComponent<MeshCollider>();
        if (!mc || !mc.sharedMesh) return;

        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);

        // mesh wireframe
        Gizmos.color = meshColor;
        var verts = mc.sharedMesh.vertices;
        var tris = mc.sharedMesh.triangles;
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = verts[tris[i]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, a);
        }

        // small face plate
        Gizmos.color = faceColor;
        Bounds bnds = mc.sharedMesh.bounds;
        Vector3 faceCenter = bnds.center;
        Vector3 faceSize = bnds.size;
        const float faceDepth = 0.01f;

        switch (gravityFace)
        {
            case FaceDirection.Up:
                faceCenter += Vector3.up * (bnds.size.y * 0.5f);
                faceSize = new Vector3(bnds.size.x, faceDepth, bnds.size.z);
                break;
            case FaceDirection.Down:
                faceCenter += Vector3.down * (bnds.size.y * 0.5f);
                faceSize = new Vector3(bnds.size.x, faceDepth, bnds.size.z);
                break;
            case FaceDirection.Left:
                faceCenter += Vector3.left * (bnds.size.x * 0.5f);
                faceSize = new Vector3(faceDepth, bnds.size.y, bnds.size.z);
                break;
            case FaceDirection.Right:
                faceCenter += Vector3.right * (bnds.size.x * 0.5f);
                faceSize = new Vector3(faceDepth, bnds.size.y, bnds.size.z);
                break;
            case FaceDirection.Forward:
                faceCenter += Vector3.forward * (bnds.size.z * 0.5f);
                faceSize = new Vector3(bnds.size.x, bnds.size.y, faceDepth);
                break;
            case FaceDirection.Back:
                faceCenter += Vector3.back * (bnds.size.z * 0.5f);
                faceSize = new Vector3(bnds.size.x, bnds.size.y, faceDepth);
                break;
        }

        Gizmos.DrawCube(faceCenter, faceSize);
        Gizmos.matrix = old;
    }
}