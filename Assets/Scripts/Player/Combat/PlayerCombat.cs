using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    public List<AttackSO> combo; // List of attack animations
    private float lastClickedTime;
    private float lastComboEnd;
    private int comboCounter;

    private Animator anim;
    [SerializeField] Weapon weapon; // Link to the weapon

    [SerializeField] private float comboBufferTime = 0.2f; // Buffer time for responsive combos

    void Start()
    {
        anim = GetComponent<Animator>();
    }

    void Update()
    {
        // Check if the left mouse button is pressed
        bool isFireButtonPressed = Input.GetButton("Fire1");
        
        if (isFireButtonPressed)
        {
            // Set isAttacking to true while the button is held down
            anim.SetBool("IsAttacking", true);
            Attack();
        }
        else
        {
            // Set isAttacking to false when the button is released
            anim.SetBool("IsAttacking", false);
            ExitAttack();
        }
    }

    void Attack()
    {
        if (Time.time - lastComboEnd > 0f && comboCounter < combo.Count) // Make sure to check bounds
        {
            CancelInvoke("EndCombo");

            // Check if enough time has passed since the last attack
            if (Time.time - lastClickedTime >= 0.6f)
            {
                // Play the combo attack
                anim.runtimeAnimatorController = combo[comboCounter].animatorOV;
                anim.Play("Attack", 0, 0);
                weapon.damage = combo[comboCounter].damage; // Assign damage to the weapon
                
                lastClickedTime = Time.time; // Update the last clicked time
                comboCounter++; // Move to the next attack in the combo

                // Reset comboCounter if we've reached the end of the combo
                if (comboCounter >= combo.Count) 
                {
                    comboCounter = 0; // Go back to Attack1 after the last attack
                }
            }
        }
    }

    void ExitAttack()
    {
        // Check if the animation has finished playing
        if (anim.GetCurrentAnimatorStateInfo(0).normalizedTime > 0.9f && anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack"))
        {
            Invoke("EndCombo", 1);
        }
    }

    void EndCombo()
    {
        comboCounter = 0; // Reset the combo counter
        lastComboEnd = Time.time; // Update last combo end time
    }
}