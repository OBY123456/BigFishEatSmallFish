using System.Collections;
using UnityEngine;

public class FishFeedback : MonoBehaviour
{
    [Header("弹性形变 (Squash & Stretch)")]
    public Transform visualRoot;
    public Vector3 biteSquash = new Vector3(1.15f, 0.85f, 1f); 
    public float recoverDuration = 0.18f;

    [Header("颜色闪烁 (Hit Flash)")]
    public Color flashColor = Color.white;
    [Tooltip("相加模式材质(Additive)，不填只改颜色。建议填入 Mobile/Particles/Additive 材质")]
    public Material additiveMaterial;
    public int flashCount = 2;
    public float flashInterval = 0.04f;

    [Header("音效反馈")]
    public AudioSource audioSource;
    public AudioClip eatSound;
    public float pitchStep = 0.08f;
    public float maxPitch = 1.6f;
    public float comboTimeout = 1.2f;

    [Header("特效 (支持多层粒子组合)")]
    public GameObject biteVFXRoot;

    private Vector3 _originalScale = Vector3.one;
    private Coroutine _squashCoroutine;
    private float _currentPitch = 1.0f;
    private float _lastEatTime;

    private SpriteRenderer _spriteRenderer;
    private Color _originalColor = Color.white;
    private Material _originalMaterial; 
    private Coroutine _flashCoroutine;

    private void Start()
    {
        if (visualRoot != null)
        {
            _spriteRenderer = visualRoot.GetComponentInChildren(typeof(SpriteRenderer)) as SpriteRenderer;
            if (_spriteRenderer != null)
            {
                _originalColor = _spriteRenderer.color;
                _originalMaterial = _spriteRenderer.material; 
            }
        }
    }

    private void Update()
    {
        if (Time.time - _lastEatTime > comboTimeout && _currentPitch > 1.0f)
        {
            _currentPitch = 1.0f;
        }
    }

    // 【修改点】增加了一个 Vector3 参数，用于接收特效应该出现的世界坐标
    public void PlayEatFeedback(bool hitStop, Vector3 fxPosition)
    {
        _lastEatTime = Time.time;

        // 1. 播放组合粒子特效
        if (biteVFXRoot != null)
        {
            // 【关键修改】在激活和播放前，将特效根节点移动到传入的位置
            biteVFXRoot.transform.position = fxPosition;
            
            biteVFXRoot.SetActive(true); // 确保父节点开启
            Component[] pSystems = biteVFXRoot.GetComponentsInChildren(typeof(ParticleSystem), true);
            
            if (pSystems != null && pSystems.Length > 0)
            {
                foreach (Component comp in pSystems)
                {
                    ParticleSystem ps = comp as ParticleSystem;
                    if (ps != null)
                    {
                        ps.gameObject.SetActive(true);
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        ps.Play(true);
                    }
                }
            }
            else
            {
                Debug.LogError("【特效失败】在 BiteVFXRoot 下没有找到任何粒子组件！");
            }
        }

        // 2. 音效音调爬升
        if (audioSource != null && eatSound != null)
        {
            audioSource.pitch = _currentPitch;
            audioSource.PlayOneShot(eatSound);
            _currentPitch = Mathf.Min(_currentPitch + pitchStep, maxPitch);
        }

        // 3. 弹性形变
        if (_squashCoroutine != null) StopCoroutine(_squashCoroutine);
        _squashCoroutine = StartCoroutine(DoSquashAndStretch());

        // 4. 发光闪烁
        if (_spriteRenderer != null)
        {
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(DoFlashColor());
        }

        // 5. 卡肉微停顿
        if (hitStop)
        {
            StartCoroutine(DoHitStop(0.04f));
        }
    }

    private IEnumerator DoFlashColor()
    {
        for (int i = 0; i < flashCount; i++)
        {
            _spriteRenderer.color = flashColor; 
            if (additiveMaterial != null) _spriteRenderer.material = additiveMaterial; 
            
            yield return new WaitForSecondsRealtime(flashInterval); 
            
            _spriteRenderer.color = _originalColor;
            if (additiveMaterial != null) _spriteRenderer.material = _originalMaterial;
            
            yield return new WaitForSecondsRealtime(flashInterval);
        }
        
        _spriteRenderer.color = _originalColor;
        if (additiveMaterial != null) _spriteRenderer.material = _originalMaterial;
    }

    private IEnumerator DoSquashAndStretch()
    {
        visualRoot.localScale = Vector3.Scale(_originalScale, biteSquash);
        float timer = 0f;

        while (timer < recoverDuration)
        {
            timer += Time.unscaledDeltaTime; 
            float t = timer / recoverDuration;
            float bounce = Mathf.Sin(t * Mathf.PI) * 0.1f;
            visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, _originalScale, t) + new Vector3(-bounce, bounce, 0);
            yield return null;
        }
        visualRoot.localScale = _originalScale;
    }

    private IEnumerator DoHitStop(float duration)
    {
        Time.timeScale = 0.05f; 
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1.0f;  
    }
}