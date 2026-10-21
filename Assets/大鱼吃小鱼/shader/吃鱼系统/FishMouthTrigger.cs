using System.Collections;
using UnityEngine;

public class FishMouthTrigger : MonoBehaviour
{
    [Header("关联组件")]
    public FishEntity selfEntity;
    public FishFeedback feedback;
    public Transform mouthAnchor; // 鱼嘴的中心点，用于吸入小鱼

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 检测是否碰到鱼的身体
        if (other.CompareTag("FishBody"))
        {
            // 避开尖括号，使用 typeof 安全获取 FishEntity 组件
            FishEntity prey = other.GetComponentInParent(typeof(FishEntity)) as FishEntity;
            
            // 确保不是自己，且能吃掉对方
            if (prey != null && prey != selfEntity && selfEntity.CanEat(prey))
            {
                // 1. 防止二次触发
                other.enabled = false;

                // 2. 增加自身体重
                float preyWeight = prey.CurrentWeight;
                selfEntity.AddWeight(preyWeight);

                // 3. 触发打击反馈 (大目标触发微停顿卡肉感)
                bool isBigTarget = preyWeight >= selfEntity.CurrentWeight * 0.5f;
                if (feedback != null) 
                {
                    // 【修改点】调用 PlayEatFeedback 时，把小鱼的位置传进去
                    // 这样特效就会在被吃掉的小鱼的位置爆炸
                    feedback.PlayEatFeedback(isBigTarget, prey.transform.position);
                }

                // 4. 播放吸入动画并销毁小鱼
                StartCoroutine(SuckAndConsume(prey));
            }
        }
    }

    private IEnumerator SuckAndConsume(FishEntity prey)
    {
        prey.OnBeEaten(); // 通知小鱼停止活动

        Transform preyTransform = prey.visualRoot; // 仅缩放视觉层
        Vector3 startPos = prey.transform.position;
        Vector3 startScale = preyTransform.localScale;
        
        float duration = 0.08f; // 吸入时间，极快
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime; 
            float t = elapsed / duration;
            
            // 抛物线吸入，制造“嗖”的一下的感觉
            prey.transform.position = Vector3.Lerp(startPos, mouthAnchor.position, t * t);
            preyTransform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            
            yield return null;
        }

        prey.Recycle();
    }
}