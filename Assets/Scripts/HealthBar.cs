using UnityEngine;

public class HealthBar : MonoBehaviour
{
    public RectTransform fill;
    public float maxWidth = 400f;

    public void SetHealth(float currentHealth, float maxHealth)
    {
        if (fill == null || maxHealth <= 0f) return;

        float healthPercent = Mathf.Clamp01(currentHealth / maxHealth);

        Vector2 size = fill.sizeDelta;
        size.x = maxWidth * healthPercent;
        fill.sizeDelta = size;
    }
}