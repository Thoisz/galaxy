using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class PickupEquipment : MonoBehaviour
{
    [Header("Presentation")]
    public bool spinning = true;
    public AnimationClip animationClip;   // optional; if null -> no animation

    // Config
    const string PlayerTag = "Player";
    const float  SpinSpeed = 200f;

    // Playables (no Animator Controller required)
    PlayableGraph _graph;
    AnimationClipPlayable _clipPlayable;
    bool _graphValid;

    void Reset()
    {
        // Single trigger collider on this object
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;

        // Ensure trigger events fire even if the player uses CharacterController
        var rb = GetComponent<Rigidbody>();
        if (!rb)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }
    }

    void OnEnable()
    {
        TryStartAnimation();
    }

    void OnDisable()
    {
        StopAnimation();
    }

    void Update()
    {
        if (spinning)
            transform.Rotate(Vector3.up, SpinSpeed * Time.deltaTime, Space.World);

        // Manual soft-loop if the clip doesn’t have Loop Time ticked
        if (_graphValid && animationClip != null)
        {
            double len = animationClip.length;
            if (len > 0.0001)
            {
                double t = PlayableExtensions.GetTime(_clipPlayable);
                if (t >= len)
                {
                    PlayableExtensions.SetTime(_clipPlayable, t % len);
                    PlayableExtensions.Play(_clipPlayable);
                }
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other || !other.CompareTag(PlayerTag)) return;

        // Inventory key = this GameObject’s name (must match Equipment “Inspector Name”)
        string key = gameObject.name;

        var pe = PlayerEquipment.Instance;
        if (pe == null)
        {
            Debug.LogWarning("[PickupEquipment] No PlayerEquipment in scene.");
            return;
        }

        // Already owned? Leave it in the world.
        if (pe.HasInInventory(key)) return;

        // Add to inventory and disappear
        if (pe.TryAdd(key))
        {
            Destroy(gameObject);
        }
        else
        {
            Debug.LogWarning($"[PickupEquipment] Failed to add '{key}' to inventory.");
        }
    }

    void TryStartAnimation()
    {
        if (animationClip == null || _graphValid) return;

        // Need an Animator for the Playable output (no controller required)
        var animator = GetComponent<Animator>();
        if (animator == null) animator = gameObject.AddComponent<Animator>();
        animator.applyRootMotion = false; // ignore root motion for pickups

        _graph = PlayableGraph.Create($"{name}_PickupGraph");
        var output = AnimationPlayableOutput.Create(_graph, "Animation", animator);

        _clipPlayable = AnimationClipPlayable.Create(_graph, animationClip);
        _clipPlayable.SetApplyFootIK(false);

        output.SetSourcePlayable(_clipPlayable);
        _graph.Play();
        _graphValid = true;
    }

    void StopAnimation()
    {
        if (!_graphValid) return;
        _graph.Stop();
        _graph.Destroy();
        _graphValid = false;
    }
}