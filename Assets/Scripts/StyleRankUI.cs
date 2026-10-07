using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public class StyleRankUI : MonoBehaviour
{
    [Header("Rank sprites")]
    public Sprite dSprite;
    public Sprite cSprite;
    public Sprite bSprite;
    public Sprite aSprite;
    public Sprite sSprite;
    public Sprite ssSprite;
    public Sprite sssSprite;

    [Header("Style points")]
    [Min(0.01f)] public float pointsPerDamage = 2f;
    [Min(0f)] public float killBonus = 20f;
    [Tooltip("Each rank requires this many more points. D appears above zero.")]
    [Min(1f)] public float pointsPerRank = 50f;

    [Header("Decay")]
    [Min(0f)] public float drainDelay = 1f;
    [Min(0f)] public float drainPerSecond = 15f;
    [Min(0f)] public float disappearDuration = 0.2f;

    [Header("Rank-up pop")]
    [Min(0.01f)] public float popDuration = 0.2f;
    [Range(0.1f, 1f)] public float popStartScale = 0.6f;
    [Range(1f, 1.5f)] public float popPeakScale = 1.15f;

    [Header("Gentle pulse")]
    [Range(0f, 0.15f)] public float pulseAmount = 0.03f;
    [Min(0f)] public float pulseBeatsPerSecond = 1.5f;

    public float Points { get; private set; }
    // -1 means hidden; 0 through 6 mean D through SSS.
    public int CurrentRank { get; private set; } = -1;

    Image rankImage;
    RectTransform rankRect;
    Vector3 baseScale;
    Color baseColor;
    float lastHitTime = float.NegativeInfinity;
    float popElapsed;
    float pulseElapsed;
    float fadeElapsed;
    bool popping;
    bool fading;

    void Awake()
    {
        rankImage = GetComponent<Image>();
        rankRect = rankImage.rectTransform;
        baseScale = rankRect.localScale;
        baseColor = rankImage.color;
        rankImage.preserveAspect = true;
        rankImage.raycastTarget = false;
        rankImage.enabled = false;
    }

    void OnEnable() { EnemyHealth.CombatDamageDealt += OnCombatDamage; }

    void OnDisable()
    {
        EnemyHealth.CombatDamageDealt -= OnCombatDamage;
        ResetStyle();
    }

    void OnCombatDamage(float damage, bool killed)
    {
        float gain = damage * Mathf.Max(0.01f, pointsPerDamage);
        if (killed) gain += Mathf.Max(0f, killBonus);
        Points = Mathf.Min(Points + gain, Mathf.Max(1f, pointsPerRank) * 7f);
        lastHitTime = Time.time;
        RefreshRank();
    }

    void Update()
    {
        // Count only the portion of this frame after the inactivity delay.
        float drainStart = lastHitTime + Mathf.Max(0f, drainDelay);
        float drainTime = Mathf.Clamp(Time.time - drainStart, 0f, Time.deltaTime);
        if (Points > 0f && drainTime > 0f)
        {
            Points = Mathf.Max(0f, Points - Mathf.Max(0f, drainPerSecond) * drainTime);
            RefreshRank();
        }
        AnimateRank(Time.deltaTime);
    }

    void RefreshRank()
    {
        int next = Points <= 0f ? -1
            : Mathf.Clamp(Mathf.FloorToInt(Points / Mathf.Max(1f, pointsPerRank)), 0, 6);
        if (next == CurrentRank) return;
        int previous = CurrentRank;
        CurrentRank = next;
        if (next < 0)
        {
            fading = true;
            fadeElapsed = 0f;
            popping = false;
            return;
        }

        fading = false;
        rankImage.color = baseColor;
        rankImage.sprite = SpriteForRank(next);
        rankImage.enabled = rankImage.sprite != null;
        popping = next > previous;
        popElapsed = 0f;
        pulseElapsed = 0f;
        rankRect.localScale = baseScale * (popping ? popStartScale : 1f);
    }

    Sprite SpriteForRank(int rank)
    {
        switch (rank)
        {
            case 0: return dSprite;
            case 1: return cSprite;
            case 2: return bSprite;
            case 3: return aSprite;
            case 4: return sSprite;
            case 5: return ssSprite;
            default: return sssSprite;
        }
    }

    void AnimateRank(float dt)
    {
        if (fading)
        {
            fadeElapsed += dt;
            float t = disappearDuration <= 0f ? 1f : Mathf.Clamp01(fadeElapsed / disappearDuration);
            Color color = baseColor;
            color.a *= 1f - t;
            rankImage.color = color;
            if (t >= 1f)
            {
                rankImage.enabled = false;
                fading = false;
                rankRect.localScale = baseScale;
            }
            return;
        }
        if (CurrentRank < 0) return;
        if (popping)
        {
            popElapsed += dt;
            float t = Mathf.Clamp01(popElapsed / Mathf.Max(0.01f, popDuration));
            float scale = t < 0.6f
                ? Mathf.Lerp(popStartScale, popPeakScale, Mathf.SmoothStep(0f, 1f, t / 0.6f))
                : Mathf.Lerp(popPeakScale, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.4f));
            rankRect.localScale = baseScale * scale;
            if (t >= 1f) popping = false;
            return;
        }
        pulseElapsed += dt;
        float pulse = (1f - Mathf.Cos(pulseElapsed * Mathf.PI * 2f * pulseBeatsPerSecond)) * 0.5f;
        rankRect.localScale = baseScale * (1f + pulse * pulseAmount);
    }

    public void ResetStyle()
    {
        Points = 0f;
        CurrentRank = -1;
        lastHitTime = float.NegativeInfinity;
        popping = fading = false;
        if (rankImage != null)
        {
            rankImage.enabled = false;
            rankImage.color = baseColor;
            rankRect.localScale = baseScale;
        }
    }
}
