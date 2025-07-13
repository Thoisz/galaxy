using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DamageTick : MonoBehaviour
{
    [Header("Damage Settings")]
    public float damageAmount = 20f;
    public float damageInterval = 0.5f; // Time between damage ticks
    
    [Header("Effects")]
    public Color gizmoColor = Color.red;
    public bool showGizmo = true;
    
    [Header("Audio")]
    public AudioClip damageSound;
    public float soundVolume = 0.5f;
    
    // Private variables
    private HashSet<GameObject> objectsInDamageZone = new HashSet<GameObject>();
    private Dictionary<GameObject, Coroutine> damageCoroutines = new Dictionary<GameObject, Coroutine>();
    private AudioSource audioSource;
    
    void Start()
    {
        // Create audio source for damage sounds
        if (damageSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = damageSound;
            audioSource.volume = soundVolume;
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1.0f; // 3D sound
        }
        
        // Ensure we have a collider set as trigger
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
        else
        {
            Debug.LogWarning("DamageTick: No collider found! Please add a collider and set it as trigger.");
        }
    }
    
    void OnTriggerEnter(Collider other)
    {
        // Check if the object has a PlayerHealth component
        PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth != null && !objectsInDamageZone.Contains(other.gameObject))
        {
            objectsInDamageZone.Add(other.gameObject);
            
            // Start damage coroutine for this object
            Coroutine damageCoroutine = StartCoroutine(DealDamageOverTime(playerHealth));
            damageCoroutines[other.gameObject] = damageCoroutine;
            
            Debug.Log($"Player entered damage zone: {gameObject.name}");
        }
    }
    
    void OnTriggerExit(Collider other)
    {
        if (objectsInDamageZone.Contains(other.gameObject))
        {
            objectsInDamageZone.Remove(other.gameObject);
            
            // Stop damage coroutine for this object
            if (damageCoroutines.ContainsKey(other.gameObject))
            {
                if (damageCoroutines[other.gameObject] != null)
                {
                    StopCoroutine(damageCoroutines[other.gameObject]);
                }
                damageCoroutines.Remove(other.gameObject);
            }
            
            Debug.Log($"Player left damage zone: {gameObject.name}");
        }
    }
    
    private IEnumerator DealDamageOverTime(PlayerHealth targetHealth)
    {
        while (true)
        {
            // Deal damage
            if (targetHealth != null && !targetHealth.IsDead())
            {
                targetHealth.TakeDamage(damageAmount);
                
                // Play damage sound
                if (audioSource != null && damageSound != null)
                {
                    audioSource.Play();
                }
            }
            
            // Wait for the next damage tick
            yield return new WaitForSeconds(damageInterval);
        }
    }
    
    // Clean up if object is destroyed while someone is in the damage zone
    void OnDestroy()
    {
        foreach (var coroutine in damageCoroutines.Values)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        damageCoroutines.Clear();
        objectsInDamageZone.Clear();
    }
    
    // Draw gizmo to visualize damage zone in editor
    void OnDrawGizmos()
    {
        if (showGizmo)
        {
            Gizmos.color = gizmoColor;
            
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                // Draw based on collider type
                if (col is BoxCollider)
                {
                    BoxCollider boxCol = (BoxCollider)col;
                    Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                    Gizmos.DrawWireCube(boxCol.center, boxCol.size);
                }
                else if (col is SphereCollider)
                {
                    SphereCollider sphereCol = (SphereCollider)col;
                    Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                    Gizmos.DrawWireSphere(sphereCol.center, sphereCol.radius);
                }
                else if (col is CapsuleCollider)
                {
                    CapsuleCollider capsuleCol = (CapsuleCollider)col;
                    Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                    // Unity doesn't have a built-in wire capsule, so draw a sphere for now
                    Gizmos.DrawWireSphere(capsuleCol.center, capsuleCol.radius);
                }
            }
        }
    }
}