using UnityEngine;

public class HealthBar : MonoBehaviour
{
    public RectTransform fill;

    public float maxWidth = 100f;

    public void SetHealth(float currentHealth, float maxHealth)
    {
        float healthPercent = currentHealth / maxHealth;

        Vector2 size = fill.sizeDelta;
        size.x = maxWidth * healthPercent;
        fill.sizeDelta = size;
    }
}