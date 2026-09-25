using System;
using UnityEngine;

/// <summary>
/// Lives on the planet Environment object. Holds Grass / Rocks / Trees prefab
/// palettes that the editor brush paints onto the sphere. Painted instances
/// are parented under a child folder named after the category.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("BackHome/Planet Environment Brush")]
public class PlanetEnvironmentBrush : MonoBehaviour
{
    [Serializable]
    public class Category
    {
        public string displayName = "Grass";
        public Color previewColor = new Color(0.45f, 0.78f, 0.32f, 1f);
        [Tooltip("Scatter radius of one brush dab, in world meters along the surface.")]
        [Min(0f)] public float brushRadius = 4f;
        [Tooltip("Minimum distance between two painted instances of this category.")]
        [Min(0.05f)] public float spacing = 1.2f;
        [Tooltip("How many instances one dab tries to drop inside the brush.")]
        [Range(1, 12)] public int amount = 3;
        [Min(0.05f)] public float scaleMin = 0.85f;
        [Min(0.05f)] public float scaleMax = 1.15f;
        public GameObject[] prefabs = Array.Empty<GameObject>();
    }

    [SerializeField] Category[] categories = Array.Empty<Category>();
    [SerializeField] float hover = PlanetSurfacePose.DefaultHover;
    [SerializeField] bool randomYaw = true;
    [SerializeField] float yaw;

    public int CategoryCount => categories != null ? categories.Length : 0;
    public float Hover => Mathf.Max(0f, hover);
    public bool RandomYaw => randomYaw;
    public float Yaw => yaw;

    public Category GetCategory(int index)
    {
        if (categories == null || index < 0 || index >= categories.Length)
            return null;
        return categories[index];
    }

    public GameObject GetPrefab(int categoryIndex, int prefabIndex)
    {
        Category category = GetCategory(categoryIndex);
        if (category?.prefabs == null || prefabIndex < 0 || prefabIndex >= category.prefabs.Length)
            return null;
        return category.prefabs[prefabIndex];
    }

    /// <summary>Uniform pick among prefabs that are actually assigned.</summary>
    public GameObject PickRandomPrefab(int categoryIndex)
    {
        Category category = GetCategory(categoryIndex);
        if (category?.prefabs == null || category.prefabs.Length == 0)
            return null;

        int count = 0;
        for (int i = 0; i < category.prefabs.Length; i++)
        {
            if (category.prefabs[i] != null)
                count++;
        }

        if (count == 0)
            return null;

        int roll = UnityEngine.Random.Range(0, count);
        for (int i = 0; i < category.prefabs.Length; i++)
        {
            if (category.prefabs[i] == null)
                continue;
            if (roll == 0)
                return category.prefabs[i];
            roll--;
        }

        return null;
    }

    public int CountAssignedPrefabs(int categoryIndex)
    {
        Category category = GetCategory(categoryIndex);
        if (category?.prefabs == null)
            return 0;

        int count = 0;
        for (int i = 0; i < category.prefabs.Length; i++)
        {
            if (category.prefabs[i] != null)
                count++;
        }

        return count;
    }

    void Reset()
    {
        categories = new[]
        {
            new Category
            {
                displayName = "Grass",
                previewColor = new Color(0.45f, 0.78f, 0.32f, 1f),
                brushRadius = 4f,
                spacing = 1.2f,
                amount = 4,
                scaleMin = 0.8f,
                scaleMax = 1.2f
            },
            new Category
            {
                displayName = "Rocks",
                previewColor = new Color(0.62f, 0.58f, 0.52f, 1f),
                brushRadius = 5f,
                spacing = 2.8f,
                amount = 1,
                scaleMin = 0.85f,
                scaleMax = 1.25f
            },
            new Category
            {
                displayName = "Trees",
                previewColor = new Color(0.22f, 0.55f, 0.28f, 1f),
                brushRadius = 7f,
                spacing = 5.5f,
                amount = 1,
                scaleMin = 0.9f,
                scaleMax = 1.15f
            }
        };
        hover = PlanetSurfacePose.DefaultHover;
        randomYaw = true;
        yaw = 0f;
    }
}
