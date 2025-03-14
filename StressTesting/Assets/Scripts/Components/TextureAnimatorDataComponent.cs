using Unity.Entities;
using UnityEngine;

[AddComponentMenu("ECS/Components/TextureAnimator")]
public class TextureAnimatorDataComponent : MonoBehaviour
{
    public TextureAnimatorData Value;
}

public class TextureAnimatorDataBaker : Baker<TextureAnimatorDataComponent>
{
    public override void Bake(TextureAnimatorDataComponent authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
