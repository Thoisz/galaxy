using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DamageTick : MonoBehaviour
{
    [Header("Damage Settings")]
    public float damageAmount = 20f;            // flat damage per tick
    public float damageInterval = 0.5f;         // seconds between ticks
    
    [Tooltip("If true, apply damage as % of player's max health instead of flat.")]
    public bool usePercentageDamage = false;
    [Range(0f, 1f)]
    public float percentagePerTick = 0.2f;      // e.g. 0.2 = 20% of maxHealth per tick
    
    [Header("Effects")]
    public Color gizmoColor = Color.red;
    public bool showGizmo = true;
    
    [Header("Audio")]
    public AudioClip damageSound;
    public float soundVolume = 0.5f;

    // Private
    private HashSet<GameObject> objectsInDamageZone = new HashSet<GameObject>();
    private Dictionary<GameObject, Coroutine> damageCoroutines = new Dictionary<GameObject, Coroutine>();
    private AudioSource audioSource;

    void Start()
    {
        if (damageSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = damageSound;
            audioSource.volume = soundVolume;
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1.0f;
        }

        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
        else Debug.LogWarning("DamageTick: No collider found! Please add a trigger collider.");
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth != null && !objectsInDamageZone.Contains(other.gameObject))
        {
            objectsInDamageZone.Add(other.gameObject);
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
            if (damageCoroutines.ContainsKey(other.gameObject))
            {
                if (damageCoroutines[other.gameObject] != null)
                    StopCoroutine(damageCoroutines[other.gameObject]);
                damageCoroutines.Remove(other.gameObject);
            }
            Debug.Log($"Player left damage zone: {gameObject.name}");
        }
    }

    private IEnumerator DealDamageOverTime(PlayerHealth targetHealth)
    {
        while (true)
        {
            if (targetHealth != null && !targetHealth.IsDead())
            {
                float damageToApply = damageAmount;

                if (usePercentageDamage)
                {
                    damageToApply = targetHealth.GetMaxHealth() * percentagePerTick;
                }

                targetHealth.TakeTickDamage(damageToApply);

                if (audioSource != null && damageSound != null)
                    audioSource.Play();
            }

            yield return new WaitForSeconds(damageInterval);
        }
    }

    void OnDestroy()
    {
        foreach (var coroutine in damageCoroutines.Values)
        {
            if (coroutine != null) StopCoroutine(coroutine);
        }
        damageCoroutines.Clear();
        objectsInDamageZone.Clear();
    }

    void OnDrawGizmos()
    {
        if (showGizmo)
        {
            Gizmos.color = gizmoColor;
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                if (col is BoxCollider boxCol)
                {
                    Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                    Gizmos.DrawWireCube(boxCol.center, boxCol.size);
                }
                else if (col is SphereCollider sphereCol)
                {
                    Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                    Gizmos.DrawWireSphere(sphereCol.center, sphereCol.radius);
                }
                else if (col is CapsuleCollider capsuleCol)
                {
                    Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                    Gizmos.DrawWireSphere(capsuleCol.center, capsuleCol.radius);
                }
            }
        }
    }
}
