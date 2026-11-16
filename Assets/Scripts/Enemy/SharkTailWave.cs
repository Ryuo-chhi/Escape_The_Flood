using System.Collections.Generic;
using UnityEngine;

public class SharkTailWave : MonoBehaviour
{
    [Header("Tail swing (tweak live in Play mode)")]
    [Tooltip("Swing size as a fraction of body length (e.g. 0.12 = 12% of length)")]
    [Range(0f, 0.4f)]  public float amplitude = 0.12f;

    [Tooltip("Waves along the body. 0.7 - 0.9 produces an organic S-curve")]
    [Range(0.1f, 3f)]  public float cycles = 0.8f;

    [Tooltip("Swing speed in radians/sec (6 rad/s ~ 1 tail stroke per second)")]
    [Range(0f, 20f)]   public float speed = 6f;

    [Tooltip("0 = whole body waves, 0.3 = front 30% stays rigid, only rear waves")]
    [Range(0f, 0.9f)]  public float tailStart = 0.25f;

    [Tooltip("Higher = head stiffer, tail swings more (exponential curve)")]
    [Range(0.3f, 4f)]  public float falloff = 1.5f;

    [Tooltip("0 = still gliding/rest, 1 = full swing")]
    [Range(0f, 1f)]    public float swimAmount = 1f;

    [Header("Auto from movement (optional)")]
    [Tooltip("When enabled, swing scales automatically with shark movement speed")]
    public bool autoFromMovement = false;

    [Tooltip("World units/sec giving full swing when autoFromMovement is true")]
    public float fullSpeedAt = 3.5f;

    [Header("Fixes")]
    [Tooltip("False = head is at negative end, tail at positive end (default). True = head is at positive end.")]
    public bool headAtPositiveEnd = false;

    [ContextMenu("Flip Head and Tail Direction")]
    public void FlipHeadAndTail()
    {
        headAtPositiveEnd = !headAtPositiveEnd;
        Debug.Log($"[SharkTailWave] headAtPositiveEnd is now: {headAtPositiveEnd}. (False = head at negative/tail at positive)", this);
    }

    [Tooltip("Reverse left/right swing direction")]
    public bool flipSide = false;

    [Tooltip("Recalculate normals every frame. Preserves crisp flat-shaded low-poly facet lighting")]
    public bool recalcNormals = true;

    [Tooltip("Log initialization details to Console")]
    public bool logInfo = true;

    // Backwards-compatibility alias for external movement scripts (SharkAI)
    public float swim
    {
        get => swimAmount;
        set => swimAmount = value;
    }

    private SkinnedMeshRenderer smr;
    private MeshFilter mf;
    private Mesh mesh;
    private readonly List<Vector3> src = new List<Vector3>();
    private readonly List<Vector3> dst = new List<Vector3>();
    private int lenAxis;
    private int sideAxis;
    private float min;
    private float length;
    private float phase;
    private Vector3 lastPos;
    private string mode = "none";
    private bool wasDeformed = false;
    private GameObject bakedGo;

    private void Start()
    {
        // 1. Check for Static Batching conflict
        if (gameObject.isStatic)
        {
            Debug.LogError($"[SharkTailWave FAIL] GameObject '{gameObject.name}' is marked 'Static'! Static batching combines geometry and prevents vertex deformation. Uncheck 'Static' in the Inspector.", this);
            enabled = false;
            return;
        }

        // 2. Locate SkinnedMeshRenderer or MeshFilter in this object or children
        smr = GetComponentInChildren<SkinnedMeshRenderer>();
        mf  = GetComponentInChildren<MeshFilter>();

        if (smr != null && smr.sharedMesh != null)
        {
            if (!SetupSkinned()) return;
        }
        else if (mf != null && mf.sharedMesh != null)
        {
            if (!SetupStatic()) return;
        }
        else
        {
            Debug.LogError($"[SharkTailWave FAIL] No MeshFilter or SkinnedMeshRenderer found on '{gameObject.name}' or any of its children! Make sure SharkTailWave is on the shark root or child model.", this);
            enabled = false;
            return;
        }

        if (mesh == null)
        {
            Debug.LogError("[SharkTailWave FAIL] Mesh instance could not be created.", this);
            enabled = false;
            return;
        }

        mesh.GetVertices(src);
        if (src.Count == 0)
        {
            Debug.LogError($"[SharkTailWave FAIL] Mesh '{mesh.name}' has 0 vertices! Check model import settings.", this);
            enabled = false;
            return;
        }

        dst.Clear();
        dst.AddRange(src);

        // Compute body dimensions from undeformed mesh
        mesh.RecalculateBounds();
        Vector3 s = mesh.bounds.size;
        lenAxis  = (s.x >= s.y && s.x >= s.z) ? 0 : (s.y >= s.z ? 1 : 2);
        sideAxis = (lenAxis == 0) ? 2 : 0;
        min = mesh.bounds.min[lenAxis];
        length = Mathf.Max(s[lenAxis], 0.0001f);
        lastPos = transform.position;

        if (logInfo)
        {
            Debug.Log($"<color=cyan><b>[SharkTailWave OK]</b></color> Mode: {mode} | Target: '{(mf != null ? mf.gameObject.name : smr.gameObject.name)}' | Verts: {src.Count} | Spine Axis: {"XYZ"[lenAxis]} ({length:F2}m) | Side Axis: {"XYZ"[sideAxis]} | Head at +: {headAtPositiveEnd}", this);
        }
    }

    private bool SetupStatic()
    {
        if (mf.sharedMesh == null)
        {
            Debug.LogError($"[SharkTailWave FAIL] MeshFilter on '{mf.gameObject.name}' has no sharedMesh assigned!", mf);
            enabled = false;
            return false;
        }

        if (!mf.sharedMesh.isReadable)
        {
            Debug.LogError($"[SharkTailWave FAIL] Mesh '{mf.sharedMesh.name}' is NOT readable!\n" +
                           $"Fix: Select the model asset ('{mf.sharedMesh.name}.fbx') in Project window > Inspector > Model tab > enable 'Read/Write' > click Apply.", mf);
            enabled = false;
            return false;
        }

        // Instantiate unique mesh copy to avoid modifying original project asset on disk
        mesh = Instantiate(mf.sharedMesh);
        mesh.name = mf.sharedMesh.name + "_TailDeformed";
        mesh.MarkDynamic();
        mf.mesh = mesh;
        mode = "static mesh";
        return true;
    }

    private bool SetupSkinned()
    {
        if (smr.sharedMesh == null)
        {
            Debug.LogError($"[SharkTailWave FAIL] SkinnedMeshRenderer on '{smr.gameObject.name}' has no sharedMesh assigned!", smr);
            enabled = false;
            return false;
        }

        // Bake the animated skinned mesh each frame into a normal mesh we can bend
        mesh = new Mesh { name = smr.sharedMesh.name + "_BakedTail" };
        mesh.MarkDynamic();
        smr.BakeMesh(mesh);

        bakedGo = new GameObject(smr.gameObject.name + "_TailWaveBake");
        bakedGo.transform.SetParent(smr.transform.parent, false);
        bakedGo.transform.localPosition = smr.transform.localPosition;
        bakedGo.transform.localRotation = smr.transform.localRotation;
        bakedGo.transform.localScale    = smr.transform.localScale;

        var bakedMf = bakedGo.AddComponent<MeshFilter>();
        bakedMf.sharedMesh = mesh;
        var mr = bakedGo.AddComponent<MeshRenderer>();
        mr.sharedMaterials = smr.sharedMaterials;

        smr.forceRenderingOff = true; // hide original skinned mesh, keep bone animations ticking
        mode = "skinned (baked)";
        return true;
    }

    private void LateUpdate()
    {
        if (mesh == null || src.Count == 0) return;
        float dt = Time.deltaTime;

        // Auto velocity tracking
        if (autoFromMovement)
        {
            float v = Vector3.Distance(transform.position, lastPos) / Mathf.Max(dt, 0.0001f);
            float targetSwim = Mathf.Clamp01(v / Mathf.Max(fullSpeedAt, 0.01f));
            swimAmount = Mathf.Lerp(swimAmount, targetSwim, 1f - Mathf.Exp(-5f * dt));
        }
        lastPos = transform.position;

        // Smoothly settle to rest pose when stationary
        if (swimAmount <= 0.0005f)
        {
            if (wasDeformed)
            {
                mesh.SetVertices(src);
                mesh.RecalculateBounds();
                if (recalcNormals) mesh.RecalculateNormals();
                wasDeformed = false;
            }
            return;
        }

        wasDeformed = true;

        // Advance sine wave phase
        phase += speed * dt;
        if (phase > 628.3185f) phase -= 628.3185f; // wrap at 100 * 2*PI

        // For skinned meshes, bake the current frame's bone-animated pose first
        if (smr != null && smr.forceRenderingOff)
        {
            smr.BakeMesh(mesh);
            mesh.GetVertices(src);
        }

        float side = flipSide ? -1f : 1f;
        float twoPiCycles = cycles * Mathf.PI * 2f;
        float maxOffset = amplitude * length * swimAmount * side;
        float tailRange = Mathf.Max(1f - tailStart, 0.0001f);

        for (int i = 0; i < src.Count; i++)
        {
            Vector3 v = src[i];

            // Normalized position along body (0..1)
            float t = Mathf.Clamp01((v[lenAxis] - min) / length);
            if (headAtPositiveEnd) t = 1f - t; // 0 = head, 1 = tail tip

            // Stiff front isolation + exponential tail falloff mask
            float normalizedTail = Mathf.Clamp01((t - tailStart) / tailRange);
            float mask = Mathf.Pow(normalizedTail, falloff);

            // Backward-traveling sine wave
            float offset = Mathf.Sin(phase - (t * twoPiCycles)) * maxOffset * mask;

            v[sideAxis] += offset;
            dst[i] = v;
        }

        mesh.SetVertices(dst);
        mesh.RecalculateBounds();

        // RecalculateNormals updates face normals every frame for flat-shaded low-poly meshes
        if (recalcNormals)
        {
            mesh.RecalculateNormals();
        }
    }

    private void OnDisable()
    {
        if (smr != null)
        {
            smr.forceRenderingOff = false;
        }
        if (bakedGo != null)
        {
            bakedGo.SetActive(false);
        }
        if (mesh != null && src.Count > 0)
        {
            mesh.SetVertices(src);
            mesh.RecalculateBounds();
            if (recalcNormals) mesh.RecalculateNormals();
            wasDeformed = false;
        }
    }

    private void OnEnable()
    {
        if (smr != null)
        {
            smr.forceRenderingOff = true;
        }
        if (bakedGo != null)
        {
            bakedGo.SetActive(true);
        }
    }

    private void OnDestroy()
    {
        if (smr != null)
        {
            smr.forceRenderingOff = false;
        }
        if (bakedGo != null)
        {
            Destroy(bakedGo);
        }
        if (mesh != null)
        {
            Destroy(mesh);
        }
    }
}
